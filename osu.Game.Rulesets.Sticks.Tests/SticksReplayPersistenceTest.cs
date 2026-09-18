#nullable enable

using System;
using System.IO;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksReplayPersistenceTest
    {
        private TemporaryNativeStorage storage = null!;

        [SetUp]
        public void SetUp() => storage = new TemporaryNativeStorage("sticks-replay-persistence-test");

        [TearDown]
        public void TearDown() => storage.Dispose();

        [Test]
        public void TestCustomScoreReplayRoundTripsThroughPrivateStorage()
        {
            var original = new Score();
            original.Replay.Frames.Add(new SticksReplayFrame(900, Vector2.Zero, new Vector2(0.25f, -0.5f), leftTrigger: true, rightShoulder: true));
            original.Replay.Frames.Add(new SticksReplayFrame(1000, new Vector2(0.8f, 0.6f), new Vector2(-1, 0), rightTrigger: true, leftShoulder: true));

            var store = new SticksReplayStore(storage);
            Assert.That(store.Save(original, 0.87f), Is.True);
            using (Stream saved = storage.GetStream($"ruleset-data/sticks/replays/{original.ScoreInfo.ID:N}.stkr"))
                Assert.That(saved.Length, Is.EqualTo(12 + 2 * 30), "The threshold is stored once in the header, not in each frame.");
            Assert.That(original.ScoreInfo.Hash, Is.EqualTo($"sticks-replay-{original.ScoreInfo.ID:N}"));

            var restored = new Score { ScoreInfo = original.ScoreInfo.DeepClone() };
            Assert.That(store.TryRestore(restored, out float threshold), Is.True);
            Assert.That(threshold, Is.EqualTo(0.87f));
            Assert.That(restored.Replay.Frames, Has.Count.EqualTo(2));

            var first = (SticksReplayFrame)restored.Replay.Frames[0];
            var second = (SticksReplayFrame)restored.Replay.Frames[1];

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(first.Time, Is.EqualTo(900));
                Assert.That(first.LeftStick, Is.EqualTo(Vector2.Zero));
                Assert.That(first.RightStick, Is.EqualTo(new Vector2(0.25f, -0.5f)));
                Assert.That(first.LeftTrigger, Is.True);
                Assert.That(first.RightTrigger, Is.False);
                Assert.That(first.LeftShoulder, Is.False);
                Assert.That(first.RightShoulder, Is.True);
                Assert.That(second.Time, Is.EqualTo(1000));
                Assert.That(second.LeftStick, Is.EqualTo(new Vector2(0.8f, 0.6f)));
                Assert.That(second.RightStick, Is.EqualTo(new Vector2(-1, 0)));
                Assert.That(second.LeftTrigger, Is.False);
                Assert.That(second.RightTrigger, Is.True);
                Assert.That(second.LeftShoulder, Is.True);
                Assert.That(second.RightShoulder, Is.False);
            });
        }

        [Test]
        public void TestExistingReplayIsNeverReplaced()
        {
            var stored = new Score();
            stored.Replay.Frames.Add(new SticksReplayFrame(1000, Vector2.UnitX, Vector2.Zero));

            var store = new SticksReplayStore(storage);
            Assert.That(store.Save(stored), Is.True);

            var replayed = new Score { ScoreInfo = stored.ScoreInfo.DeepClone() };
            replayed.Replay.Frames.Add(new SticksReplayFrame(500, Vector2.UnitY, Vector2.Zero));

            Assert.That(store.TryRestore(replayed), Is.True);
            Assert.That(replayed.Replay.Frames, Has.Count.EqualTo(1));
            Assert.That(((SticksReplayFrame)replayed.Replay.Frames[0]).LeftStick, Is.EqualTo(Vector2.UnitY));
        }

        [Test]
        public void TestUnsavedRecordingIsDiscarded()
        {
            var score = createScore();
            Action? notifySaved = null;

            var store = new SticksReplayStore(storage);
            using (var persistence = new SticksReplayPersistence(
                       store,
                       _ => false,
                       (_, callback) =>
                       {
                           notifySaved = callback;
                           return new TestDisposable();
                       }))
            {
                persistence.Track(score);
                notifySaved?.Invoke();
            }

            var restored = new Score { ScoreInfo = score.ScoreInfo.DeepClone() };
            Assert.That(store.TryRestore(restored), Is.False);
        }

        [Test]
        public void TestRecordingIsRetainedAfterScoreIsSaved()
        {
            var score = createScore();
            bool saved = false;
            Action? notifySaved = null;

            var store = new SticksReplayStore(storage);
            using (var persistence = new SticksReplayPersistence(
                       store,
                       _ => saved,
                       (_, callback) =>
                       {
                           notifySaved = callback;
                           return new TestDisposable();
                       }))
            {
                persistence.Track(score, 0.87f);
                saved = true;
                notifySaved?.Invoke();
            }

            var restored = new Score { ScoreInfo = score.ScoreInfo.DeepClone() };
            Assert.That(store.TryRestore(restored, out float threshold), Is.True);
            Assert.That(threshold, Is.EqualTo(0.87f));
            Assert.That(restored.Replay.Frames, Has.Count.EqualTo(1));
        }

        [Test]
        public void TestRecordingIsRetainedWhenSaveNotificationIsLate()
        {
            var score = createScore();
            bool saved = false;

            var store = new SticksReplayStore(storage);
            using (var persistence = new SticksReplayPersistence(
                       store,
                       _ => saved,
                       (_, _) => new TestDisposable()))
            {
                persistence.Track(score, 0.87f);
                saved = true;
            }

            var restored = new Score { ScoreInfo = score.ScoreInfo.DeepClone() };
            Assert.That(store.TryRestore(restored, out float threshold), Is.True);
            Assert.That(threshold, Is.EqualTo(0.87f));
            Assert.That(restored.Replay.Frames, Has.Count.EqualTo(1));
        }

        [Test]
        public void TestOldReplayWithoutSavedScoreIsRemoved()
        {
            var kept = createScore();
            var discarded = createScore();
            var store = new SticksReplayStore(storage);

            Assert.That(store.Save(kept), Is.True);
            Assert.That(store.Save(discarded), Is.True);

            store.DeleteOrphans(id => id == kept.ScoreInfo.ID);

            Assert.That(store.TryRestore(new Score { ScoreInfo = kept.ScoreInfo.DeepClone() }), Is.True);
            Assert.That(store.TryRestore(new Score { ScoreInfo = discarded.ScoreInfo.DeepClone() }), Is.False);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void TestOldRecordingUsesDefaultThreshold(int version)
        {
            var score = new Score();
            using (Stream stream = storage.GetStream($"ruleset-data/sticks/replays/{score.ScoreInfo.ID:N}.stkr", FileAccess.Write, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0x53544B52);
                writer.Write(version);
                writer.Write(1000d);
                writer.Write(0.9f);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(1f);
                for (int button = 0; button < (version - 1) * 2; button++)
                    writer.Write(true);
            }

            Assert.That(new SticksReplayStore(storage).TryRestore(score, out float threshold), Is.True);
            Assert.That(threshold, Is.EqualTo(SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD));
            var frame = (SticksReplayFrame)score.Replay.Frames[0];
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(frame.Time, Is.EqualTo(1000));
                Assert.That(frame.LeftStick, Is.EqualTo(new Vector2(0.9f, 0)));
                Assert.That(frame.RightStick, Is.EqualTo(Vector2.UnitY));
                Assert.That(frame.LeftTrigger && frame.RightTrigger, Is.EqualTo(version >= 2));
                Assert.That(frame.LeftShoulder && frame.RightShoulder, Is.EqualTo(version >= 3));
                Assert.That(frame.LeftStickButton && frame.RightStickButton, Is.EqualTo(version >= 4));
            });
        }

        [TestCase(float.NaN)]
        [TestCase(0.5f)]
        [TestCase(1.01f)]
        public void TestInvalidThresholdDoesNotReplaceReplay(float invalidThreshold)
        {
            var score = createScore();
            var store = new SticksReplayStore(storage);
            Assert.That(store.Save(score, 0.87f), Is.True);
            using (Stream stream = storage.GetStream($"ruleset-data/sticks/replays/{score.ScoreInfo.ID:N}.stkr", FileAccess.Write, FileMode.Open))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = 8;
                writer.Write(invalidThreshold);
            }

            var original = score.Replay.Frames[0];
            Assert.That(store.TryRestore(score, out float threshold), Is.False);
            Assert.That(threshold, Is.EqualTo(SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD));
            Assert.That(score.Replay.Frames, Is.EqualTo(new[] { original }));
        }

        private static Score createScore()
        {
            var score = new Score();
            score.Replay.Frames.Add(new SticksReplayFrame(1000, Vector2.UnitX, Vector2.Zero));
            return score;
        }

        private sealed class TestDisposable : System.IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
