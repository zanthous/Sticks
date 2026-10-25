#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Profiles;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.UI.Profile;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksProfileTest
    {
        private TemporaryNativeStorage storage = null!;
        private static readonly HashSet<int> emptyWhitelist = new HashSet<int>();

        [SetUp]
        public void SetUp() => storage = new TemporaryNativeStorage("sticks-profile-test");

        [TearDown]
        public void TearDown() => storage.Dispose();

        internal static SticksProfileScore Entry(int map = 1, double pp = 100) => new SticksProfileScore
        {
            ScoreId = Guid.NewGuid(), UserId = 42, BeatmapId = map, BeatmapSetId = 10,
            SourceMd5 = "0123456789abcdef0123456789abcdef", PlayedAt = 1792417969000,
            DifficultyVersion = 202609163, Rate = 1, PP = pp, Accuracy = 0.98, MaxCombo = 500,
            TotalScore = 980000, ActivationThreshold = 0.97f, Ranked = true, MatchesPublishedMap = true,
            Judgements = Enumerable.Range(0, 16).ToArray(),
        };

        [Test]
        public void TestCompactRecordRoundTripAndNoDuplicate()
        {
            var score = Entry();
            var store = new SticksProfileStore(storage);
            Assert.That(SticksProfileStore.Encode(score), Has.Length.EqualTo(166));
            Assert.That(store.Append(score), Is.True);
            Assert.That(store.Append(score), Is.False);
            var restored = store.Read().Single();
            Assert.Multiple(() =>
            {
                Assert.That(restored.ScoreId, Is.EqualTo(score.ScoreId));
                Assert.That(restored.SourceMd5, Is.EqualTo(score.SourceMd5));
                Assert.That(restored.PP, Is.EqualTo(score.PP));
                Assert.That(restored.Judgements, Is.EqualTo(score.Judgements));
                Assert.That(restored.MatchesPublishedMap, Is.True);
            });
            using Stream file = storage.GetStream("ruleset-data/sticks/profile/scores.bin");
            Assert.That(file.Length, Is.EqualTo(12 + 182), "One binary record, not a separate filesystem allocation per score.");
        }

        [Test]
        public void TestInterruptedAppendDoesNotLoseEarlierScores()
        {
            var store = new SticksProfileStore(storage);
            store.Append(Entry());
            using (Stream file = storage.GetStream("ruleset-data/sticks/profile/scores.bin", FileAccess.Write, FileMode.Append))
                file.Write(new byte[43]);
            Assert.That(store.Read(), Has.Length.EqualTo(1));
            Assert.That(store.Append(Entry(2)), Is.True);
            Assert.That(store.Read(), Has.Length.EqualTo(2));
        }

        [Test]
        public void TestDamagedRecordIsNotCounted()
        {
            var store = new SticksProfileStore(storage);
            store.Append(Entry());
            store.Append(Entry(2));
            using (Stream file = storage.GetStream("ruleset-data/sticks/profile/scores.bin", FileAccess.Write, FileMode.Open))
            {
                file.Position = 12 + 70;
                file.WriteByte(255);
            }
            Assert.That(store.Read().Select(s => s.BeatmapId), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void TestDamagedHeaderIsNeverOverwritten()
        {
            const string path = "ruleset-data/sticks/profile/scores.bin";
            using (Stream file = storage.GetStream(path, FileAccess.Write, FileMode.Create)) file.Write(new byte[20]);
            Assert.Throws<InvalidDataException>(() => new SticksProfileStore(storage).Append(Entry()));
            using Stream preserved = storage.GetStream(path);
            Assert.That(preserved.Length, Is.EqualTo(20));
        }

        [Test]
        public void TestWhitelistIsBySetAndStillRequiresPublishedMap()
        {
            var whitelist = new HashSet<int> { 10 };
            Assert.Multiple(() =>
            {
                Assert.That(SticksProfilePolicy.IsEligible(Entry(), emptyWhitelist), Is.True);
                Assert.That(SticksProfilePolicy.IsEligible(Entry() with { Ranked = false }, emptyWhitelist), Is.False);
                Assert.That(SticksProfilePolicy.IsEligible(Entry(1) with { Ranked = false }, whitelist), Is.True);
                Assert.That(SticksProfilePolicy.IsEligible(Entry(2) with { Ranked = false }, whitelist), Is.True);
                Assert.That(SticksProfilePolicy.IsEligible(Entry() with { Ranked = false, BeatmapSetId = 11 }, whitelist), Is.False);
                Assert.That(SticksProfilePolicy.IsEligible(Entry() with { MatchesPublishedMap = false }, whitelist), Is.False);
            });
        }

        [Test]
        public void TestOnlyBestNoModPerDifficultyAndAccountCounts()
        {
            var scores = new[]
            {
                Entry(1, 100), Entry(1, 120), Entry(2, 80),
                Entry(3, 999) with { UserId = 99 }, Entry(4, 999) with { Mods = 128 },
                Entry(5, 999) with { Generation = 2 }, Entry(6, 999) with { Ranked = false },
            };
            var top = SticksProfilePolicy.TopPlays(scores, 42, emptyWhitelist);
            Assert.That(top.Select(s => s.PP), Is.EqualTo(new[] { 120d, 80d }));
            Assert.That(SticksProfilePolicy.TotalPP(top), Is.EqualTo(120 + 80 * 0.95).Within(1e-8));
        }

        [Test]
        public void TestWeightedTotalUsesOnlyTop100()
        {
            var plays = Enumerable.Range(1, 150).Select(i => Entry(i, 100)).ToArray();
            Assert.That(SticksProfilePolicy.TotalPP(plays), Is.EqualTo(100 * (1 - Math.Pow(0.95, 100)) / 0.05).Within(1e-8));
        }

        [Test]
        public void TestPublishedRevisionSupersedesOldChecksumEvenWithLowerPP()
        {
            var original = Entry();
            var updated = Entry(1, 80) with { SourceMd5 = new string('f', 32), PlayedAt = original.PlayedAt + 1000 };
            var edited = Entry(1, 999) with { SourceMd5 = new string('b', 32), PlayedAt = updated.PlayedAt + 1000, MatchesPublishedMap = false };
            var top = SticksProfilePolicy.TopPlays(new[] { original, updated, edited }, 42, emptyWhitelist);
            Assert.That(top.Single().ScoreId, Is.EqualTo(updated.ScoreId));
        }

        [Test]
        public void TestProfileMergesPlaysFromOtherComputersBestPerMap()
        {
            var here = Entry(1, 100);
            var elsewhere = new[] { Entry(1, 150), Entry(2, 90), here with { } };
            var merged = SticksProfilePolicy.Merge(new[] { here, Entry(3, 50) }, elsewhere);
            Assert.That(merged.Select(s => (s.BeatmapId, s.PP)), Is.EqualTo(new[] { (1, 150d), (2, 90d), (3, 50d) }));
            Assert.That(SticksProfilePolicy.TotalPP(merged), Is.EqualTo(150 + 90 * 0.95 + 50 * 0.95 * 0.95).Within(1e-8));
        }

        [Test]
        public void TestProfileMergeKeepsThisComputersCopyAndTheNewestRevision()
        {
            var here = Entry(1, 100);
            Assert.That(SticksProfilePolicy.Merge(new[] { here }, new[] { here with { } }).Single(), Is.SameAs(here));

            var updated = Entry(1, 80) with { SourceMd5 = new string('f', 32), PlayedAt = here.PlayedAt + 1000 };
            Assert.That(SticksProfilePolicy.Merge(new[] { here }, new[] { updated }).Single().ScoreId, Is.EqualTo(updated.ScoreId));
            Assert.That(SticksProfilePolicy.Merge(new[] { updated }, new[] { here }).Single().ScoreId, Is.EqualTo(updated.ScoreId));
        }

        [Test]
        public void TestAutomationAndConversionOverridesCannotCount()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksProfilePolicy.TryEncodeMods(new[] { new SticksModAutoplay() }, out _), Is.False);
                Assert.That(SticksProfilePolicy.TryEncodeMods(new[] { new SticksModRelax() }, out _), Is.False);
                Assert.That(SticksProfilePolicy.TryEncodeMods(new[] { new SticksModDifficultyAdjust() }, out _), Is.False);
                Assert.That(SticksProfilePolicy.TryEncodeMods(new[] { new SticksModAmbidextrous() }, out _), Is.False);
                Assert.That(SticksProfilePolicy.TryEncodeMods(new[] { new SticksModStrum() }, out _), Is.False);
                Assert.That(SticksProfilePolicy.TryEncodeMods(new osu.Game.Rulesets.Mods.Mod[] { new SticksModNoFail() }, out _), Is.False);
                Assert.That(SticksProfilePolicy.TryEncodeMods(Array.Empty<osu.Game.Rulesets.Mods.Mod>(), out _), Is.True);
            });
        }

        [Test]
        public void TestFailedAndNativeScoresCannotBeCaptured()
        {
            Assert.That(SticksProfileScore.Capture(new ScoreInfo { Passed = false }, 100, 1, 0.97f), Is.Null);
            Assert.That(SticksProfileScore.Capture(new ScoreInfo
            {
                BeatmapInfo = new BeatmapInfo(new SticksRuleset().RulesetInfo),
            }, 100, 1, 0.97f), Is.Null);
        }

        [Test]
        public void TestInvalidValuesCannotBePersisted()
        {
            var store = new SticksProfileStore(storage);
            foreach (var score in new[] { Entry() with { PP = double.NaN }, Entry() with { Accuracy = 1.1 },
                         Entry() with { Mods = uint.MaxValue }, Entry() with { SourceMd5 = "bad" } })
                Assert.That(store.Append(score), Is.False);
            Assert.That(store.Read(), Is.Empty);
        }

        [Test]
        public void TestLiveGameplayScoreWithoutDateIsCaptured()
        {
            // Gameplay hands the tracker its live score, which lazer never dates; only the imported copy is.
            var live = new ScoreInfo
            {
                User = new APIUser { Id = 42, Username = "Test" },
                BeatmapInfo = new BeatmapInfo
                {
                    OnlineID = 1, BeatmapSet = new BeatmapSetInfo { OnlineID = 10 },
                    MD5Hash = Entry().SourceMd5, OnlineMD5Hash = Entry().SourceMd5, Status = BeatmapOnlineStatus.Ranked,
                },
                Accuracy = 0.98, MaxCombo = 500, TotalScore = 980000,
            };
            Assert.That(SticksProfileScore.Capture(live.DeepClone(), 100, 1, 0.97f), Is.Null, "An undated score is invalid by itself.");

            var captured = SticksProfileScore.Capture(SticksProfileTracker.Snapshot(live), 100, 1, 0.97f);
            Assert.That(captured, Is.Not.Null);
            Assert.That(DateTimeOffset.FromUnixTimeMilliseconds(captured!.PlayedAt), Is.EqualTo(DateTimeOffset.Now).Within(TimeSpan.FromMinutes(1)));
            Assert.That(live.Date, Is.EqualTo(default(DateTimeOffset)), "The live gameplay score must not be modified.");
        }

        [Test]
        public void TestProfileCoverWorksOnThisClient()
        {
            // Stable lazer and Tachyon name the cover type differently; a future rename would silently drop the cover.
            var cover = SticksUserCover.Create(new APIUser { Id = 2, Username = "peppy" });
            Assert.That(cover.GetType().Name, Is.EqualTo("CoverBackground").Or.EqualTo("UserCoverBackground"));
            Assert.That(cover.RelativeSizeAxes, Is.EqualTo(osu.Framework.Graphics.Axes.Both));
        }

        [Test]
        public void TestPlayRowColumnsCountEachNoteOnce()
        {
            // Timing 200/5/0/2, angle 190/15/0/2, clicks 10/0/0/1, tracking 20 hit 3 missed, ends 30 hit 1 missed.
            var score = Entry() with { Judgements = new[] { 200, 5, 0, 2, 190, 15, 0, 2, 10, 0, 0, 1, 20, 3, 30, 1 } };
            Assert.That(SticksProfilePlayRow.Statistics(score), Is.EqualTo(new[] { 210, 5, 0, 3, 3 }),
                "Great/Ok/Meh/Miss count notes and clicks by timing, so a missed note isn't counted again for its angle; breaks are tracking misses.");
            Assert.That(score.MissCount, Is.EqualTo(3));
        }

        [TestCase(BeatmapOnlineStatus.Ranked, true)]
        [TestCase(BeatmapOnlineStatus.Approved, true)]
        [TestCase(BeatmapOnlineStatus.Qualified, false)]
        [TestCase(BeatmapOnlineStatus.Loved, false)]
        public void TestOnlyStatusesThatAwardPPCountAsRanked(BeatmapOnlineStatus status, bool ranked)
        {
            var info = new ScoreInfo
            {
                User = new APIUser { Id = 42 },
                BeatmapInfo = new BeatmapInfo
                {
                    OnlineID = 1, BeatmapSet = new BeatmapSetInfo { OnlineID = 10 },
                    MD5Hash = Entry().SourceMd5, OnlineMD5Hash = Entry().SourceMd5, Status = status,
                },
                Accuracy = 0.98, Date = DateTimeOffset.UtcNow,
            };
            Assert.That(SticksProfileScore.Capture(info, 100, 1, 0.97f)!.Ranked, Is.EqualTo(ranked));
        }

        [Test]
        public void TestCaptureRetainsFreshJudgementsAndIdentity()
        {
            var info = new ScoreInfo
            {
                User = new APIUser { Id = 42, Username = "Test" },
                BeatmapInfo = new BeatmapInfo
                {
                    OnlineID = 1, BeatmapSet = new BeatmapSetInfo { OnlineID = 10 },
                    MD5Hash = Entry().SourceMd5, OnlineMD5Hash = Entry().SourceMd5, Status = BeatmapOnlineStatus.Ranked,
                },
                Accuracy = 0.98, MaxCombo = 500, TotalScore = 980000, Date = DateTimeOffset.UtcNow,
                HitEvents = new List<HitEvent>
                {
                    new HitEvent(0, 1, HitResult.Great, new SticksFlick(), null!, null),
                    new HitEvent(0, 1, HitResult.Ok, new SticksAngleComponent(), null!, null),
                    new HitEvent(0, 1, HitResult.Meh, new SticksClick(), null!, null),
                    new HitEvent(0, 1, HitResult.LargeTickMiss, new SticksHoldTick(), null!, null),
                    new HitEvent(0, 1, HitResult.SliderTailHit, new SticksHoldTail(), null!, null),
                },
            };
            var captured = SticksProfileScore.Capture(info.DeepClone(), 123.45, 202609163, 0.97f)!;
            Assert.That(captured, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(captured.UserId, Is.EqualTo(42));
                Assert.That(captured.ScoreId, Is.EqualTo(info.ID));
                Assert.That(captured.PP, Is.EqualTo(123.45));
                Assert.That(captured.Judgements[0], Is.EqualTo(1));
                Assert.That(captured.Judgements[5], Is.EqualTo(1));
                Assert.That(captured.Judgements[10], Is.EqualTo(1));
                Assert.That(captured.Judgements[13], Is.EqualTo(1));
                Assert.That(captured.Judgements[14], Is.EqualTo(1));
            });
            info.BeatmapInfo.OnlineMD5Hash = new string('f', 32);
            Assert.That(SticksProfileScore.Capture(info, 100, 1, 0.97f)!.MatchesPublishedMap, Is.False);
            info.User = new APIUser { Id = -1 };
            Assert.That(SticksProfileScore.Capture(info, 100, 1, 0.97f)!.UserId, Is.Zero);
        }

        [Test]
        public void TestAllOfferedModsAreExcluded()
        {
            foreach (Mod mod in new SticksRuleset().AllMods)
                Assert.That(SticksProfilePolicy.TryEncodeMods(new[] { mod }, out _), Is.False, mod.Name);
        }

        [Test]
        public void TestUnsavedQuitNeverRecordsProfile()
        {
            int count = 0;
            using (var persistence = new SticksReplayPersistence(new SticksReplayStore(storage), _ => false,
                       (_, _) => new Subscription(), (_, _) => count++))
                persistence.Track(new Score(), saveReplay: false);
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void TestSavedScoreNotificationDoesNotForceReplaySaving()
        {
            bool saved = false;
            int notifications = 0;
            Action? notify = null;
            var score = new Score();
            var replayStore = new SticksReplayStore(storage);
            using var persistence = new SticksReplayPersistence(replayStore, _ => saved,
                (_, callback) => { notify = callback; return new Subscription(); }, (_, _) => notifications++);
            persistence.Track(score, saveReplay: false);
            notify!();
            Assert.That(notifications, Is.Zero);
            saved = true;
            notify();
            notify();
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(replayStore.TryRestore(new Score { ScoreInfo = score.ScoreInfo.DeepClone() }), Is.False);
        }

        private sealed class Subscription : IDisposable { public void Dispose() { } }
    }
}
