// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

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
        public void TestEightyPercentTravelMappingFeedsGestureDetection()
        {
            var tracker = new SticksInputTracker();
            var physicalNeutral = new Vector2(0.5f, 0);
            var physicalEdge = new Vector2(0.8f, 0);
            Vector2 mappedNeutral = SticksPlayfield.MapStickDistance(physicalNeutral, 0.8f);
            Vector2 mappedEdge = SticksPlayfield.MapStickDistance(physicalEdge, 0.8f);

            tracker.Update(StickSide.Left, physicalNeutral, mappedNeutral, 0);
            tracker.Update(StickSide.Left, physicalEdge, mappedEdge, 100);

            Assert.Multiple(() =>
            {
                Assert.That(physicalNeutral.Length, Is.EqualTo(SticksInputTracker.NEUTRAL_THRESHOLD).Within(0.0001));
                Assert.That(mappedNeutral.Length, Is.EqualTo(0.625f).Within(0.0001));
                Assert.That(mappedEdge.Length, Is.EqualTo(1).Within(0.0001));
                Assert.That(tracker.SequenceFor(StickSide.Left), Is.EqualTo(1));
            });
        }

        [Test]
        public void TestEightyPercentTravelDoesNotShrinkPhysicalNeutralZone()
        {
            var tracker = new SticksInputTracker();
            var physicalEdge = new Vector2(0.8f, 0);
            var physicalReturn = new Vector2(0.45f, 0);

            tracker.Update(StickSide.Left, Vector2.Zero, Vector2.Zero, 0);
            tracker.Update(StickSide.Left, physicalEdge, SticksPlayfield.MapStickDistance(physicalEdge, 0.8f), 100);
            tracker.Update(StickSide.Left, physicalReturn, SticksPlayfield.MapStickDistance(physicalReturn, 0.8f), 200);
            tracker.Update(StickSide.Left, physicalEdge, SticksPlayfield.MapStickDistance(physicalEdge, 0.8f), 300);

            Assert.That(tracker.SequenceFor(StickSide.Left), Is.EqualTo(2),
                "Returning inside the requested 50% physical neutral zone must recharge the next start gesture.");
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

        [Test]
        public void TestHeldOutStickDoesNotAuthoriseNewDurationObject()
        {
            var tracker = new SticksInputTracker();
            var eligibility = new SticksTrackingEligibility();

            // This flick belongs to an earlier note. The new slider begins while the stick is
            // still held out, so its baseline includes that already-used gesture.
            tracker.Update(StickSide.Left, Vector2.Zero, 0);
            tracker.Update(StickSide.Left, new Vector2(1, 0), 100);
            eligibility.Reset(tracker.SequenceFor(StickSide.Left));

            tracker.Update(StickSide.Left, new Vector2(1, 0), 200);
            bool sawNewGesture = eligibility.Observe(
                tracker.SequenceFor(StickSide.Left),
                tracker.LastFlickFor(StickSide.Left),
                150,
                1000,
                0,
                20,
                out bool canAuthorise);

            Assert.Multiple(() =>
            {
                Assert.That(sawNewGesture, Is.False);
                Assert.That(canAuthorise, Is.False);
                Assert.That(eligibility.IsAuthorised, Is.False);
            });

            // Returning to neutral and flicking again is a fresh slider-entry gesture.
            tracker.Update(StickSide.Left, Vector2.Zero, 300);
            tracker.Update(StickSide.Left, new Vector2(1, 0), 400);
            sawNewGesture = eligibility.Observe(
                tracker.SequenceFor(StickSide.Left),
                tracker.LastFlickFor(StickSide.Left),
                150,
                1000,
                0,
                20,
                out canAuthorise);

            if (canAuthorise && tracker.TryConsumeFlick(StickSide.Left, tracker.SequenceFor(StickSide.Left)))
                eligibility.Authorise();

            Assert.Multiple(() =>
            {
                Assert.That(sawNewGesture, Is.True);
                Assert.That(canAuthorise, Is.True);
                Assert.That(eligibility.IsAuthorised, Is.True);
            });
        }

        [Test]
        public void TestWrongAngleFlickDoesNotAuthoriseByRotatingWhileHeld()
        {
            var tracker = new SticksInputTracker();
            var eligibility = new SticksTrackingEligibility();
            eligibility.Reset(0);

            tracker.Update(StickSide.Left, Vector2.Zero, 0);
            tracker.Update(StickSide.Left, new Vector2(0, 1), 100);
            eligibility.Observe(
                tracker.SequenceFor(StickSide.Left),
                tracker.LastFlickFor(StickSide.Left),
                0,
                1000,
                0,
                20,
                out bool canAuthorise);

            if (canAuthorise && tracker.TryConsumeFlick(StickSide.Left, tracker.SequenceFor(StickSide.Left)))
                eligibility.Authorise();

            tracker.Update(StickSide.Left, new Vector2(1, 0), 200);
            eligibility.Observe(
                tracker.SequenceFor(StickSide.Left),
                tracker.LastFlickFor(StickSide.Left),
                0,
                1000,
                0,
                20,
                out canAuthorise);

            if (canAuthorise && tracker.TryConsumeFlick(StickSide.Left, tracker.SequenceFor(StickSide.Left)))
                eligibility.Authorise();

            Assert.That(eligibility.IsAuthorised, Is.False);
        }

        [Test]
        public void TestFlickConsumedByPreviousNoteCannotAuthoriseSlider()
        {
            var tracker = new SticksInputTracker();
            var eligibility = new SticksTrackingEligibility();
            eligibility.Reset(0);

            tracker.Update(StickSide.Left, Vector2.Zero, 0);
            tracker.Update(StickSide.Left, new Vector2(1, 0), 100);
            long sequence = tracker.SequenceFor(StickSide.Left);

            Assert.That(tracker.TryConsumeFlick(StickSide.Left, sequence), Is.True, "The preceding note claims this gesture first.");

            bool sawNewGesture = eligibility.Observe(
                sequence,
                tracker.LastFlickFor(StickSide.Left),
                0,
                1000,
                0,
                20,
                out bool canAuthorise);

            if (canAuthorise && tracker.TryConsumeFlick(StickSide.Left, sequence))
                eligibility.Authorise();

            Assert.Multiple(() =>
            {
                Assert.That(sawNewGesture, Is.True);
                Assert.That(canAuthorise, Is.True);
                Assert.That(eligibility.IsAuthorised, Is.False);
            });
        }
    }
}
