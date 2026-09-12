using System;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksDifficultyScalingTest
    {
        [Test]
        public void TestGlobalStarCalibrationReducesInflatedRatings()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksDifficultyScaling.CalibrateStarRating(0), Is.Zero);
                Assert.That(SticksDifficultyScaling.CalibrateStarRating(1), Is.LessThan(0.4));
                Assert.That(SticksDifficultyScaling.CalibrateStarRating(3.4), Is.EqualTo(1.5).Within(0.01));
                Assert.That(SticksDifficultyScaling.CalibrateStarRating(11.1), Is.EqualTo(7.64).Within(0.02));
                Assert.That(SticksDifficultyScaling.CalibrateStarRating(30), Is.EqualTo(30));
            });
        }

        [Test]
        public void TestRawAngularPrecisionDemandAnchors()
        {
            double easy = SticksDifficultyScaling.AngularPrecisionMultiplier(3);
            double reference = SticksDifficultyScaling.AngularPrecisionMultiplier(4);
            double hard = SticksDifficultyScaling.AngularPrecisionMultiplier(5.4f);

            Assert.Multiple(() =>
            {
                Assert.That(easy, Is.EqualTo(11.0 / 14).Within(0.0001));
                Assert.That(reference, Is.EqualTo(1).Within(0.0001));
                Assert.That(hard, Is.EqualTo(11.0 / 8).Within(0.0001));
            });
        }

        [Test]
        public void TestAngularStarAdjustmentScalesWithBaseDifficulty()
        {
            const double developed_map_stars = 7.5;
            const double trivial_map_stars = 0.5;

            double easy = SticksDifficultyScaling.AngularPrecisionStarAdjustment(developed_map_stars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(35, 17.5f));
            double reference = SticksDifficultyScaling.AngularPrecisionStarAdjustment(developed_map_stars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(27.5f, 13.75f));
            double hard = SticksDifficultyScaling.AngularPrecisionStarAdjustment(developed_map_stars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(20, 10));
            double trivialEasy = SticksDifficultyScaling.AngularPrecisionStarAdjustment(trivial_map_stars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(35, 17.5f));
            double trivialHard = SticksDifficultyScaling.AngularPrecisionStarAdjustment(trivial_map_stars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(20, 10));

            Assert.Multiple(() =>
            {
                Assert.That(easy, Is.LessThan(-0.35), "Wide windows must not hit the old fixed reduction cap.");
                Assert.That(reference, Is.Zero.Within(0.0001));
                Assert.That(hard, Is.GreaterThan(0.25), "Tight windows must not hit the old fixed bonus cap.");
                Assert.That(trivialEasy, Is.EqualTo(trivial_map_stars * (Math.Sqrt(11.0 / 14) - 1)).Within(0.0001));
                Assert.That(easy / developed_map_stars, Is.EqualTo(trivialEasy / trivial_map_stars).Within(0.0001));
                Assert.That(hard / developed_map_stars, Is.EqualTo(trivialHard / trivial_map_stars).Within(0.0001));
                Assert.That(trivialHard, Is.GreaterThan(0));
            });
        }

        [TestCase(4, 3.58)]
        [TestCase(5, 3.90)]
        [TestCase(5.4f, 4.20)]
        [TestCase(6, 4.30)]
        [TestCase(7, 4.49)]
        [TestCase(8, 4.71)]
        [TestCase(9, 4.98)]
        [TestCase(10, 5.30)]
        public void TestCircleSizeCurveMatchesAgreedRatings(float circleSize, double expectedStars)
        {
            // The reference map's calibrated rating before angular precision is applied.
            const double base_stars = 3.5815894519875924;
            Assert.That(adjustedStars(base_stars, SticksHitObject.HitAngleForCircleSize(circleSize)),
                Is.EqualTo(expectedStars).Within(0.005));
        }

        [Test]
        public void TestPrecisionAloneKeepsTrivialPatternsEasy()
        {
            Assert.Multiple(() =>
            {
                Assert.That(adjustedStars(0.5, 15), Is.LessThan(1), "Precision alone should not turn a trivial pattern into a difficult map.");
                Assert.That(adjustedStars(0, 4), Is.Zero);
            });
        }

        [TestCase(0.5)]
        [TestCase(7.5)]
        public void TestReferencePrecisionBoundaryIsContinuous(double baseStars)
        {
            const double step = 0.00001;
            double boundary = SticksDifficultyScaling.AngularPrecisionMultiplier(27.5f, 13.75f);
            double at = SticksDifficultyScaling.AngularPrecisionStarAdjustment(baseStars, boundary);
            double below = SticksDifficultyScaling.AngularPrecisionStarAdjustment(baseStars, boundary - step);
            double above = SticksDifficultyScaling.AngularPrecisionStarAdjustment(baseStars, boundary + step);

            Assert.Multiple(() =>
            {
                Assert.That(at, Is.Zero);
                Assert.That(below, Is.LessThan(0));
                Assert.That(above, Is.GreaterThan(0));
                Assert.That(Math.Abs(below), Is.LessThan(baseStars * step));
                Assert.That(above, Is.LessThan(baseStars * step));
            });
        }

        [Test]
        public void TestDifficultyAdjustPrecisionContinuesBeyondCircleSizeTen()
        {
            float[] widths = { 90, 45, 35, 27.5f, 22.5f, 20, 15, 12, 10, 8, 6, 4 };
            double previous = 0;

            foreach (float width in widths)
            {
                double stars = adjustedStars(1, width);
                Assert.That(double.IsFinite(stars), Is.True);
                Assert.That(stars, Is.GreaterThan(previous), $"Narrowing the primary window to {width} degrees must increase stars.");
                previous = stars;
            }
        }

        [Test]
        public void TestAngularPrecisionInterpolationIsMonotonic()
        {
            double previous = SticksDifficultyScaling.AngularPrecisionMultiplier(0);

            for (int step = 1; step <= 200; step++)
            {
                float circleSize = step / 20f;
                double current = SticksDifficultyScaling.AngularPrecisionMultiplier(circleSize);
                Assert.That(current, Is.GreaterThan(previous), $"CS {circleSize} should increase angular precision demand.");
                previous = current;
            }

            Assert.Multiple(() =>
            {
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(0), Is.EqualTo(11.0 / 18).Within(0.0001));
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(10), Is.EqualTo(11.0 / 6).Within(0.0001));
            });
        }

        [Test]
        public void TestTimingPrecisionUsesGameplayGreatWindows()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksDifficultyScaling.TimingPrecisionMultiplier(0), Is.EqualTo(49.5 / 79.5).Within(0.0001));
                Assert.That(SticksDifficultyScaling.TimingPrecisionMultiplier(5), Is.EqualTo(1).Within(0.0001));
                Assert.That(SticksDifficultyScaling.TimingPrecisionMultiplier(10), Is.EqualTo(49.5 / 19.5).Within(0.0001));
            });

            double previous = SticksDifficultyScaling.TimingPrecisionMultiplier(0);

            for (float overallDifficulty = 0.25f; overallDifficulty <= 10; overallDifficulty += 0.25f)
            {
                double current = SticksDifficultyScaling.TimingPrecisionMultiplier(overallDifficulty);
                Assert.That(current, Is.GreaterThanOrEqualTo(previous));
                previous = current;
            }
        }

        [Test]
        public void TestStarRatingPrecisionIgnoresApproachRate()
        {
            var lowApproachRate = new BeatmapDifficulty
            {
                CircleSize = 4.5f,
                OverallDifficulty = 7,
                ApproachRate = 0,
            };
            var highApproachRate = lowApproachRate.Clone();
            highApproachRate.ApproachRate = 10;

            double expected = SticksDifficultyScaling.OverallDifficultyMultiplier(7);

            Assert.Multiple(() =>
            {
                Assert.That(SticksDifficultyScaling.StarRatingPrecisionMultiplier(lowApproachRate), Is.EqualTo(expected).Within(0.0001));
                Assert.That(SticksDifficultyScaling.StarRatingPrecisionMultiplier(highApproachRate), Is.EqualTo(expected).Within(0.0001));
            });
        }

        [Test]
        public void TestActualObjectBandsCanOverrideCircleSize()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(35, 17.5f), Is.EqualTo(11.0 / 14).Within(0.0001));
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(27.5f, 13.75f), Is.EqualTo(1).Within(0.0001));
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(20, 10), Is.EqualTo(11.0 / 8).Within(0.0001));
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(35, 15), Is.EqualTo(Math.Sqrt(363.0 / 560)).Within(0.0001));
            });
        }

        private static double adjustedStars(double baseStars, float primaryAngle) => baseStars
            + SticksDifficultyScaling.AngularPrecisionStarAdjustment(baseStars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(primaryAngle, primaryAngle / 2));
    }
}
