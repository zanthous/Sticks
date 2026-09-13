using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksCounterpointMotionTest
    {
        [TestCase(200, 200, false)]
        [TestCase(200, 500, false)]
        [TestCase(500, 200, false)]
        [TestCase(249.99, 500, false)]
        [TestCase(250, 250, true)]
        [TestCase(500, 500, true)]
        public void TestInitialAndFinalReversalSpans(double first, double last, bool expected)
        {
            SticksSlider slider = path(new[] { 20f, -20f }, new[] { first, last });
            Assert.That(SticksCounterpointMotion.HasReadableReversals(slider), Is.EqualTo(expected));
        }

        [Test]
        public void TestShortInteriorReversalSpanIsRejected()
        {
            SticksSlider slider = path(new[] { 30f, -10f, 30f }, new[] { 500d, 100d, 500d });
            Assert.That(SticksCounterpointMotion.HasReadableReversals(slider), Is.False);
        }

        [Test]
        public void TestSpeedChangesAccumulateUntilARealReversal()
        {
            SticksSlider slider = path(new[] { 4f, 6f, 20f, -6f, -24f }, new[] { 50d, 100d, 350d, 100d, 400d });
            Assert.That(SticksCounterpointMotion.HasReadableReversals(slider), Is.True,
                "The short pieces describe one continuous direction, not extra reversal judgements.");
        }

        [Test]
        public void TestDwellsAccumulateAccordingToTheActualReversalJudgement()
        {
            SticksSlider slider = path(new[] { 10f, 0f, 0f, -10f, 0f }, new[] { 100d, 50d, 100d, 100d, 150d });
            Assert.Multiple(() =>
            {
                Assert.That(slider.SegmentEndsWithReversal(0), Is.False);
                Assert.That(slider.SegmentEndsWithReversal(1), Is.False);
                Assert.That(slider.SegmentEndsWithReversal(2), Is.True);
                Assert.That(SticksCounterpointMotion.HasReadableReversals(slider), Is.True);
            });
        }

        [Test]
        public void TestBorrowingATooShortDurationCannotIntroduceFastReversals()
        {
            SticksSlider source = path(new[] { 20f, -20f }, new[] { 500d, 500d });
            SticksSlider fitted = path(source.SegmentArcAngles.ToArray(), new[] { 200d, 200d });
            Assert.Multiple(() =>
            {
                Assert.That(SticksCounterpointMotion.HasReadableReversals(source), Is.True);
                Assert.That(SticksCounterpointMotion.HasReadableReversals(fitted), Is.False);
                Assert.That(fitted.SegmentArcAngles, Is.EqualTo(source.SegmentArcAngles), "Validation must preserve the source contour.");
            });
        }

        [Test]
        public void TestSameDirectionPathDoesNotInheritReversalRestrictions()
        {
            SticksSlider slider = path(new[] { 20f, 0f, 20f }, new[] { 50d, 50d, 50d });
            Assert.That(SticksCounterpointMotion.HasReadableReversals(slider), Is.True,
                "The caller's general speed check is separate; this path has no reversal judgement.");
        }

        [TestCase(89f, true)]
        [TestCase(90f, false)]
        [TestCase(91f, false)]
        public void TestExistingReversalAngularVelocityLimit(float arc, bool expected)
        {
            SticksSlider slider = path(new[] { arc, -arc }, new[] { 500d, 500d });
            Assert.That(SticksCounterpointMotion.HasReadableReversals(slider), Is.EqualTo(expected));
        }

        private static SticksSlider path(float[] arcs, double[] durations)
        {
            var slider = new SticksSlider { Duration = durations.Sum() };
            slider.SetTimedSegments(arcs, durations);
            return slider;
        }
    }
}
