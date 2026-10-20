#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.Sticks.Profiles;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksReplaySharingTest
    {
        private const string md5 = "0123456789abcdef0123456789abcdef";

        [Test]
        public async Task TestSyncUploadsEachAcceptedTopPlaysReplayOnce()
        {
            using var storage = new TemporaryNativeStorage("sticks-replay-upload-test");
            var service = new FakeService { MaxDurationSeconds = 2 };
            using var http = new HttpClient(service);
            var client = new SticksLeaderboardClient(storage, "https://sticks.example", http);
            var short_ = SticksProfileTest.Entry(1, 200);
            var long_ = SticksProfileTest.Entry(2, 100);
            var profile = new SticksProfileStore(storage);
            profile.Append(short_);
            profile.Append(long_);
            var replays = new SticksReplayStore(storage);
            byte[] shortRecording = Recording(60);
            Assert.That(replays.SaveRaw(short_.ScoreId, shortRecording), Is.True);
            Assert.That(replays.SaveRaw(long_.ScoreId, Recording(300)), Is.True, "~5 s of frames, over the 2 s limit");

            await client.Connect(42, _ => { });
            await client.Sync(42);

            Assert.That(service.Uploads.Keys, Is.EquivalentTo(new[] { $"/v1/rulesets/sticks/users/42/scores/1/{short_.ScoreId:N}/replay" }),
                "Only the accepted play within the duration limit is uploaded.");
            Assert.That(gunzip(service.Uploads.Values.Single()), Is.EqualTo(shortRecording));

            await client.Sync(42);
            Assert.That(service.Puts, Is.EqualTo(1), "Uploaded and permanently ineligible replays are not sent again.");
        }

        [Test]
        public async Task TestTemporaryRefusalIsRetriedOnTheNextSync()
        {
            using var storage = new TemporaryNativeStorage("sticks-replay-retry-test");
            var service = new FakeService { NextUploadStatus = HttpStatusCode.TooManyRequests };
            using var http = new HttpClient(service);
            var client = new SticksLeaderboardClient(storage, "https://sticks.example", http);
            var play = SticksProfileTest.Entry();
            new SticksProfileStore(storage).Append(play);
            new SticksReplayStore(storage).SaveRaw(play.ScoreId, Recording(30));

            await client.Connect(42, _ => { });
            await client.Sync(42);
            Assert.That(service.Uploads, Is.Empty);
            await client.Sync(42);
            Assert.That(service.Uploads, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task TestTopPlaysAndDownloads()
        {
            using var storage = new TemporaryNativeStorage("sticks-replay-download-test");
            var service = new FakeService();
            using var http = new HttpClient(service);
            var client = new SticksLeaderboardClient(storage, "https://sticks.example", http);

            var page = await client.GetTopPlays(7);
            Assert.That(service.Requests, Does.Contain("/v1/rulesets/sticks/users/7/top?limit=100"));
            var play = page.Entries.Single();
            Assert.That(play.Judgements, Has.Length.EqualTo(16));
            Assert.That(play.Replay?.Available, Is.True);

            byte[] downloaded = await client.DownloadReplay(play.Replay!.DownloadPath!);
            Assert.That(SticksReplayPackage.Decode(downloaded, Guid.ParseExact(play.ScoreId, "N"), md5, 1 << 20), Is.EqualTo(service.Recording));
            Assert.ThrowsAsync<InvalidDataException>(async () => await client.DownloadReplay("https://elsewhere.example/replay"));
            Assert.ThrowsAsync<InvalidDataException>(async () => await client.DownloadReplay("/v1/rulesets/sticks/users/7/../../../secrets"));
        }

        [Test]
        public void TestDownloadedReplaysAreVerifiedAgainstThePlay()
        {
            var scoreId = Guid.NewGuid();
            byte[] raw = Recording(40);
            Assert.That(SticksReplayPackage.Decode(Package(scoreId, raw), scoreId, md5, 1 << 20), Is.EqualTo(raw));

            byte[] tampered = Package(scoreId, raw);
            tampered[^5] ^= 0xff;
            Assert.Throws<InvalidDataException>(() => SticksReplayPackage.Decode(tampered, scoreId, md5, 1 << 20), "A changed recording fails its hash.");
            Assert.Throws<InvalidDataException>(() => SticksReplayPackage.Decode(Package(scoreId, raw), Guid.NewGuid(), md5, 1 << 20), "Another play's replay.");
            Assert.Throws<InvalidDataException>(() => SticksReplayPackage.Decode(Package(scoreId, raw), scoreId, new string('f', 32), 1 << 20), "Another beatmap revision.");
            Assert.Throws<InvalidDataException>(() => SticksReplayPackage.Decode(Package(scoreId, raw, generation: 2), scoreId, md5, 1 << 20), "Another conversion generation.");
            Assert.Throws<InvalidDataException>(() => SticksReplayPackage.Decode(Package(scoreId, raw), scoreId, md5, raw.Length - 1), "Inflation is bounded.");
            Assert.Throws<InvalidDataException>(() => SticksReplayPackage.Decode(Encoding.ASCII.GetBytes("not a replay"), scoreId, md5, 1 << 20));
        }

        [Test]
        public void TestDownloadedReplaysRestoreTheirThresholdAndSurviveLocalCleanup()
        {
            using var storage = new TemporaryNativeStorage("sticks-downloaded-replay-test");
            var scoreId = Guid.NewGuid();
            Assert.That(SticksReplayStore.Downloads(storage).SaveRaw(scoreId, Recording(30)), Is.True);

            var local = new SticksReplayStore(storage);
            // Local cleanup removes recordings without a local score; another player's download never has one.
            local.DeleteOrphans(_ => false);
            var score = new Score { ScoreInfo = new ScoreInfo { ID = scoreId } };
            Assert.That(local.TryRestore(score, out _), Is.False);

            Assert.That(SticksReplayStore.Downloads(storage).TryRestore(score, out float threshold), Is.True);
            Assert.That(threshold, Is.EqualTo(0.97f), "Playback uses the threshold the replay was recorded with.");
            Assert.That(score.Replay.Frames, Has.Count.EqualTo(30));
        }

        /// <summary>
        /// A raw .stkr v5 recording with frames 16.67 ms apart.
        /// </summary>
        internal static byte[] Recording(int frames)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(0x53544B52);
            writer.Write(5);
            writer.Write(0.97f);
            for (int i = 0; i < frames; i++)
            {
                writer.Write(i * 16.67);
                writer.Write(MathF.Sin(i / 10f));
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(MathF.Cos(i / 10f));
                for (int button = 0; button < 6; button++) writer.Write(false);
            }

            writer.Flush();
            return stream.ToArray();
        }

        internal static byte[] Package(Guid scoreId, byte[] raw, int generation = 1)
        {
            byte[] compressed = SticksReplayPackage.Compress(raw);
            byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new
            {
                formatVersion = 1, ruleset = "sticks", player = new { userId = 7, username = "Other" },
                score = new { scoreId = scoreId.ToString("N"), sourceMd5 = md5, generation },
                recording = new { format = "stkr", version = 5, encoding = "gzip", sha256 = Convert.ToHexString(SHA256.HashData(compressed)).ToLowerInvariant() },
            });
            var package = new List<byte>(Encoding.ASCII.GetBytes("ZKR1"));
            package.AddRange(BitConverter.GetBytes((uint)metadata.Length));
            package.AddRange(metadata);
            package.AddRange(compressed);
            return package.ToArray();
        }

        private static byte[] gunzip(byte[] compressed)
        {
            using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

        private sealed class FakeService : HttpMessageHandler
        {
            public int MaxDurationSeconds { get; init; } = 1800;
            public HttpStatusCode? NextUploadStatus { get; set; }
            public Dictionary<string, byte[]> Uploads { get; } = new Dictionary<string, byte[]>();
            public List<string> Requests { get; } = new List<string>();
            public int Puts { get; private set; }
            public readonly byte[] Recording = SticksReplaySharingTest.Recording(20);
            private readonly Guid remoteScore = Guid.NewGuid();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string path = request.RequestUri!.AbsolutePath;
                Requests.Add(request.RequestUri.PathAndQuery);

                if (request.Method == HttpMethod.Put)
                {
                    Puts++;
                    Assert.That(request.Content!.Headers.ContentType?.MediaType, Is.EqualTo("application/gzip"));
                    Assert.That(request.Content.Headers.ContentEncoding, Is.Empty, "The gzip body is the file itself, not a transfer encoding.");
                    Assert.That(request.Headers.Authorization?.Parameter, Is.EqualTo(new string('a', 64)));
                    if (NextUploadStatus is HttpStatusCode refusal)
                    {
                        NextUploadStatus = null;
                        return new HttpResponseMessage(refusal);
                    }

                    Uploads[path] = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                    return json(HttpStatusCode.Created, new { stored = true, alreadyStored = false });
                }

                if (path.EndsWith("/replay", StringComparison.Ordinal))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Package(remoteScore, Recording)) };

                return path switch
                {
                    "/auth/start" => json(HttpStatusCode.OK, new { id = "test", url = "https://sticks.example/auth/browser?id=test" }),
                    "/auth/complete" => json(HttpStatusCode.OK, new { pending = false, userId = 42, token = new string('a', 64), expires = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds() }),
                    "/v1/rulesets/sticks/policy" => json(HttpStatusCode.OK, new
                    {
                        ruleset = "sticks", generation = 1, whitelistedSets = Array.Empty<int>(),
                        replays = new { enabled = true, retainedPerPlayer = 100, maxDurationSeconds = MaxDurationSeconds, maxCompressedBytes = 4 << 20, maxRawBytes = 8 << 20 },
                    }),
                    "/v1/rulesets/sticks/scores" => json(HttpStatusCode.OK, new { ruleset = "sticks", accepted = true }),
                    "/v1/rulesets/sticks/users/7/top" => json(HttpStatusCode.OK, new
                    {
                        ruleset = "sticks", player = new { userId = 7, username = "Other", pp = 123.4 }, total = 1,
                        entries = new[]
                        {
                            new
                            {
                                rank = 1, scoreId = remoteScore.ToString("N"), userId = 7, beatmapId = 3, beatmapSetId = 10, sourceMd5 = md5,
                                playedAt = 1792417969000, generation = 1, pp = 123.4, accuracy = 0.97, maxCombo = 400, totalScore = 900000,
                                activationThreshold = 0.97, title = "Song", artist = "Artist", difficulty = "Hard",
                                judgements = Enumerable.Range(0, 16).ToArray(),
                                replay = new { eligible = true, available = true, downloadPath = $"/v1/rulesets/sticks/users/7/scores/3/{remoteScore:N}/replay" },
                            },
                        },
                    }),
                    _ => throw new InvalidOperationException(path),
                };
            }

            private static HttpResponseMessage json(HttpStatusCode status, object body) =>
                new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        }
    }
}
