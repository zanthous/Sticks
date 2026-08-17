using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Mods;
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
        public void TestSustainedPatternHasMoreDifficultStrainsForPerformanceMissPenalty()
        {
            SticksDifficultyBreakdown triple = SticksDifficultyCalculator.CalculateDifficulty(clusteredStream(3, 125));
            SticksDifficultyBreakdown twelveNotes = SticksDifficultyCalculator.CalculateDifficulty(clusteredStream(12, 125));

            Assert.Multiple(() =>
            {
                Assert.That(triple.MechanicalDifficultStrainCount, Is.GreaterThan(0));
                Assert.That(twelveNotes.MechanicalDifficultStrainCount, Is.GreaterThan(triple.MechanicalDifficultStrainCount));
                Assert.That(twelveNotes.ReadingDifficultStrainCount, Is.GreaterThan(triple.ReadingDifficultStrainCount));
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
                // pattern has less raw travel on average, but still requires somewhat more reading.
                Assert.That(scattered, Is.GreaterThan(repeatedWidePattern));
                Assert.That(scattered - repeatedWidePattern, Is.LessThan(0.75),
                    "A predictable 180-degree pattern should only receive a modest reading discount.");
            });
        }

        [Test]
        public void TestSpatialSearchKeepsLargeJumpCostBeforeBroadRegionBonus()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksDifficultyModel.SpatialSearchMultiplier(1, 0.5, 2), Is.GreaterThan(1),
                    "A predictable two-region 180-degree pattern must retain its large-jump cost.");
                Assert.That(SticksDifficultyModel.SpatialSearchMultiplier(0, 1, 8), Is.EqualTo(1),
                    "Region coverage alone must not matter without an angular jump.");
                Assert.That(SticksDifficultyModel.SpatialSearchMultiplier(0.5, 0.5, 3), Is.GreaterThan(1));
                Assert.That(SticksDifficultyModel.SpatialSearchMultiplier(0.5, 0.5, 6),
                    Is.GreaterThan(SticksDifficultyModel.SpatialSearchMultiplier(0.5, 0.5, 3)));
                Assert.That(SticksDifficultyModel.SpatialSearchMultiplier(1, 1, 8),
                    Is.EqualTo(SticksDifficultyModel.SpatialSearchMultiplier(1, 1, 6)),
                    "The search term should saturate once the pattern already covers most of the circle.");
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

        [Test]
        public void TestDifficultyIsIndependentOfInputOrder()
        {
            SticksHitObject[] ordered = scatteredStream().ToArray();
            SticksHitObject[] reversed = ordered.Reverse().ToArray();

            Assert.That(rate(reversed), Is.EqualTo(rate(ordered)).Within(0.0000001));
        }

        [Test]
        public void TestCalculatorCalculateOrdersUnorderedTopLevelObjects()
        {
            var ruleset = new SticksRuleset();
            var difficulty = new BeatmapDifficulty { OverallDifficulty = 6 };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, difficulty),
            };

            beatmap.HitObjects.AddRange(new SticksHitObject[]
            {
                flick(1540, StickSide.Right, 300),
                flick(1000, StickSide.Left, 0),
                flick(1360, StickSide.Left, 210),
                flick(1180, StickSide.Right, 75),
            });

            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);

            var actual = (SticksDifficultyAttributes)new SticksDifficultyCalculator(
                ruleset.RulesetInfo,
                new PassthroughWorkingBeatmap(beatmap)).Calculate();
            SticksDifficultyBreakdown expected = SticksDifficultyCalculator.CalculateDifficultyIndependent(
                beatmap.HitObjects,
                overallDifficulty: difficulty.OverallDifficulty);

            assertDifficultyMatches(actual, expected);
        }

        [Test]
        public void TestTimedDifficultyMatchesIndependentPrefixes()
        {
            var ruleset = new SticksRuleset();
            var difficulty = new BeatmapDifficulty
            {
                CircleSize = 4,
                OverallDifficulty = 6,
                ApproachRate = 5,
                SliderTickRate = 1,
            };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, difficulty),
            };

            beatmap.HitObjects.AddRange(scatteredStream());

            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);

            var calculator = new SticksDifficultyCalculator(ruleset.RulesetInfo, new PassthroughWorkingBeatmap(beatmap));
            List<TimedDifficultyAttributes> timed = calculator.CalculateTimed();

            Assert.That(timed, Has.Count.EqualTo(beatmap.HitObjects.Count));

            for (int i = 0; i < timed.Count; i++)
            {
                SticksDifficultyBreakdown expected = SticksDifficultyCalculator.CalculateDifficultyIndependent(
                    beatmap.HitObjects.Take(i + 1),
                    overallDifficulty: difficulty.OverallDifficulty);
                var actual = (SticksDifficultyAttributes)timed[i].Attributes;

                Assert.Multiple(() =>
                {
                    Assert.That(actual.StarRating, Is.EqualTo(expected.StarRating).Within(0.0000001));
                    Assert.That(actual.MechanicalDifficulty, Is.EqualTo(expected.Mechanical).Within(0.0000001));
                    Assert.That(actual.ReadingDifficulty, Is.EqualTo(expected.Reading).Within(0.0000001));
                    Assert.That(actual.ControlDifficulty, Is.EqualTo(expected.Control).Within(0.0000001));
                    Assert.That(actual.CoordinationDifficulty, Is.EqualTo(expected.Coordination).Within(0.0000001));
                });
            }
        }

        [Test]
        public void TestTimedDifficultyMatchesIndependentPrefixesWithChordsAndOverlappingDurations()
        {
            var ruleset = new SticksRuleset();
            var difficulty = new BeatmapDifficulty
            {
                CircleSize = 4,
                OverallDifficulty = 6,
                ApproachRate = 5,
                SliderTickRate = 1,
            };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, difficulty),
            };

            beatmap.HitObjects.AddRange(new SticksHitObject[]
            {
                flick(1000, StickSide.Left, 0),
                flick(1000, StickSide.Right, 180),
                slider(1300, 1800, StickSide.Left, 45, 180),
                flick(1500, StickSide.Right, 90),
                flick(1750, StickSide.Right, 135),
                slider(2200, 900, StickSide.Left, 225, -90),
                flick(2200, StickSide.Right, 45),
                flick(2600, StickSide.Right, 300),
            });

            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);

            var calculator = new SticksDifficultyCalculator(ruleset.RulesetInfo, new PassthroughWorkingBeatmap(beatmap));
            List<TimedDifficultyAttributes> timed = calculator.CalculateTimed();
            var fullAttributes = (SticksDifficultyAttributes)new SticksDifficultyCalculator(
                ruleset.RulesetInfo,
                new PassthroughWorkingBeatmap(beatmap)).Calculate();

            Assert.That(timed, Has.Count.EqualTo(beatmap.HitObjects.Count));

            for (int i = 0; i < timed.Count; i++)
            {
                SticksDifficultyBreakdown expected = SticksDifficultyCalculator.CalculateDifficultyIndependent(
                    beatmap.HitObjects.Take(i + 1),
                    overallDifficulty: difficulty.OverallDifficulty);
                var actual = (SticksDifficultyAttributes)timed[i].Attributes;

                Assert.Multiple(() =>
                {
                    Assert.That(actual.StarRating, Is.EqualTo(expected.StarRating).Within(0.0000001), $"Prefix {i + 1}");
                    Assert.That(actual.MechanicalDifficulty, Is.EqualTo(expected.Mechanical).Within(0.0000001), $"Mechanical prefix {i + 1}");
                    Assert.That(actual.ReadingDifficulty, Is.EqualTo(expected.Reading).Within(0.0000001), $"Reading prefix {i + 1}");
                    Assert.That(actual.ControlDifficulty, Is.EqualTo(expected.Control).Within(0.0000001), $"Control prefix {i + 1}");
                    Assert.That(actual.CoordinationDifficulty, Is.EqualTo(expected.Coordination).Within(0.0000001), $"Coordination prefix {i + 1}");
                    Assert.That(actual.AngularPrecision, Is.EqualTo(expected.AngularPrecision).Within(0.0000001), $"Angular prefix {i + 1}");
                    Assert.That(actual.TimingPrecision, Is.EqualTo(expected.TimingPrecision).Within(0.0000001), $"Timing prefix {i + 1}");
                });
            }

            // Two two-note chord groups cost 1 + 2 evaluations each; all four other objects
            // are processed once. Completed prefixes are never replayed.
            Assert.Multiple(() =>
            {
                Assert.That(calculator.IncrementalObjectEvaluationCount, Is.EqualTo(10));
                Assert.That(calculator.ProcessedDifficultyCheckpointCount, Is.EqualTo(beatmap.HitObjects.Count));
                Assert.That(((SticksDifficultyAttributes)timed[^1].Attributes).MaxCombo, Is.EqualTo(fullAttributes.MaxCombo));
            });
        }

        [Test]
        public void TestDenseTimedDifficultyProcessesEachCompletedPrefixObjectOnce()
        {
            const int object_count = 512;
            var ruleset = new SticksRuleset();
            var difficulty = new BeatmapDifficulty { OverallDifficulty = 5 };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, difficulty),
            };

            beatmap.HitObjects.AddRange(flicks(object_count, 40,
                i => i % 2 == 0 ? StickSide.Left : StickSide.Right,
                i => i * 47 % 360));

            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);

            var calculator = new SticksDifficultyCalculator(ruleset.RulesetInfo, new PassthroughWorkingBeatmap(beatmap));
            List<TimedDifficultyAttributes> timed = calculator.CalculateTimed();

            Assert.Multiple(() =>
            {
                Assert.That(timed, Has.Count.EqualTo(object_count));
                Assert.That(calculator.IncrementalObjectEvaluationCount, Is.EqualTo(object_count));
                Assert.That(calculator.ProcessedDifficultyCheckpointCount, Is.EqualTo(object_count));
            });
        }

        [Test]
        public void TestDenseTimedDifficultyObservesCancellationAtCheckpoint()
        {
            const int object_count = 512;
            const int cancellation_checkpoint = 32;
            var ruleset = new SticksRuleset();
            var difficulty = new BeatmapDifficulty { OverallDifficulty = 5 };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, difficulty),
            };

            beatmap.HitObjects.AddRange(flicks(object_count, 20,
                i => i % 2 == 0 ? StickSide.Left : StickSide.Right,
                i => i * 53 % 360));

            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);

            using var cancellation = new CancellationTokenSource();
            var calculator = new InstrumentedDifficultyCalculator(
                ruleset.RulesetInfo,
                new PassthroughWorkingBeatmap(beatmap),
                checkpoint =>
                {
                    if (checkpoint == cancellation_checkpoint)
                        cancellation.Cancel();
                });

            Assert.Multiple(() =>
            {
                Assert.Throws<OperationCanceledException>(() => calculator.CalculateTimed(cancellation.Token));
                Assert.That(calculator.ProcessedDifficultyCheckpointCount, Is.EqualTo(cancellation_checkpoint));
            });
        }

        [Test]
        public void TestCancellationBridgeFollowsLazerEndTimeOrderingForOverlappingDurations()
        {
            var ruleset = new SticksRuleset();
            var difficulty = new BeatmapDifficulty { OverallDifficulty = 5 };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, difficulty),
            };

            // The first object ends after every later flick. lazer therefore traverses all bridge
            // checkpoints before requesting the first exact-prefix attribute. This documents why
            // the real incremental model cannot safely live inside Skill.Process().
            beatmap.HitObjects.AddRange(new SticksHitObject[]
            {
                slider(1000, 5000, StickSide.Left, 0, 180),
                flick(1200, StickSide.Right, 30),
                flick(1400, StickSide.Right, 90),
                flick(1600, StickSide.Right, 150),
                flick(1800, StickSide.Right, 210),
            });

            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);

            var calculator = new InstrumentedDifficultyCalculator(
                ruleset.RulesetInfo,
                new PassthroughWorkingBeatmap(beatmap));

            calculator.CalculateTimed();

            Assert.Multiple(() =>
            {
                Assert.That(calculator.ProcessedDifficultyCheckpointCount, Is.EqualTo(beatmap.HitObjects.Count));
                Assert.That(calculator.ModelObjectCountsAtCheckpoints, Has.Count.EqualTo(beatmap.HitObjects.Count));
                Assert.That(calculator.ModelObjectCountsAtCheckpoints, Is.All.EqualTo(0),
                    "Base end-time ordering consumed overlapping checkpoints before the first exact-prefix attribute.");
            });
        }

        [Test]
        public void TestCancellationAfterRunAheadStopsModelBeforeFirstPrefix()
        {
            var ruleset = new SticksRuleset();
            var difficulty = new BeatmapDifficulty { OverallDifficulty = 5 };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, difficulty),
            };

            beatmap.HitObjects.AddRange(new SticksHitObject[]
            {
                slider(1000, 5000, StickSide.Left, 0, 180),
                flick(1200, StickSide.Right, 30),
                flick(1400, StickSide.Right, 90),
                flick(1600, StickSide.Right, 150),
                flick(1800, StickSide.Right, 210),
            });

            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);

            using var cancellation = new CancellationTokenSource();
            var calculator = new InstrumentedDifficultyCalculator(
                ruleset.RulesetInfo,
                new PassthroughWorkingBeatmap(beatmap),
                checkpoint =>
                {
                    if (checkpoint == beatmap.HitObjects.Count)
                        cancellation.Cancel();
                });

            Assert.Multiple(() =>
            {
                Assert.Throws<OperationCanceledException>(() => calculator.CalculateTimed(cancellation.Token));
                Assert.That(calculator.ProcessedDifficultyCheckpointCount, Is.EqualTo(beatmap.HitObjects.Count));
                Assert.That(calculator.IncrementalObjectEvaluationCount, Is.Zero,
                    "Cancellation after the final run-ahead checkpoint must be observed before prefix model work begins.");
            });
        }

        [Test]
        public void TestCalculatorReuseResetsForMutationAndRateMods()
        {
            var ruleset = new SticksRuleset();
            var difficulty = new BeatmapDifficulty { OverallDifficulty = 5 };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, difficulty),
            };

            beatmap.HitObjects.AddRange(new SticksHitObject[]
            {
                flick(1000, StickSide.Left, 0),
                flick(1180, StickSide.Right, 20),
                flick(1360, StickSide.Left, 40),
                flick(1540, StickSide.Right, 60),
            });

            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);

            var calculator = new SticksDifficultyCalculator(ruleset.RulesetInfo, new PassthroughWorkingBeatmap(beatmap));
            var original = (SticksDifficultyAttributes)calculator.Calculate();

            beatmap.HitObjects[1].Angle = 200;
            beatmap.HitObjects[2].Angle = 300;
            var afterMutation = (SticksDifficultyAttributes)calculator.Calculate();
            SticksDifficultyBreakdown expectedMutation = SticksDifficultyCalculator.CalculateDifficultyIndependent(
                beatmap.HitObjects,
                overallDifficulty: difficulty.OverallDifficulty);

            var doubleTime = (SticksDifficultyAttributes)calculator.Calculate(new Mod[] { new SticksModDoubleTime() });
            SticksDifficultyBreakdown expectedDoubleTime = SticksDifficultyCalculator.CalculateDifficultyIndependent(
                beatmap.HitObjects,
                clockRate: 1.5,
                overallDifficulty: difficulty.OverallDifficulty);

            Assert.Multiple(() =>
            {
                Assert.That(afterMutation.StarRating, Is.EqualTo(expectedMutation.StarRating).Within(0.0000001));
                Assert.That(afterMutation.ReadingDifficulty, Is.EqualTo(expectedMutation.Reading).Within(0.0000001));
                Assert.That(afterMutation.StarRating, Is.Not.EqualTo(original.StarRating).Within(0.0000001),
                    "A same-count in-place map mutation must invalidate calculator state.");
                Assert.That(doubleTime.StarRating, Is.EqualTo(expectedDoubleTime.StarRating).Within(0.0000001));
                Assert.That(doubleTime.MechanicalDifficulty, Is.EqualTo(expectedDoubleTime.Mechanical).Within(0.0000001));
                Assert.That(calculator.IncrementalObjectEvaluationCount, Is.EqualTo(beatmap.HitObjects.Count));
            });
        }

        [Test]
        public void TestCalculatorReuseResetsWhenProcessedPrefixMutatesBeforeAppend()
        {
            var ruleset = new SticksRuleset();
            var difficulty = new BeatmapDifficulty { OverallDifficulty = 5 };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, difficulty),
            };

            beatmap.HitObjects.AddRange(new SticksHitObject[]
            {
                flick(1000, StickSide.Left, 0),
                flick(1180, StickSide.Right, 20),
                flick(1360, StickSide.Left, 40),
                flick(1540, StickSide.Right, 60),
            });

            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);

            var calculator = new SticksDifficultyCalculator(ruleset.RulesetInfo, new PassthroughWorkingBeatmap(beatmap));
            calculator.Calculate();

            // Mutate a middle object so boundary-reference checks alone cannot detect the stale
            // prefix, then extend the map before the calculator is queried again.
            beatmap.HitObjects[1].Angle = 245;
            beatmap.HitObjects[1].Side = StickSide.Left;
            SticksFlick appended = flick(1720, StickSide.Right, 315);
            appended.ApplyDefaults(beatmap.ControlPointInfo, difficulty);
            beatmap.HitObjects.Add(appended);

            var actual = (SticksDifficultyAttributes)calculator.Calculate();
            SticksDifficultyBreakdown expected = SticksDifficultyCalculator.CalculateDifficultyIndependent(
                beatmap.HitObjects,
                overallDifficulty: difficulty.OverallDifficulty);

            assertDifficultyMatches(actual, expected);
        }

        [Test]
        public void TestRankedValuesRollbackRestoresDuplicatesAndInsertionOrder()
        {
            SticksDifficultyModel.RollbackableRankedValues values =
                SticksDifficultyModel.RollbackableRankedValues.CreateAscending();

            values.Add(3);
            int checkpoint = values.Checkpoint;

            // Insert below, above, between, and equal to the checkpointed value. This exercises
            // both AVL subtrees and duplicate keys; rollback must remove each logged insertion.
            values.Add(1);
            values.Add(5);
            values.Add(2);
            values.Add(3);
            values.Add(4);

            Assert.That(values.Median(), Is.EqualTo(3));

            values.RollbackTo(checkpoint);

            Assert.Multiple(() =>
            {
                Assert.That(values.Checkpoint, Is.EqualTo(checkpoint));
                Assert.That(values.Median(), Is.EqualTo(3));
            });

            // Reusing a restored collection must not retain stale duplicate keys.
            values.Add(1);
            values.Add(1);
            Assert.That(values.Median(), Is.EqualTo(1));
        }

        [Test]
        public void TestRankedStrainsPreserveDuplicateWeightsAndPositiveFilterAcrossRollback()
        {
            const double harmonic_scale = 5;
            SticksDifficultyModel.RollbackableRankedValues strains =
                SticksDifficultyModel.RollbackableRankedValues.CreateStrains();

            strains.Add(4);
            strains.Add(2);
            int checkpoint = strains.Checkpoint;

            strains.Add(4);
            strains.Add(1);
            strains.Add(4);
            strains.Add(0);
            strains.Add(-3);
            strains.Add(double.NaN);

            Assert.That(strains.HarmonicDifficulty(harmonic_scale),
                Is.EqualTo(referenceHarmonic(new[] { 4d, 2, 4, 1, 4 }, harmonic_scale)));

            strains.RollbackTo(checkpoint);

            Assert.Multiple(() =>
            {
                Assert.That(strains.Checkpoint, Is.EqualTo(checkpoint));
                Assert.That(strains.HarmonicDifficulty(harmonic_scale),
                    Is.EqualTo(referenceHarmonic(new[] { 4d, 2 }, harmonic_scale)));
            });
        }

        [Test]
        public void TestRankedValueMutationWorkIsLogarithmicForAdversarialOrder()
        {
            const int value_count = 8192;
            const int checkpoint_count = value_count / 2;
            int logarithmicBound = 2 * (int)Math.Ceiling(Math.Log2(value_count + 1));
            SticksDifficultyModel.RollbackableRankedValues values =
                SticksDifficultyModel.RollbackableRankedValues.CreateAscending();

            // Sorted insertion is the adversarial case for an unbalanced search tree and caused
            // quadratic element shifts in the former sorted List implementation.
            for (int i = 0; i < checkpoint_count; i++)
                values.Add(i);

            int checkpoint = values.Checkpoint;

            for (int i = checkpoint_count; i < value_count; i++)
                values.Add(i);

            long insertionComparisons = values.MutationComparisonCount;

            Assert.Multiple(() =>
            {
                Assert.That(values.Count, Is.EqualTo(value_count));
                Assert.That(values.TreeHeight, Is.LessThanOrEqualTo(logarithmicBound));
                Assert.That(insertionComparisons, Is.LessThanOrEqualTo((long)value_count * logarithmicBound));
            });

            _ = values.Median();
            Assert.That(values.LastSelectionVisitCount, Is.LessThanOrEqualTo(2 * values.TreeHeight));

            values.RollbackTo(checkpoint);
            long removalComparisons = values.MutationComparisonCount - insertionComparisons;

            Assert.Multiple(() =>
            {
                Assert.That(values.Count, Is.EqualTo(checkpoint_count));
                Assert.That(values.TreeHeight, Is.LessThanOrEqualTo(logarithmicBound));
                Assert.That(removalComparisons, Is.LessThanOrEqualTo((long)(value_count - checkpoint_count) * logarithmicBound));
            });
        }

        [Test]
        public void TestHarmonicScanUsesCachedRankWeightsWithoutAllocationDependentTiming()
        {
            const int value_count = 1024;
            const double harmonic_scale = 5;
            SticksDifficultyModel.RollbackableRankedValues strains =
                SticksDifficultyModel.RollbackableRankedValues.CreateStrains();

            for (int i = 1; i <= value_count; i++)
                strains.Add(i);

            double first = strains.HarmonicDifficulty(harmonic_scale);

            Assert.Multiple(() =>
            {
                Assert.That(strains.LastHarmonicVisitCount, Is.EqualTo(value_count));
                Assert.That(strains.HarmonicWeightComputationCount, Is.EqualTo(value_count));
            });

            double second = strains.HarmonicDifficulty(harmonic_scale);

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.EqualTo(first));
                Assert.That(strains.LastHarmonicVisitCount, Is.EqualTo(value_count));
                Assert.That(strains.HarmonicWeightComputationCount, Is.EqualTo(value_count),
                    "An unchanged prefix should reuse every rank weight.");
            });

            strains.Add(value_count + 1);
            _ = strains.HarmonicDifficulty(harmonic_scale);

            Assert.Multiple(() =>
            {
                Assert.That(strains.LastHarmonicVisitCount, Is.EqualTo(value_count + 1));
                Assert.That(strains.HarmonicWeightComputationCount, Is.EqualTo(value_count + 1),
                    "Growing by one value should calculate exactly one new rank weight.");
            });
        }

        [Test]
        [Explicit("Non-gating Release benchmark for lazer's live-PP timed difficulty path.")]
        public void BenchmarkDenseTimedDifficulty()
        {
            const float overall_difficulty = 5;

            // Warm JIT and framework setup outside the measurements.
            Beatmap<SticksHitObject> warmup = createDenseBenchmarkBeatmap(64, overall_difficulty);
            _ = new SticksDifficultyCalculator(new SticksRuleset().RulesetInfo, new PassthroughWorkingBeatmap(warmup)).CalculateTimed();
            _ = SticksDifficultyCalculator.CalculateDifficultyIndependent(warmup.HitObjects, overallDifficulty: overall_difficulty);

            foreach (int objectCount in new[] { 500, 1000, 2000, 4000 })
            {
                Beatmap<SticksHitObject> beatmap = createDenseBenchmarkBeatmap(objectCount, overall_difficulty);
                var calculator = new SticksDifficultyCalculator(new SticksRuleset().RulesetInfo, new PassthroughWorkingBeatmap(beatmap));
                using var safetyCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();
                List<TimedDifficultyAttributes> timed = calculator.CalculateTimed(safetyCancellation.Token);
                stopwatch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                Assert.That(timed, Has.Count.EqualTo(objectCount));
                TestContext.Progress.WriteLine(
                    $"incremental {objectCount,4} objects: {stopwatch.Elapsed.TotalMilliseconds,8:0.0} ms, " +
                    $"{allocated / 1024d / 1024:0.0} MiB allocated, final {timed[^1].Attributes.StarRating:0.000}★");
            }

            // This reproduces the former timed implementation using the retained independent
            // prefix oracle. Keep it smaller because its purpose is comparison, not a CI budget.
            const int legacy_object_count = 500;
            Beatmap<SticksHitObject> legacyBeatmap = createDenseBenchmarkBeatmap(legacy_object_count, overall_difficulty);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long legacyAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var legacyStopwatch = Stopwatch.StartNew();
            SticksDifficultyBreakdown legacyFinal = default;

            for (int i = 0; i < legacyBeatmap.HitObjects.Count; i++)
            {
                legacyFinal = SticksDifficultyCalculator.CalculateDifficultyIndependent(
                    legacyBeatmap.HitObjects.Take(i + 1),
                    overallDifficulty: overall_difficulty);
            }

            legacyStopwatch.Stop();
            long legacyAllocated = GC.GetAllocatedBytesForCurrentThread() - legacyAllocatedBefore;
            TestContext.Progress.WriteLine(
                $"independent {legacy_object_count,4} prefixes: {legacyStopwatch.Elapsed.TotalMilliseconds,8:0.0} ms, " +
                $"{legacyAllocated / 1024d / 1024:0.0} MiB allocated, final {legacyFinal.StarRating:0.000}★");

            const int full_object_count = 4000;
            Beatmap<SticksHitObject> fullBeatmap = createDenseBenchmarkBeatmap(full_object_count, overall_difficulty);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long fullAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var fullStopwatch = Stopwatch.StartNew();
            SticksDifficultyBreakdown full = SticksDifficultyCalculator.CalculateDifficultyIndependent(
                fullBeatmap.HitObjects,
                overallDifficulty: overall_difficulty);
            fullStopwatch.Stop();
            long fullAllocated = GC.GetAllocatedBytesForCurrentThread() - fullAllocatedBefore;
            TestContext.Progress.WriteLine(
                $"full-map   {full_object_count,4} objects: {fullStopwatch.Elapsed.TotalMilliseconds,8:0.0} ms, " +
                $"{fullAllocated / 1024d / 1024:0.0} MiB allocated, final {full.StarRating:0.000}★");
        }

        private static Beatmap<SticksHitObject> createDenseBenchmarkBeatmap(int objectCount, float overallDifficulty)
        {
            var ruleset = new SticksRuleset();
            var difficulty = new BeatmapDifficulty
            {
                CircleSize = 4,
                OverallDifficulty = overallDifficulty,
                ApproachRate = 5,
                SliderTickRate = 1,
            };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, difficulty),
            };

            beatmap.HitObjects.AddRange(flicks(objectCount, 45,
                i => i % 2 == 0 ? StickSide.Left : StickSide.Right,
                i => i * 137 % 360));

            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);

            return beatmap;
        }

        private static double referenceHarmonic(IEnumerable<double> values, double harmonicScale)
        {
            double difficulty = 0;
            int index = 0;

            foreach (double value in values.Where(value => value > 0).OrderByDescending(value => value))
            {
                double weight = (1 + harmonicScale / (1 + index))
                                / (Math.Pow(index, 0.9) + 1 + harmonicScale / (1 + index));
                difficulty += value * weight;
                index++;
            }

            return difficulty;
        }

        private static void assertDifficultyMatches(SticksDifficultyAttributes actual, SticksDifficultyBreakdown expected)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.StarRating, Is.EqualTo(expected.StarRating).Within(0.0000001));
                Assert.That(actual.MechanicalDifficulty, Is.EqualTo(expected.Mechanical).Within(0.0000001));
                Assert.That(actual.ReadingDifficulty, Is.EqualTo(expected.Reading).Within(0.0000001));
                Assert.That(actual.ControlDifficulty, Is.EqualTo(expected.Control).Within(0.0000001));
                Assert.That(actual.CoordinationDifficulty, Is.EqualTo(expected.Coordination).Within(0.0000001));
                Assert.That(actual.AngularPrecision, Is.EqualTo(expected.AngularPrecision).Within(0.0000001));
                Assert.That(actual.TimingPrecision, Is.EqualTo(expected.TimingPrecision).Within(0.0000001));
                Assert.That(actual.MechanicalDifficultStrainCount, Is.EqualTo(expected.MechanicalDifficultStrainCount).Within(0.0000001));
                Assert.That(actual.ReadingDifficultStrainCount, Is.EqualTo(expected.ReadingDifficultStrainCount).Within(0.0000001));
                Assert.That(actual.ControlDifficultStrainCount, Is.EqualTo(expected.ControlDifficultStrainCount).Within(0.0000001));
                Assert.That(actual.CoordinationDifficultStrainCount, Is.EqualTo(expected.CoordinationDifficultStrainCount).Within(0.0000001));
            });
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

        private sealed class PassthroughWorkingBeatmap : FlatWorkingBeatmap
        {
            private readonly IBeatmap beatmap;

            public PassthroughWorkingBeatmap(IBeatmap beatmap)
                : base(beatmap)
            {
                this.beatmap = beatmap;
            }

            public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return beatmap;
            }
        }

        private sealed class InstrumentedDifficultyCalculator : SticksDifficultyCalculator
        {
            private readonly Action<int> checkpointAction;

            public readonly List<int> ModelObjectCountsAtCheckpoints = new List<int>();

            public InstrumentedDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap, Action<int> checkpointAction = null)
                : base(ruleset, beatmap)
            {
                this.checkpointAction = checkpointAction;
            }

            protected override void OnDifficultyCheckpointProcessed(int checkpoint)
            {
                ModelObjectCountsAtCheckpoints.Add(IncrementalObjectEvaluationCount);
                checkpointAction?.Invoke(checkpoint);
            }
        }
    }
}
