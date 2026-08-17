using System;
using NUnit.Framework;
using osu.Game.Beatmaps;

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
                Assert.That(easy, Is.EqualTo(5.0 / 7).Within(0.0001));
                Assert.That(reference, Is.EqualTo(1).Within(0.0001));
                Assert.That(hard, Is.EqualTo(5.0 / 4).Within(0.0001));
            });
        }

        [Test]
        public void TestAngularStarAdjustmentIsSmallAndBounded()
        {
            const double developed_map_stars = 7.5;
            const double trivial_map_stars = 0.5;

            double easy = SticksDifficultyScaling.AngularPrecisionStarAdjustment(developed_map_stars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(35, 17.5f));
            double reference = SticksDifficultyScaling.AngularPrecisionStarAdjustment(developed_map_stars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(25, 12.5f));
            double hard = SticksDifficultyScaling.AngularPrecisionStarAdjustment(developed_map_stars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(20, 10));
            double trivialEasy = SticksDifficultyScaling.AngularPrecisionStarAdjustment(trivial_map_stars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(35, 17.5f));
            double trivialHard = SticksDifficultyScaling.AngularPrecisionStarAdjustment(trivial_map_stars,
                SticksDifficultyScaling.AngularPrecisionMultiplier(20, 10));

            Assert.Multiple(() =>
            {
                Assert.That(easy, Is.EqualTo(-SticksDifficultyScaling.MAX_ANGULAR_STAR_DECREASE));
                Assert.That(reference, Is.Zero.Within(0.0001));
                Assert.That(hard, Is.EqualTo(SticksDifficultyScaling.MAX_ANGULAR_STAR_INCREASE));
                Assert.That(hard - easy, Is.EqualTo(0.6).Within(0.0001));
                Assert.That(trivialEasy, Is.EqualTo(trivial_map_stars * (Math.Sqrt(5.0 / 7) - 1)).Within(0.0001));
                Assert.That(trivialHard, Is.EqualTo(trivial_map_stars * (Math.Sqrt(5.0 / 4) - 1)).Within(0.0001));
                Assert.That(SticksDifficultyScaling.AngularPrecisionStarAdjustment(developed_map_stars, 100),
                    Is.EqualTo(SticksDifficultyScaling.MAX_ANGULAR_STAR_INCREASE));
                Assert.That(SticksDifficultyScaling.AngularPrecisionStarAdjustment(developed_map_stars, 0.001),
                    Is.EqualTo(-SticksDifficultyScaling.MAX_ANGULAR_STAR_DECREASE));
            });
        }

        [Test]
        public void TestAngularPrecisionInterpolationIsMonotonic()
        {
            double previous = SticksDifficultyScaling.AngularPrecisionMultiplier(3);

            for (float circleSize = 3.05f; circleSize <= 5.4f; circleSize += 0.05f)
            {
                double current = SticksDifficultyScaling.AngularPrecisionMultiplier(circleSize);
                Assert.That(current, Is.GreaterThan(previous), $"CS {circleSize} should increase angular precision demand.");
                previous = current;
            }

            Assert.Multiple(() =>
            {
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(0), Is.EqualTo(5.0 / 7).Within(0.0001));
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(10), Is.EqualTo(5.0 / 4).Within(0.0001));
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
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(35, 17.5f), Is.EqualTo(5.0 / 7).Within(0.0001));
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(25, 12.5f), Is.EqualTo(1).Within(0.0001));
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(20, 10), Is.EqualTo(5.0 / 4).Within(0.0001));
                Assert.That(SticksDifficultyScaling.AngularPrecisionMultiplier(35, 15), Is.EqualTo(Math.Sqrt(15.0 / 28)).Within(0.0001));
            });
        }
    }
}
