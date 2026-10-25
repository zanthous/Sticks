#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Rulesets.Sticks.Replays;

namespace osu.Game.Rulesets.Sticks.Profiles
{
    // This client never reads lazer's access token. Replays of top plays are uploaded only for players who opted in to score sharing.
    internal sealed class SticksLeaderboardClient
    {
        private const string settings_file = "online.json";
        private const string ruleset_id = "sticks";
        private const string ruleset_path = "v1/rulesets/" + ruleset_id;
        private static readonly object settingsLock = new object();
        private static readonly SemaphoreSlim syncLock = new SemaphoreSlim(1, 1);
        private static readonly HttpClient sharedHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        private readonly HttpClient http;
        private static readonly JsonSerializerOptions json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
        private readonly Storage storage;
        private readonly Storage root;
        private readonly string origin;

        public SticksLeaderboardClient(Storage root)
            : this(root, typeof(SticksLeaderboardClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                                .FirstOrDefault(a => a.Key == "SticksLeaderboardUrl")?.Value ?? "", sharedHttp)
        {
        }

        internal SticksLeaderboardClient(Storage root, string configured, HttpClient http)
        {
            this.http = http;
            this.root = root;
            storage = root.GetStorageForDirectory("ruleset-data/sticks/profile");
            origin = Uri.TryCreate(configured, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0
                ? configured.TrimEnd('/') : "";
        }

        public bool IsConfigured => origin.Length > 0;
        public HashSet<int> WhitelistedSets => new HashSet<int>(read().WhitelistedSets);
        public bool IsConnected(int userId) { var state = read(); return IsConfigured && state.UserId == userId && state.Token.Length > 0 && state.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }

        public void Disconnect()
        {
            var state = read();
            update(s => { s.Token = ""; s.UserId = 0; s.Expires = 0; s.Uploaded.Clear(); s.Replays.Clear(); });
            if (state.Token.Length > 0) _ = revoke(state.Token);
        }

        private async Task revoke(string token)
        {
            try { await request<JsonElement>("v1/disconnect", new { }, token).ConfigureAwait(false); }
            catch { /* Local opt-out takes effect even while offline; server sessions also expire. */ }
        }

        public async Task Connect(int userId, Action<string> openBrowser, CancellationToken cancellationToken = default)
        {
            if (userId <= 0) throw new InvalidOperationException("Log in to osu! first");
            string verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            string challenge = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(verifier))).ToLowerInvariant();
            var start = await request<LoginStart>("auth/start", new { userId, challenge }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!Uri.TryCreate(start.Url, UriKind.Absolute, out var url) || url.GetLeftPart(UriPartial.Authority) != new Uri(origin).GetLeftPart(UriPartial.Authority))
                throw new InvalidDataException("The sign-in address does not belong to the configured service.");
            openBrowser(start.Url);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
                var result = await request<LoginComplete>("auth/complete", new { id = start.Id, verifier }, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.Pending) continue;
                if (result.UserId != userId || result.Token.Length != 64) throw new InvalidDataException("Sign-in returned a different account.");
                cancellationToken.ThrowIfCancellationRequested();
                update(state =>
                {
                    // Renewing an expired session must not re-send every top play; disconnecting clears this record.
                    if (state.UserId != userId)
                    {
                        state.Uploaded.Clear();
                        state.Replays.Clear();
                    }
                    state.UserId = userId;
                    state.Token = result.Token;
                    state.Expires = result.Expires;
                });
                return;
            }
            throw new InvalidOperationException("Sign-in timed out; try connecting again");
        }

        public async Task Sync(int userId)
        {
            if (!await syncLock.WaitAsync(0).ConfigureAwait(false)) return;
            try
            {
                var state = read();
                if (!IsConnected(userId)) return;
                var policy = await request<Policy>($"{ruleset_path}/policy").ConfigureAwait(false);
                requireSticks(policy.Ruleset);
                if (policy.Generation != SticksProfileScore.CurrentGeneration) throw new InvalidOperationException("Update Sticks to submit to the current leaderboard");
                update(s => s.WhitelistedSets = policy.WhitelistedSets);
                var records = new SticksProfileStore(root).Read();
                var whitelist = new HashSet<int>(policy.WhitelistedSets);
                var candidates = SticksProfilePolicy.TopPlays(records, userId, whitelist).Take(100).ToArray();
                foreach (var score in candidates)
                {
                    if (!IsConnected(userId)) break;
                    string key = $"{ruleset_id}:{score.Generation}:{score.BeatmapId}";
                    if (state.Uploaded.TryGetValue(key, out Guid uploaded) && uploaded == score.ScoreId) continue;
                    // Bounded, explicit fields. No score file, hit-event stream or replay is serialised.
                    var submission = await request<Submission>($"{ruleset_path}/scores", new
                    {
                        scoreId = score.ScoreId.ToString("N"), score.UserId, score.BeatmapId, score.BeatmapSetId,
                        score.SourceMd5, score.PlayedAt, score.Generation, score.DifficultyVersion, score.Mods,
                        score.Rate, score.PP, score.Accuracy, score.MaxCombo, score.TotalScore, score.ActivationThreshold,
                        score.Judgements,
                    }, state.Token).ConfigureAwait(false);
                    requireSticks(submission.Ruleset);
                    if (submission.Accepted)
                        update(s => { if (s.UserId == userId && s.Token == state.Token) s.Uploaded[key] = score.ScoreId; });
                }

                if (policy.Replays is { Enabled: true } replays)
                    await uploadReplays(userId, state.Token, replays, candidates.Take(replays.RetainedPerPlayer)).ConfigureAwait(false);
            }
            finally { syncLock.Release(); }
        }

        // Each accepted top play's recording is sent once. Recordings the service could never accept are not retried;
        // temporary refusals (busy, storage full, score still changing) are retried on a later sync.
        private async Task uploadReplays(int userId, string token, ReplayPolicy policy, IEnumerable<SticksProfileScore> plays)
        {
            var store = new SticksReplayStore(root);
            foreach (var score in plays)
            {
                if (!IsConnected(userId)) return;
                var state = read();
                string key = $"{ruleset_id}:{score.Generation}:{score.BeatmapId}";
                if (!state.Uploaded.TryGetValue(key, out Guid accepted) || accepted != score.ScoreId) continue;
                if (state.Replays.TryGetValue(key, out Guid handled) && handled == score.ScoreId) continue;

                // Missing (replays disabled), an older recording format, longer or larger than allowed.
                byte[]? raw = store.ReadForUpload(score.ScoreId, policy.MaxDurationSeconds * 1000.0);
                byte[]? body = raw == null || raw.Length > policy.MaxRawBytes ? null : SticksReplayPackage.Compress(raw);
                if (body == null || body.Length > policy.MaxCompressedBytes)
                {
                    markReplay(userId, token, key, score.ScoreId);
                    continue;
                }

                switch ((int)await putReplay($"{ruleset_path}/users/{userId}/scores/{score.BeatmapId}/{score.ScoreId:N}/replay", body, token).ConfigureAwait(false))
                {
                    case 200:
                    case 201:
                    case 400:
                    case 413:
                    case 415:
                        markReplay(userId, token, key, score.ScoreId);
                        break;

                    case 401:
                        throw new InvalidOperationException("Reconnect your osu! account to share scores");

                    case 403:
                        throw new InvalidOperationException("This account is banned from the Sticks leaderboard; local play is unaffected");

                    case 409:
                        break;

                    default:
                        return;
                }
            }
        }

        private void markReplay(int userId, string token, string key, Guid scoreId) =>
            update(s => { if (s.UserId == userId && s.Token == token) s.Replays[key] = scoreId; });

        private async Task<HttpStatusCode> putReplay(string path, byte[] body, string token)
        {
            using var message = new HttpRequestMessage(HttpMethod.Put, $"{origin}/{path}") { Content = new ByteArrayContent(body) };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            return response.StatusCode;
        }

        public async Task<TopPlays> GetTopPlays(int userId)
        {
            var page = await request<TopPlays>($"{ruleset_path}/users/{userId}/top?limit=100").ConfigureAwait(false);
            requireSticks(page.Ruleset);
            return page;
        }

        // Null when the service has no shared plays for this player.
        public async Task<TopPlays?> FindTopPlays(int userId)
        {
            try { return await GetTopPlays(userId).ConfigureAwait(false); }
            catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound) { return null; }
        }

        // The service caps compressed recordings at 8 MiB; metadata is at most 16 KiB.
        private const int max_download_bytes = 8 * 1024 * 1024 + SticksReplayPackage.MaxMetadataBytes + 8;

        public async Task<byte[]> DownloadReplay(string downloadPath, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("Online leaderboard is not configured in this build");
            if (!downloadPath.StartsWith($"/{ruleset_path}/users/", StringComparison.Ordinal) || downloadPath.Contains(".."))
                throw new InvalidDataException("The service returned an unexpected replay location.");
            using var response = await http.GetAsync($"{origin}{downloadPath}", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("This replay is no longer available");
            if ((int)response.StatusCode == 429) throw new InvalidOperationException("The leaderboard is busy; try again later");
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(max_download_bytes, cancellationToken).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<Player[]> GetPlayers(int offset)
        {
            var players = await request<Players>($"{ruleset_path}/leaderboard?offset={offset}").ConfigureAwait(false);
            requireSticks(players.Ruleset);
            return players.Entries;
        }

        private static void requireSticks(string ruleset)
        {
            if (ruleset != ruleset_id) throw new InvalidDataException("The service returned data for a different ruleset.");
        }

        public static async Task TrySync(Storage storage, int userId)
        {
            try
            {
                var client = new SticksLeaderboardClient(storage);
                if (client.IsConnected(userId)) await client.Sync(userId).ConfigureAwait(false);
            }
            catch (Exception e) { Logger.Log($"Sticks leaderboard sync deferred ({e.GetType().Name}); local scores are retained.", level: LogLevel.Important); }
        }

        private async Task<T> request<T>(string path, object? body = null, string? token = null, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("Online leaderboard is not configured in this build");
            using var message = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, $"{origin}/{path}");
            if (body != null) message.Content = new StringContent(JsonSerializer.Serialize(body, json), Encoding.UTF8, "application/json");
            if (token != null) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("Reconnect your osu! account to share scores");
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new InvalidOperationException("This account is banned from the Sticks leaderboard; local play is unaffected");
            if ((int)response.StatusCode == 429) throw new InvalidOperationException("The leaderboard is busy; try again later");
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(256 * 1024, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), json)
                   ?? throw new InvalidDataException("Empty leaderboard response.");
        }

        private State read()
        {
            lock (settingsLock)
            {
                if (!storage.Exists(settings_file)) return new State { Origin = origin };
                try
                {
                    using var stream = storage.GetStream(settings_file, FileAccess.Read);
                    if (stream.Length > 1024 * 1024) return new State { Origin = origin };
                    var state = JsonSerializer.Deserialize<State>(stream, json);
                    return state?.Origin == origin ? state : new State { Origin = origin };
                }
                catch (JsonException) { return new State { Origin = origin }; }
            }
        }

        private void update(Action<State> action)
        {
            lock (settingsLock)
            {
                var state = read();
                action(state);
                const string temporary = settings_file + ".tmp";
                using (var stream = storage.GetStream(temporary, FileAccess.Write, FileMode.Create)) JsonSerializer.Serialize(stream, state, json);
                File.Move(storage.GetFullPath(temporary), storage.GetFullPath(settings_file), true);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(storage.GetFullPath(settings_file), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        private sealed class State
        {
            public string Origin { get; set; } = "";
            public int UserId { get; set; }
            public string Token { get; set; } = "";
            public long Expires { get; set; }
            public int[] WhitelistedSets { get; set; } = Array.Empty<int>();
            public Dictionary<string, Guid> Uploaded { get; set; } = new Dictionary<string, Guid>();
            public Dictionary<string, Guid> Replays { get; set; } = new Dictionary<string, Guid>();
        }

        private sealed record LoginStart(string Id, string Url);
        private sealed record LoginComplete(bool Pending, int UserId, string Token, long Expires);
        private sealed record Policy(string Ruleset, int Generation, int[] WhitelistedSets, ReplayPolicy? Replays);
        private sealed record ReplayPolicy(bool Enabled, int RetainedPerPlayer, int MaxDurationSeconds, int MaxCompressedBytes, int MaxRawBytes);
        private sealed record Submission(string Ruleset, bool Accepted);
        private sealed record Players(string Ruleset, Player[] Entries);
        internal sealed record Player(int Rank, int UserId, string Username, double PP);
        internal sealed record TopPlays(string Ruleset, TopPlayer Player, int Total, RemotePlay[] Entries);
        internal sealed record TopPlayer(int UserId, string Username, double? PP);
        internal sealed record RemotePlay(int? Rank, string ScoreId, int BeatmapId, int BeatmapSetId, string SourceMd5, long PlayedAt,
                                          int Generation, double PP, double Accuracy, int MaxCombo, long TotalScore, float ActivationThreshold,
                                          string? Title, string? Artist, string? Difficulty, int[] Judgements, RemoteReplay? Replay)
        {
            // Shared plays have already passed the service's ranked, revision and generation checks.
            public SticksProfileScore ToScore(int userId) => new SticksProfileScore
            {
                ScoreId = Guid.TryParseExact(ScoreId, "N", out Guid id) ? id : Guid.Empty, UserId = userId, BeatmapId = BeatmapId,
                BeatmapSetId = BeatmapSetId, SourceMd5 = SourceMd5, PlayedAt = PlayedAt, Generation = Generation, PP = PP,
                Accuracy = Accuracy, MaxCombo = MaxCombo, TotalScore = TotalScore, ActivationThreshold = ActivationThreshold,
                Ranked = true, MatchesPublishedMap = true, Judgements = Judgements,
            };
        }

        internal sealed record RemoteReplay(bool Eligible, bool Available, string? DownloadPath);
    }
}
