// Copyright (c) Zanthous. Licensed under the MIT Licence.

using NUnit.Framework;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksInputTrackerTest
    {
        [Test]
        public void TestFlickRequiresNeutralAndFreshOutwardCrossing()
        {
            var tracker = new SticksInputTracker();
            Assert.That(SticksInputTracker.NEUTRAL_THRESHOLD, Is.EqualTo(0.5f));

            tracker.Update(StickSide.Left, Vector2.Zero, 0);
            tracker.Update(StickSide.Left, new Vector2(0.9f, 0), 100);
            Assert.That(tracker.SequenceFor(StickSide.Left), Is.EqualTo(1));
            Assert.That(tracker.LastFlickFor(StickSide.Left).Angle, Is.EqualTo(0).Within(0.001));

            tracker.Update(StickSide.Left, Vector2.One, 200);
            Assert.That(tracker.SequenceFor(StickSide.Left), Is.EqualTo(1));

            tracker.Update(StickSide.Left, Vector2.Zero, 300);
            tracker.Update(StickSide.Left, new Vector2(0, -0.9f), 400);
            Assert.That(tracker.SequenceFor(StickSide.Left), Is.EqualTo(2));
            Assert.That(tracker.LastFlickFor(StickSide.Left).Angle, Is.EqualTo(270).Within(0.001));
        }

        [Test]
        public void TestSticksAreIndependent()
        {
            var tracker = new SticksInputTracker();
            tracker.Update(StickSide.Right, new Vector2(-1, 0), 100);

            Assert.Multiple(() =>
            {
                Assert.That(tracker.SequenceFor(StickSide.Left), Is.Zero);
                Assert.That(tracker.SequenceFor(StickSide.Right), Is.EqualTo(1));
                Assert.That(tracker.LastFlickFor(StickSide.Right).Angle, Is.EqualTo(180).Within(0.001));
            });
        }

        [Test]
        public void TestEachPhysicalFlickCanOnlyBeConsumedOnce()
        {
            var tracker = new SticksInputTracker();
            tracker.Update(StickSide.Left, Vector2.Zero, 0);
            tracker.Update(StickSide.Left, new Vector2(0.9f, 0), 100);
            long firstSequence = tracker.SequenceFor(StickSide.Left);

            Assert.Multiple(() =>
            {
                Assert.That(tracker.TryConsumeFlick(StickSide.Left, firstSequence), Is.True);
                Assert.That(tracker.TryConsumeFlick(StickSide.Left, firstSequence), Is.False);
                Assert.That(tracker.TryConsumeFlick(StickSide.Right, firstSequence), Is.False);
            });

            tracker.Update(StickSide.Left, Vector2.Zero, 200);
            tracker.Update(StickSide.Left, new Vector2(0, 0.9f), 300);
            long secondSequence = tracker.SequenceFor(StickSide.Left);

            Assert.That(tracker.TryConsumeFlick(StickSide.Left, secondSequence), Is.True);
        }
    }
}
