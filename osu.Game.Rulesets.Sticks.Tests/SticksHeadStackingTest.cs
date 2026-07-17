// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksHeadStackingTest
    {
        [Test]
        public void TestLaterOverlappingHeadReceivesStackRank()
        {
            var first = flick(1000, StickSide.Left, 355);
            var second = flick(1200, StickSide.Left, 5);

            Assert.Multiple(() =>
            {
                Assert.That(SticksHitObjectContainer.StackRankFor(first, new[] { first, second }), Is.Zero);
                Assert.That(SticksHitObjectContainer.StackRankFor(second, new[] { first, second }), Is.EqualTo(1));
            });
        }

        [Test]
        public void TestDifferentStickOrSeparatedAngleDoesNotStack()
        {
            var target = flick(1200, StickSide.Left, 0);

            Assert.That(SticksHitObjectContainer.StackRankFor(target, new[]
            {
                flick(1000, StickSide.Right, 0),
                flick(1100, StickSide.Left, 30),
                target,
            }), Is.Zero);
        }

        [Test]
        public void TestOffsetsMoveAwayFromGuideCircle()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksHitObjectContainer.OffsetFor(StickSide.Left, 1), Is.Positive);
                Assert.That(SticksHitObjectContainer.OffsetFor(StickSide.Right, 1), Is.Negative);
                Assert.That(SticksHitObjectContainer.OffsetFor(StickSide.Left, 2),
                    Is.EqualTo(SticksHitObjectContainer.OffsetFor(StickSide.Left, 1) * 2));
            });
        }

        [Test]
        public void TestFutureSliderPathObstructsHeadBeforeSnakeReachesIt()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
                Side = StickSide.Left,
                Angle = 0,
                ArcAngle = 180,
            };
            var drawable = new DrawableSticksSlider(slider);

            Assert.Multiple(() =>
            {
                Assert.That(drawable.FuturePathObstructsHeadAt(-201, 160, 10), Is.False, "The slider is not alive yet.");
                Assert.That(drawable.FuturePathObstructsHeadAt(-200, 160, 10), Is.True, "The full future path is reserved before snaking begins.");
                Assert.That(drawable.FuturePathObstructsHeadAt(1250, 90, 10), Is.True, "The remaining active path occupies this angle.");
                Assert.That(drawable.FuturePathObstructsHeadAt(1500, 45, 10), Is.False, "The erased portion must not remain obstructive.");
                Assert.That(drawable.FuturePathObstructsHeadAt(1250, 330, 10), Is.False);
                Assert.That(drawable.FuturePathObstructsHeadAt(2000, 180, 10), Is.False, "A completed slider reserves no path.");
            });
        }

        [Test]
        public void TestVisibleSliderPathSupportsWraparoundAndMultipleTurns()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 2000,
                Side = StickSide.Right,
                Angle = 350,
                ArcAngle = 720,
            };
            var drawable = new DrawableSticksSlider(slider);

            Assert.Multiple(() =>
            {
                Assert.That(drawable.FuturePathObstructsHeadAt(1000, 5, 10), Is.True);
                Assert.That(drawable.FuturePathObstructsHeadAt(1000, 180, 10), Is.True);
            });
        }

        [Test]
        public void TestFutureReversalSegmentIsReservedBeforeItsPreview()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 2000,
                Side = StickSide.Left,
                Angle = 0,
            };
            slider.SetCustomSegments(new[] { 90f, -180f });
            var drawable = new DrawableSticksSlider(slider);

            Assert.That(drawable.FuturePathObstructsHeadAt(1000, 315, 10), Is.True,
                "The future reversal is reserved even before its snake preview begins.");
        }

        [TestCase(StickSide.Left, 12f)]
        [TestCase(StickSide.Right, -12f)]
        public void TestCompleteSliderUsesSharedVisualRadius(StickSide side, float offset)
        {
            var drawable = new DrawableSticksSlider(new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
                Side = side,
                Angle = 45,
                ArcAngle = 180,
            });

            drawable.ApplyVisualRadialOffsetForTesting(offset);
            float expectedRadius = SticksPlayfield.RadiusFor(side) + offset;

            Assert.Multiple(() =>
            {
                assertArcRadius(drawable, "path", 7, expectedRadius);
                assertArcRadius(drawable, "reversalPathPreview", 7, expectedRadius);
                assertArcRadius(drawable, "reversalPathPreviewOutline", 9, expectedRadius);
                assertArcRadius(drawable, "reversalOutline", 9, expectedRadius);
                assertArcRadius(drawable, "reversalPreviewOutline", 6, expectedRadius);
                assertArcRadius(drawable, "directionPreview", 4, expectedRadius);

                var head = field<SticksSliderHeadMarker>(drawable, "headMarker");
                var tracking = field<SticksArcMarker>(drawable, "trackingMarker");
                Assert.That(head.DisplayedRadialOffset, Is.EqualTo(offset).Within(0.001));
                Assert.That(tracking.DisplayedRadialOffset, Is.EqualTo(offset).Within(0.001));
                Assert.That(drawable.VisualRadialOffset, Is.EqualTo(offset).Within(0.001));
            });
        }

        private static void assertArcRadius(DrawableSticksSlider drawable, string fieldName, float halfThickness, float expectedRadius)
        {
            CircularProgress arc = field<CircularProgress>(drawable, fieldName);
            Assert.That(arc.Size.X / 2 - halfThickness, Is.EqualTo(expectedRadius).Within(0.001), fieldName);
        }

        private static T field<T>(object target, string name) where T : class =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

        private static SticksFlick flick(double startTime, StickSide side, float angle) => new SticksFlick
        {
            StartTime = startTime,
            Side = side,
            Angle = angle,
            PrimaryHitAngle = 20,
        };
    }
}
