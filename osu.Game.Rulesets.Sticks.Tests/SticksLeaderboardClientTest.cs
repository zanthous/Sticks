#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.Sticks.Profiles;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksLeaderboardClientTest
    {
        [Test]
        public async Task TestOptInIdentitySummaryOnlyDedupeAndDisconnect()
        {
            using var storage = new TemporaryNativeStorage("sticks-online-test");
            var handler = new FakeService();
            using var http = new HttpClient(handler);
            var client = new SticksLeaderboardClient(storage, "https://sticks.example", http);
            var store = new SticksProfileStore(storage);
            store.Append(SticksProfileTest.Entry());
            store.Append(SticksProfileTest.Entry(2) with { UserId = 99 });
            await client.Sync(42);
            Assert.That(handler.Paths, Is.Empty, "Tracking must not silently enable uploads.");
            string? browser = null;
            await client.Connect(42, url => browser = url);
            Assert.That(browser, Is.EqualTo("https://sticks.example/auth/browser?id=test"));
            Assert.That(client.IsConnected(42), Is.True);
            Assert.That(client.IsConnected(99), Is.False);
            await client.Sync(42);
            await client.Sync(42);
            Assert.That(handler.Summaries, Has.Count.EqualTo(1), "Unchanged PBs are not uploaded repeatedly.");
            using var summary = JsonDocument.Parse(handler.Summaries[0]);
            Assert.Multiple(() =>
            {
                Assert.That(summary.RootElement.GetProperty("userId").GetInt32(), Is.EqualTo(42));
                Assert.That(summary.RootElement.GetProperty("mods").GetInt32(), Is.Zero);
                Assert.That(summary.RootElement.GetProperty("judgements").GetArrayLength(), Is.EqualTo(16));
                Assert.That(summary.RootElement.TryGetProperty("replay", out _), Is.False);
                Assert.That(summary.RootElement.TryGetProperty("hitEvents", out _), Is.False);
            });
            store.Append(SticksProfileTest.Entry(1, 150));
            await client.Sync(42);
            Assert.That(handler.Summaries, Has.Count.EqualTo(2));
            client.Disconnect();
            Assert.That(client.IsConnected(42), Is.False);
            await client.Sync(42);
            Assert.That(handler.Summaries, Has.Count.EqualTo(2));
            var otherServer = new SticksLeaderboardClient(storage, "https://different.example", http);
            Assert.That(otherServer.IsConnected(42), Is.False, "Sessions must not cross service origins.");
        }

        [Test]
        public async Task TestNoServiceMeansNoNetworkAndNoUpload()
        {
            using var storage = new TemporaryNativeStorage("sticks-offline-test");
            var handler = new FakeService();
            using var http = new HttpClient(handler);
            foreach (string url in new[] { "", "http://sticks.example", "https://user:password@sticks.example" })
            {
                var client = new SticksLeaderboardClient(storage, url, http);
                Assert.That(client.IsConfigured, Is.False);
                await client.Sync(42);
            }
            Assert.That(handler.Paths, Is.Empty);
        }

        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public async Task TestDeferredSubmissionRetainsLocalScoreAndCanBeRetried(HttpStatusCode status)
        {
            using var storage = new TemporaryNativeStorage("sticks-deferred-test");
            var handler = new FakeService { NextSubmissionFailure = status };
            using var http = new HttpClient(handler);
            var client = new SticksLeaderboardClient(storage, "https://sticks.example", http);
            var store = new SticksProfileStore(storage);
            var score = SticksProfileTest.Entry();
            store.Append(score);
            await client.Connect(42, _ => { });

            Assert.CatchAsync(async () => await client.Sync(42));
            Assert.That(store.Read(), Has.Length.EqualTo(1), "A deferred upload must not delete the local play.");
            Assert.That(store.Read()[0].ScoreId, Is.EqualTo(score.ScoreId));
            Assert.That(handler.Summaries, Has.Count.EqualTo(1), "Do not immediately loop on a failed submission.");

            // A later sync (including after a restart) retries the unacknowledged score.
            client = new SticksLeaderboardClient(storage, "https://sticks.example", http);
            await client.Sync(42);
            Assert.That(handler.Summaries, Has.Count.EqualTo(2));
            Assert.That(handler.Summaries[1], Is.EqualTo(handler.Summaries[0]));
            await client.Sync(42);
            Assert.That(handler.Summaries, Has.Count.EqualTo(2), "Only accepted submissions may be marked uploaded.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task TestRejectsAnotherRulesetsPolicyOrSubmissionReceipt(bool wrongPolicy)
        {
            using var storage = new TemporaryNativeStorage("sticks-ruleset-test");
            var handler = new FakeService
            {
                PolicyRuleset = wrongPolicy ? "test-mode" : "sticks",
                SubmissionRuleset = wrongPolicy ? "sticks" : "test-mode",
            };
            using var http = new HttpClient(handler);
            var client = new SticksLeaderboardClient(storage, "https://sticks.example", http);
            new SticksProfileStore(storage).Append(SticksProfileTest.Entry());
            await client.Connect(42, _ => { });
            Assert.ThrowsAsync<InvalidDataException>(async () => await client.Sync(42));
            Assert.That(handler.Summaries, Has.Count.EqualTo(wrongPolicy ? 0 : 1));
            if (wrongPolicy) Assert.That(client.WhitelistedSets, Is.Empty);

            handler.PolicyRuleset = handler.SubmissionRuleset = "sticks";
            await client.Sync(42);
            Assert.That(handler.Summaries, Has.Count.EqualTo(wrongPolicy ? 1 : 2), "A different ruleset's receipt must not acknowledge this play.");
            Assert.That(handler.Paths, Does.Contain("/v1/rulesets/sticks/policy"));
            Assert.That(handler.Paths, Does.Contain("/v1/rulesets/sticks/scores"));
            Assert.That(handler.Paths, Does.Not.Contain("/v1/scores"));
        }

        [Test]
        public async Task TestRankingsMustBeForSticks()
        {
            using var storage = new TemporaryNativeStorage("sticks-ranking-test");
            var handler = new FakeService { PlayersRuleset = "test-mode" };
            using var http = new HttpClient(handler);
            var client = new SticksLeaderboardClient(storage, "https://sticks.example", http);
            Assert.ThrowsAsync<InvalidDataException>(async () => await client.GetPlayers(0));
            handler.PlayersRuleset = "sticks";
            Assert.That(await client.GetPlayers(0), Is.Empty);
            Assert.That(handler.Paths, Is.All.EqualTo("/v1/rulesets/sticks/leaderboard"));
        }

        private sealed class FakeService : HttpMessageHandler
        {
            public List<string> Paths { get; } = new List<string>();
            public List<string> Summaries { get; } = new List<string>();
            public HttpStatusCode? NextSubmissionFailure { get; set; }
            public string PolicyRuleset { get; set; } = "sticks";
            public string SubmissionRuleset { get; set; } = "sticks";
            public string PlayersRuleset { get; set; } = "sticks";
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string path = request.RequestUri!.AbsolutePath;
                Paths.Add(path);
                string response = path switch
                {
                    "/auth/start" => "{\"id\":\"test\",\"url\":\"https://sticks.example/auth/browser?id=test\"}",
                    "/auth/complete" => JsonSerializer.Serialize(new { pending = false, userId = 42, token = new string('a', 64), expires = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds() }),
                    "/v1/rulesets/sticks/policy" => JsonSerializer.Serialize(new { ruleset = PolicyRuleset, generation = 1, whitelistedSets = new[] { 10 } }),
                    "/v1/rulesets/sticks/scores" => JsonSerializer.Serialize(new { ruleset = SubmissionRuleset, accepted = true }),
                    "/v1/rulesets/sticks/leaderboard" => JsonSerializer.Serialize(new { ruleset = PlayersRuleset, entries = Array.Empty<object>() }),
                    "/v1/disconnect" => "{\"disconnected\":true}",
                    _ => throw new InvalidOperationException(path),
                };
                if (path == "/v1/rulesets/sticks/scores")
                {
                    Assert.That(request.Headers.Authorization?.Parameter, Is.EqualTo(new string('a', 64)));
                    Summaries.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                    if (NextSubmissionFailure is HttpStatusCode failure)
                    {
                        NextSubmissionFailure = null;
                        return new HttpResponseMessage(failure) { Content = new StringContent("{\"error\":\"retry later\"}") };
                    }
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
            }
        }
    }
}
