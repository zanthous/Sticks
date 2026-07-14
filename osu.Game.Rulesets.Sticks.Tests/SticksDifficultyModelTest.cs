// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    /// <summary>
    /// Behavioural constraints for the Sticks difficulty model. These intentionally test ordering
    /// rather than exact star values so calibration can evolve without losing the agreed design.
    /// </summary>
    [TestFixture]
    public class SticksDifficultyModelTest
    {
        [Test]
        public void TestLongerBurstsAccumulateDifficulty()
        {
            double triple = rate(clusteredStream(3, 125));
            double sixNotes = rate(clusteredStream(6, 125));
            double twelveNotes = rate(clusteredStream(12, 125));

            Assert.Multiple(() =>
            {
                Assert.That(sixNotes, Is.GreaterThan(triple));
                Assert.That(twelveNotes, Is.GreaterThan(sixNotes));
            });
        }

        [Test]
        public void TestStreamLengthUsesDiminishingGrowth()
        {
            double fiveSeconds = rate(clusteredStream(noteCountFor(5, 125), 125));
            double tenSeconds = rate(clusteredStream(noteCountFor(10, 125), 125));
            double thirtySeconds = rate(clusteredStream(noteCountFor(30, 125), 125));

            Assert.Multiple(() =>
            {
                Assert.That(tenSeconds, Is.GreaterThan(fiveSeconds));
                Assert.That(thirtySeconds, Is.GreaterThan(tenSeconds));

                // Twenty additional seconds must add less difficulty per second than the first
                // five additional seconds. Sustaining a pattern matters, but does not scale linearly.
                Assert.That((thirtySeconds - tenSeconds) / 20,
                    Is.LessThan((tenSeconds - fiveSeconds) / 5));
                Assert.That(thirtySeconds, Is.LessThan(fiveSeconds * 2));
            });
        }

        [Test]
        public void TestFastSameStickResetsAreHarderThanAlternating()
        {
            const int count = 12;
            const double interval = 125;

            double alternating = rate(flicks(count, interval,
                i => i % 2 == 0 ? StickSide.Left : StickSide.Right,
                _ => 0));
            double sameStick = rate(flicks(count, interval, _ => StickSide.Left, _ => 0));

            Assert.That(sameStick, Is.GreaterThan(alternating));
        }

        [Test]
        public void TestAngularNoveltyIsReadingStrainRatherThanRawTravel()
        {
            const int count = 24;
            const double interval = 180;
            float[] scatteredAngles = { 0, 75, 215, 310, 125, 265, 30, 190, 335, 145, 280, 55 };

            double clustered = rate(flicks(count, interval,
                i => i % 2 == 0 ? StickSide.Left : StickSide.Right,
                _ => 0));
            double repeatedWidePattern = rate(flicks(count, interval,
                i => i % 2 == 0 ? StickSide.Left : StickSide.Right,
                i => i % 2 == 0 ? 0 : 180));
            double scattered = rate(flicks(count, interval,
                i => i % 2 == 0 ? StickSide.Left : StickSide.Right,
                i => scatteredAngles[i % scatteredAngles.Length]));

            Assert.Multiple(() =>
            {
                Assert.That(repeatedWidePattern, Is.GreaterThan(clustered));

                // The repeated pattern travels 180 degrees on every transition. The scattered
                // pattern has less raw travel on average, but requires substantially more reading.
                Assert.That(scattered, Is.GreaterThan(repeatedWidePattern));
            });
        }

        [Test]
        public void TestLongSlowSliderIsEasyRelativeToFastSliderAndBurst()
        {
            double longSlowSlider = rate(new[]
            {
                slider(1000, 5000, StickSide.Left, 0, 180),
            });
            double fastSlider = rate(new[]
            {
                slider(1000, 1000, StickSide.Left, 0, 180),
            });
            double burst = rate(clusteredStream(6, 125));

            Assert.Multiple(() =>
            {
                Assert.That(longSlowSlider, Is.LessThan(fastSlider));
                Assert.That(longSlowSlider, Is.LessThan(burst));
            });
        }

        [Test]
        public void TestReversalAddsModestControlDifficulty()
        {
            var noReversal = slider(1000, 1800, StickSide.Left, 0, 180);
            var reversal = slider(1000, 1800, StickSide.Left, 0, 90);
            reversal.SetCustomSegments(new[] { 90f, -90f });
            var twiceAsFast = slider(1000, 900, StickSide.Left, 0, 180);

            double noReversalStars = rate(new[] { noReversal });
            double reversalStars = rate(new[] { reversal });
            double fastStars = rate(new[] { twiceAsFast });

            Assert.Multiple(() =>
            {
                Assert.That(reversalStars, Is.GreaterThan(noReversalStars));
                Assert.That(reversalStars, Is.LessThan(fastStars));
            });
        }

        [Test]
        public void TestOtherStickActivityDuringSliderAddsCoordinationStrain()
        {
            var overlap = new List<SticksHitObject>
            {
                slider(1000, 2000, StickSide.Left, 0, 180),
            };
            var sequential = new List<SticksHitObject>
            {
                slider(1000, 2000, StickSide.Left, 0, 180),
            };

            for (int i = 0; i < 7; i++)
            {
                overlap.Add(flick(1250 + i * 250, StickSide.Right, 90));
                sequential.Add(flick(3250 + i * 250, StickSide.Right, 90));
            }

            Assert.That(rate(overlap), Is.GreaterThan(rate(sequential)));
        }

        [Test]
        public void TestIsolatedChordHasModestCoordinationCost()
        {
            double single = rate(new[]
            {
                flick(1000, StickSide.Left, 0),
            });
            double chord = rate(new[]
            {
                flick(1000, StickSide.Left, 0),
                flick(1000, StickSide.Right, 0),
            });
            double burst = rate(clusteredStream(6, 125));

            Assert.Multiple(() =>
            {
                Assert.That(chord, Is.GreaterThan(single));
                Assert.That(chord, Is.LessThan(burst));
            });
        }

        [Test]
        public void TestCircleSizePrecisionHasBoundedThresholds()
        {
            double thirtyDegrees = rate(scatteredStream(), circleSize: 3);
            double twentyDegrees = rate(scatteredStream(), circleSize: 4);
            double fifteenDegrees = rate(scatteredStream(), circleSize: 5.4f);

            Assert.Multiple(() =>
            {
                Assert.That(twentyDegrees, Is.GreaterThan(thirtyDegrees));
                Assert.That(fifteenDegrees, Is.GreaterThan(twentyDegrees));
                Assert.That(fifteenDegrees - twentyDegrees, Is.LessThanOrEqualTo(SticksDifficultyScaling.MAX_ANGULAR_STAR_INCREASE + 0.0001));
                Assert.That(twentyDegrees - thirtyDegrees, Is.LessThanOrEqualTo(SticksDifficultyScaling.MAX_ANGULAR_STAR_DECREASE + 0.0001));
                Assert.That(fifteenDegrees - thirtyDegrees, Is.LessThanOrEqualTo(0.6001));
            });
        }

        [Test]
        public void TestOverallDifficultyAffectsTimingDifficulty()
        {
            double lenientTiming = rate(clusteredStream(24, 125), overallDifficulty: 3);
            double strictTiming = rate(clusteredStream(24, 125), overallDifficulty: 8);

            Assert.That(strictTiming, Is.GreaterThan(lenientTiming));
        }

        [Test]
        public void TestApproachRateIsExcludedFromDifficulty()
        {
            double lowApproachRate = rate(scatteredStream(), approachRate: 0);
            double highApproachRate = rate(scatteredStream(), approachRate: 10);

            Assert.That(highApproachRate, Is.EqualTo(lowApproachRate).Within(0.0000001));
        }

        private static IEnumerable<SticksHitObject> clusteredStream(int count, double interval) =>
            flicks(count, interval,
                i => i % 2 == 0 ? StickSide.Left : StickSide.Right,
                i => new[] { 0f, 10f, 20f }[i % 3]);

        private static IEnumerable<SticksHitObject> scatteredStream()
        {
            float[] angles = { 0, 75, 215, 310, 125, 265, 30, 190, 335, 145, 280, 55 };
            return flicks(angles.Length, 160,
                i => i % 2 == 0 ? StickSide.Left : StickSide.Right,
                i => angles[i]);
        }

        private static IEnumerable<SticksHitObject> flicks(int count, double interval,
                                                           Func<int, StickSide> sideAt, Func<int, float> angleAt)
        {
            for (int i = 0; i < count; i++)
                yield return flick(1000 + i * interval, sideAt(i), angleAt(i));
        }

        private static SticksFlick flick(double time, StickSide side, float angle) => new SticksFlick
        {
            StartTime = time,
            Side = side,
            Angle = angle,
        };

        private static SticksSlider slider(double time, double duration, StickSide side, float angle, float arcAngle) => new SticksSlider
        {
            StartTime = time,
            Duration = duration,
            Side = side,
            Angle = angle,
            ArcAngle = arcAngle,
        };

        private static int noteCountFor(int seconds, double interval) => (int)(seconds * 1000 / interval);

        private static double rate(IEnumerable<SticksHitObject> hitObjects, float circleSize = 4,
                                   float overallDifficulty = 5, float approachRate = 5)
        {
            SticksHitObject[] objects = hitObjects.ToArray();
            var difficulty = new BeatmapDifficulty
            {
                CircleSize = circleSize,
                OverallDifficulty = overallDifficulty,
                ApproachRate = approachRate,
                SliderTickRate = 1,
            };
            var controlPoints = new ControlPointInfo();

            foreach (SticksHitObject hitObject in objects)
                hitObject.ApplyDefaults(controlPoints, difficulty);

            return SticksDifficultyCalculator.CalculateStarRating(objects, overallDifficulty: overallDifficulty);
        }
    }
}
