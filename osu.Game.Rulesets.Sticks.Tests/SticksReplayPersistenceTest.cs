#nullable enable

using System;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.Sticks.Replays;
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
            original.Replay.Frames.Add(new SticksReplayFrame(900, Vector2.Zero, new Vector2(0.25f, -0.5f)));
            original.Replay.Frames.Add(new SticksReplayFrame(1000, new Vector2(0.8f, 0.6f), new Vector2(-1, 0)));

            var store = new SticksReplayStore(storage);
            Assert.That(store.Save(original), Is.True);
            Assert.That(original.ScoreInfo.Hash, Is.EqualTo($"sticks-replay-{original.ScoreInfo.ID:N}"));

            var restored = new Score { ScoreInfo = original.ScoreInfo.DeepClone() };
            Assert.That(store.TryRestore(restored), Is.True);
            Assert.That(restored.Replay.Frames, Has.Count.EqualTo(2));

            var first = (SticksReplayFrame)restored.Replay.Frames[0];
            var second = (SticksReplayFrame)restored.Replay.Frames[1];

            Assert.Multiple(() =>
            {
                Assert.That(first.Time, Is.EqualTo(900));
                Assert.That(first.LeftStick, Is.EqualTo(Vector2.Zero));
                Assert.That(first.RightStick, Is.EqualTo(new Vector2(0.25f, -0.5f)));
                Assert.That(second.Time, Is.EqualTo(1000));
                Assert.That(second.LeftStick, Is.EqualTo(new Vector2(0.8f, 0.6f)));
                Assert.That(second.RightStick, Is.EqualTo(new Vector2(-1, 0)));
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
                persistence.Track(score);
                saved = true;
                notifySaved?.Invoke();
            }

            var restored = new Score { ScoreInfo = score.ScoreInfo.DeepClone() };
            Assert.That(store.TryRestore(restored), Is.True);
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
                persistence.Track(score);
                saved = true;
            }

            var restored = new Score { ScoreInfo = score.ScoreInfo.DeepClone() };
            Assert.That(store.TryRestore(restored), Is.True);
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
