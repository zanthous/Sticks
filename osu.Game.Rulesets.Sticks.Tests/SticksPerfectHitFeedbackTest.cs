using NUnit.Framework;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksPerfectHitFeedbackTest
    {
        [Test]
        public void TestSingleHeadsAccentOnlyPerfectGrades()
        {
            var tracker = new SticksPerfectHitTracker();
            SticksHitObject head = createHead(StickSide.Left);
            foreach (HitResult result in new[] { HitResult.Miss, HitResult.Meh, HitResult.Ok, HitResult.Good, HitResult.Great })
                Assert.That(tracker.TryResolve(head, result, null, out _), Is.False);
            Assert.That(tracker.TryResolve(head, HitResult.Perfect, null, out bool both), Is.True);
            Assert.That(both, Is.False);
            Assert.That(tracker.TryResolve(new SticksClick(), HitResult.Perfect, null, out _), Is.False,
                "A directionless click must not invent a contact angle.");
        }

        [TestCase(HitResult.Perfect, HitResult.Perfect, true)]
        [TestCase(HitResult.Great, HitResult.Perfect, false)]
        [TestCase(HitResult.Perfect, HitResult.Miss, false)]
        public void TestPurpleAccentRequiresBothHandsAndSurvivesFirstDrawableExpiring(HitResult first, HitResult second, bool expected)
        {
            var tracker = new SticksPerfectHitTracker();
            SticksHitObject left = createHead(StickSide.Left, 359);
            SticksHitObject right = createHead(StickSide.Right, -1);
            Assert.That(tracker.TryResolve(left, first, right, out _), Is.False);
            // The first drawable need no longer be present to identify its completed grade.
            Assert.That(tracker.TryResolve(right, second, null, out bool both), Is.EqualTo(expected));
            Assert.That(both, Is.True);
        }

        [Test]
        public void TestNearbyHeadsRemainIndependentAndRewindClearsPendingStack()
        {
            var tracker = new SticksPerfectHitTracker();
            SticksHitObject left = createHead(StickSide.Left);
            SticksHitObject right = createHead(StickSide.Right, 3);
            Assert.That(tracker.TryResolve(left, HitResult.Perfect, right, out bool both), Is.True);
            Assert.That(both, Is.False);
            right.Angle = left.Angle;
            Assert.That(tracker.TryResolve(left, HitResult.Perfect, right, out _), Is.False);
            tracker.Clear();
            Assert.That(tracker.TryResolve(right, HitResult.Perfect, left, out _), Is.False,
                "The other hand's result from before a seek must not complete this stack.");
        }

        [Test]
        public void TestVisualPoolIsBoundedAndClearsOnExpiryAndRewind()
        {
            using var layer = new SticksPerfectContactLayer();
            for (int i = 0; i < 200; i++)
                layer.TriggerAt(1000, i * 10, 27.5f, Color4.White);
            Assert.That(layer.ActiveCount, Is.EqualTo(SticksPerfectContactLayer.CAPACITY));
            layer.UpdateAt(1000 + SticksPerfectContactLayer.DURATION);
            Assert.That(layer.ActiveCount, Is.Zero);
            layer.TriggerAt(2000, 90, 27.5f, Color4.White);
            layer.UpdateAt(1999);
            Assert.That(layer.ActiveCount, Is.Zero);
        }

        private static SticksHitObject createHead(StickSide side, float angle = 0) =>
            new SticksAngleComponent { StartTime = 1000, Side = side, Angle = angle };
    }
}
