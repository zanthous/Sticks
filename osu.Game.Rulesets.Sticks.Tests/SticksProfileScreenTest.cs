#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.Sticks.Profiles;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Rulesets.Sticks.UI.Profile;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    public partial class SticksProfileScreenTest : OsuTestScene
    {
        protected override bool UseFreshStoragePerRun => true;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        // Local storage outlives each test in this fixture; one test's plays must not change another's totals.
        [SetUpSteps]
        public void ResetSticksData() => AddStep("reset Sticks data", () =>
        {
            if (LocalStorage.ExistsDirectory("ruleset-data/sticks"))
                LocalStorage.DeleteDirectory("ruleset-data/sticks");
        });

        [Test]
        public void TestRecordedPlayWithMissingScoreAndReplayDisplaysSafely()
        {
            SticksProfileScreen screen = null!;
            AddStep("seed a newly tracked play", () =>
            {
                new SticksProfileStore(LocalStorage).Append(SticksProfileTest.Entry() with { UserId = System.Math.Max(0, API.LocalUser.Value.Id) });
                Child = new ProfileContainer(LocalStorage)
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new ScreenStack(screen = new SticksProfileScreen()) { RelativeSizeAxes = Axes.Both },
                };
            });
            AddUntilStep("profile loads", () => screen.ChildrenOfType<SticksProfilePlayRow>().Any());
            AddAssert("summary shows total pp", () => screen.ChildrenOfType<SticksProfileStatistics>().Single()
                                                            .ChildrenOfType<OsuSpriteText>().Any(t => t.Text.ToString() == "100"));
            AddAssert("row title is safe and truncates", () => screen.ChildrenOfType<TruncatingSpriteText>().Any(t => t.Text.ToString().Contains("Beatmap 1")));
            AddAssert("missing replay has no watch button", () => !screen.ChildrenOfType<SticksProfilePlayRow.WatchReplayButton>().Any());
        }

        [Test]
        public void TestPlayWithSavedReplayCanBeWatched()
        {
            SticksProfileScreen screen = null!;
            AddStep("seed a play with its score, beatmap and replay", () =>
            {
                Guid scoreId = Guid.NewGuid();
                Realm.Write(r =>
                {
                    RulesetInfo osu = r.Find<RulesetInfo>("osu") ?? r.Add(rulesets.GetRuleset("osu")!.Clone());
                    var set = new BeatmapSetInfo { OnlineID = 10 };
                    var beatmap = new BeatmapInfo(osu, new BeatmapDifficulty(), new BeatmapMetadata { Title = "Replay Song", Artist = "Someone" })
                    {
                        OnlineID = 1, DifficultyName = "Hard", BeatmapSet = set,
                    };
                    set.Beatmaps.Add(beatmap);
                    r.Add(set);
                    r.Add(new ScoreInfo(beatmap, osu) { ID = scoreId });
                });
                using (Stream replay = LocalStorage.GetStorageForDirectory("ruleset-data/sticks/replays").GetStream($"{scoreId:N}.stkr", FileAccess.Write, FileMode.Create))
                    replay.WriteByte(0);
                new SticksProfileStore(LocalStorage).Append(SticksProfileTest.Entry() with { ScoreId = scoreId, UserId = Math.Max(0, API.LocalUser.Value.Id) });

                Child = new ProfileContainer(LocalStorage, new ScoreManager(rulesets, () => beatmaps, LocalStorage, Realm, API))
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new ScreenStack(screen = new SticksProfileScreen()) { RelativeSizeAxes = Axes.Both },
                };
            });
            AddUntilStep("profile loads", () => screen.ChildrenOfType<SticksProfilePlayRow>().Any());
            AddAssert("installed beatmap title is shown", () => screen.ChildrenOfType<TruncatingSpriteText>().Any(t => t.Text.ToString() == "Replay Song"));
            AddAssert("saved replay has a watch button", () => screen.ChildrenOfType<SticksProfilePlayRow.WatchReplayButton>().Single().Enabled.Value);
        }

        [Test]
        public void TestLeaderboardTabListsPlayers()
        {
            SticksProfileScreen screen = null!;
            AddStep("open profile with a leaderboard", () =>
            {
                var http = new HttpClient(new LeaderboardService());
                Child = new ProfileContainer(LocalStorage)
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new ScreenStack(screen = new SticksProfileScreen(s => new SticksLeaderboardClient(s, "https://sticks.example", http)))
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                };
            });
            AddUntilStep("profile loads", () => screen.IsLoaded);
            AddAssert("leaderboard is not fetched for players who have not opted in", () => !screen.ChildrenOfType<SticksLeaderboardRow>().Any());
            AddStep("switch to leaderboard", () => screen.ChildrenOfType<OsuTabControl<SticksProfileTab>>().Single().Current.Value = SticksProfileTab.Leaderboard);
            AddUntilStep("players are listed", () => screen.ChildrenOfType<SticksLeaderboardRow>().Count() == 2);
        }

        [Test]
        public void TestSelectingAPlayerShowsTheirTopPlaysAndReplays()
        {
            ScreenStack stack = null!;
            SticksProfileScreen screen = null!;
            AddStep("open leaderboard", () =>
            {
                var http = new HttpClient(new LeaderboardService());
                Child = new ProfileContainer(LocalStorage)
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = stack = new ScreenStack(screen = new SticksProfileScreen(s => new SticksLeaderboardClient(s, "https://sticks.example", http)))
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                };
            });
            AddUntilStep("profile loads", () => screen.IsLoaded);
            AddStep("switch to leaderboard", () => screen.ChildrenOfType<OsuTabControl<SticksProfileTab>>().Single().Current.Value = SticksProfileTab.Leaderboard);
            AddUntilStep("players are listed", () => screen.ChildrenOfType<SticksLeaderboardRow>().Count() == 2);
            AddStep("select the first player", () => screen.ChildrenOfType<SticksLeaderboardRow>().First().TriggerClick());
            AddUntilStep("their top plays load", () => stack.CurrentScreen is SticksPlayerScreen player && player.ChildrenOfType<SticksProfilePlayRow>().Count() == 2);
            AddAssert("titles come from the service", () => player().ChildrenOfType<TruncatingSpriteText>().Any(t => t.Text.ToString() == "Uploaded Song"));
            AddAssert("only the uploaded replay can be watched", () => player().ChildrenOfType<SticksProfilePlayRow.WatchReplayButton>().Count() == 1);
            AddAssert("each beatmap title opens the beatmap", () => player().ChildrenOfType<OsuHoverContainer>().Count(h => h.TooltipText == "View beatmap") == 2);
            AddAssert("misses count heads and clicks once", () => player().ChildrenOfType<OsuSpriteText>().Count(t => t.Text.ToString() == "3 misses") == 2);

            SticksPlayerScreen player() => (SticksPlayerScreen)stack.CurrentScreen;
        }

        [Test]
        public void TestOpeningProfileRetriesUnsentPlays()
        {
            SticksProfileScreen screen = null!;
            var service = new LeaderboardService();
            AddStep("connect and open profile", () =>
            {
                connect();
                var http = new HttpClient(service);
                Child = new ProfileContainer(LocalStorage)
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new ScreenStack(screen = new SticksProfileScreen(s => new SticksLeaderboardClient(s, "https://sticks.example", http)))
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                };
            });
            AddUntilStep("sync is retried", () => service.Paths.Contains("/v1/rulesets/sticks/policy"));
            AddUntilStep("sharing settles as on", () => screen.ChildrenOfType<SticksScoreSharingPanel>().SingleOrDefault()?.State == SticksSharingState.On);
            AddAssert("there is no manual sync button", () => !screen.ChildrenOfType<RoundedButton>().Any(b => b.Text.ToString().Contains("Sync")));
            AddUntilStep("rank is fetched after syncing", () => service.Paths.Contains("/v1/rulesets/sticks/leaderboard"));
        }

        [Test]
        public void TestProfileIncludesPlaysSharedFromOtherComputers()
        {
            SticksProfileScreen screen = null!;
            AddStep("connect with one play here and two shared from elsewhere", () =>
            {
                connect();
                new SticksProfileStore(LocalStorage).Append(SticksProfileTest.Entry(1, 100) with { UserId = API.LocalUser.Value.Id });
                var http = new HttpClient(new LeaderboardService());
                Child = new ProfileContainer(LocalStorage)
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new ScreenStack(screen = new SticksProfileScreen(s => new SticksLeaderboardClient(s, "https://sticks.example", http)))
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                };
            });
            AddUntilStep("plays from elsewhere are listed", () => screen.ChildrenOfType<SticksProfilePlayRow>().Count() == 2
                                                                  && titles(screen).Contains("Local Only Song"));
            AddAssert("a better play from elsewhere replaces this computer's", () => titles(screen).Contains("Uploaded Song"));
            AddAssert("total pp counts both", () => screen.ChildrenOfType<SticksProfileStatistics>().Single().ChildrenOfType<OsuSpriteText>()
                                                          .Any(t => t.Text.ToString() == (300 + 150 * 0.95).ToString("#,##0")));
            AddAssert("its uploaded replay can be watched", () => screen.ChildrenOfType<SticksProfilePlayRow.WatchReplayButton>().Count() == 1);
        }

        [Test]
        public void TestProfileDoesNotContactTheLeaderboardWithoutSharing()
        {
            SticksProfileScreen screen = null!;
            var service = new LeaderboardService();
            AddStep("open profile with one play and sharing off", () =>
            {
                new SticksProfileStore(LocalStorage).Append(SticksProfileTest.Entry(1, 100) with { UserId = API.LocalUser.Value.Id });
                var http = new HttpClient(service);
                Child = new ProfileContainer(LocalStorage)
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new ScreenStack(screen = new SticksProfileScreen(s => new SticksLeaderboardClient(s, "https://sticks.example", http)))
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                };
            });
            AddUntilStep("this computer's play is listed", () => screen.ChildrenOfType<SticksProfilePlayRow>().Count() == 1);
            AddAssert("the leaderboard was not contacted", () => service.Paths.IsEmpty);
        }

        private static string[] titles(SticksProfileScreen screen) => screen.ChildrenOfType<TruncatingSpriteText>().Select(t => t.Text.ToString()).ToArray();

        private void connect()
        {
            using Stream stream = LocalStorage.GetStorageForDirectory("ruleset-data/sticks/profile").GetStream("online.json", FileAccess.Write, FileMode.Create);
            stream.Write(Encoding.UTF8.GetBytes($"{{\"origin\":\"https://sticks.example\",\"userId\":{API.LocalUser.Value.Id},"
                                                + $"\"token\":\"{new string('a', 64)}\",\"expires\":9999999999}}"));
        }

        private class LeaderboardService : HttpMessageHandler
        {
            public readonly ConcurrentQueue<string> Paths = new ConcurrentQueue<string>();

            // Two misses on note heads and one click miss; angle, tracking and tail misses don't add to the count.
            private static string topPlays(int userId) => JsonSerializer.Serialize(new
            {
                ruleset = "sticks", player = new { userId, username = "first", pp = 1234.5 }, total = 2,
                entries = new[]
                {
                    play(userId, 1, "1", "Uploaded Song", true),
                    play(userId, 2, "2", "Local Only Song", false),
                },
            });

            private static object play(int userId, int rank, string id, string title, bool uploaded) => new
            {
                rank, scoreId = id.PadLeft(32, '0'), userId, beatmapId = rank, beatmapSetId = 10,
                sourceMd5 = "0123456789abcdef0123456789abcdef", playedAt = 1792417969000, generation = 1,
                pp = 300.0 / rank, accuracy = 0.97, maxCombo = 400, totalScore = 900000, activationThreshold = 0.97,
                title, artist = "Artist", difficulty = "Hard",
                judgements = new[] { 200, 5, 0, 2, 190, 15, 0, 2, 10, 0, 0, 1, 20, 3, 30, 1 },
                replay = new { eligible = true, available = uploaded, downloadPath = uploaded ? $"/v1/rulesets/sticks/users/{userId}/scores/{rank}/{id.PadLeft(32, '0')}/replay" : null },
            };

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string path = request.RequestUri!.AbsolutePath;
                Paths.Enqueue(path);
                string body = path.EndsWith("/policy", StringComparison.Ordinal)
                    ? "{\"ruleset\":\"sticks\",\"generation\":1,\"whitelistedSets\":[]}"
                    : path.StartsWith("/v1/rulesets/sticks/users/", StringComparison.Ordinal) && path.EndsWith("/top", StringComparison.Ordinal)
                        ? topPlays(int.Parse(path.Split('/')[^2]))
                        : "{\"ruleset\":\"sticks\",\"entries\":[{\"rank\":1,\"userId\":2,\"username\":\"first\",\"pp\":1234.5},"
                          + "{\"rank\":2,\"userId\":3,\"username\":\"second\",\"pp\":987.6}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }
        }

        private partial class ProfileContainer : Container
        {
            private readonly Storage storage;
            private readonly ScoreManager? scores;

            public ProfileContainer(Storage storage, ScoreManager? scores = null)
            {
                this.storage = storage;
                this.scores = scores;
            }

            protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
            {
                var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
                dependencies.CacheAs(storage);
                if (scores != null) dependencies.CacheAs(scores);
                return dependencies;
            }
        }
    }
}
