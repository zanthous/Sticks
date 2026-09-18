using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public partial class SticksStationarySliderSemanticsTest
    {
        [TestCase(0.75, false)]
        [TestCase(1, false)]
        [TestCase(1.5, false)]
        [TestCase(0.75, true)]
        [TestCase(1, true)]
        [TestCase(1.5, true)]
        public void TestStationaryRepresentationPreservesEveryDifficultyComponent(double clockRate, bool timed)
        {
            SticksHitObject[] legacy = mixedPattern(false, timed);
            SticksHitObject[] modern = mixedPattern(true, timed);
            var legacyState = new SticksDifficultyModel.IncrementalState(clockRate, 8);
            var modernState = new SticksDifficultyModel.IncrementalState(clockRate, 8);

            for (int i = 0; i < legacy.Length; i++)
            {
                legacyState.Append(legacy[i]);
                modernState.Append(modern[i]);
                Assert.That(modernState.GetBreakdown(), Is.EqualTo(legacyState.GetBreakdown()),
                    $"Changing a stationary sustain's representation changed the prefix ending at {legacy[i].StartTime}ms.");
            }

            Assert.That(SticksDifficultyCalculator.CalculateDifficulty(modern, clockRate, 8),
                Is.EqualTo(SticksDifficultyCalculator.CalculateDifficulty(legacy, clockRate, 8)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestStationaryCheckpointsPreserveScoreAndHealth(bool timed)
        {
            var legacy = new SticksHold { StartTime = 1000, Duration = 2000, Side = StickSide.Left, Angle = 30 };
            SticksSlider modern = stationarySlider(1000, 2000, StickSide.Left, 30, timed);
            applyDefaults(legacy);
            applyDefaults(modern);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(events(modern).Select(hitObject => (hitObject.StartTime, hitObject.Judgement.MaxResult)),
                    Is.EqualTo(events(legacy).Select(hitObject => (hitObject.StartTime, hitObject.Judgement.MaxResult))));
                Assert.That(modern.NestedHitObjects.OfType<SticksSliderRepeat>(), Is.Empty,
                    "A stationary path has no physical reversal to judge.");
                Assert.That(modern.NestedHitObjects.OfType<SticksSliderTick>().Select(tick => tick.StartTime),
                    Is.EqualTo(new[] { 1500d, 2000, 2500 }), "Zero-motion segment boundaries must not add checkpoints or reset tick phase.");
                Assert.That(modern.NestedHitObjects.OfType<SticksSliderTick>().All(tick => tick.IsStationary), Is.True);
                Assert.That(modern.NestedHitObjects.OfType<SticksSliderTail>().Single().IsStationary, Is.True);
            });

            var health = new TestHealthProcessor();
            double[] legacyRecovery = events(legacy).Select(hitObject => health.IncreaseFor(hitObject, hitObject.Judgement.MaxResult)).ToArray();
            double[] modernRecovery = events(modern).Select(hitObject => health.IncreaseFor(hitObject, hitObject.Judgement.MaxResult)).ToArray();
            Assert.That(modernRecovery, Is.EqualTo(legacyRecovery));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestStationaryTailPreservesLegacySuddenDeathBehaviour(bool failOnSliderTail)
        {
            var legacy = new SticksHold { StartTime = 1000, Duration = 2000 };
            SticksSlider stationary = stationarySlider(1000, 2000, StickSide.Left, 0, false);
            var moving = new SticksSlider { StartTime = 1000, Duration = 2000, ArcAngle = 90 };
            applyDefaults(legacy);
            applyDefaults(stationary);
            applyDefaults(moving);
            var mod = new TestSuddenDeath();
            mod.FailOnSliderTail.Value = failOnSliderTail;

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(mod.FailsFor(legacy.NestedHitObjects.OfType<SticksHoldTail>().Single(), HitResult.IgnoreMiss), Is.False);
                Assert.That(mod.FailsFor(stationary.NestedHitObjects.OfType<SticksSliderTail>().Single(), HitResult.IgnoreMiss), Is.False);
                Assert.That(mod.FailsFor(moving.NestedHitObjects.OfType<SticksSliderTail>().Single(), HitResult.IgnoreMiss), Is.EqualTo(failOnSliderTail));
                Assert.That(mod.FailsFor(moving.NestedHitObjects.OfType<SticksSliderTail>().Single(), HitResult.SliderTailHit), Is.False);
            });
        }

        [Test]
        public void TestMovingSliderWithDwellKeepsMovingDifficultyAndHealth()
        {
            var moving = new SticksSlider { StartTime = 1000, Duration = 2000 };
            moving.SetTimedSegments(new[] { 0f, 90f }, new[] { 1000d, 1000d });
            applyDefaults(moving);
            SticksSlider stationary = stationarySlider(1000, 2000, StickSide.Left, 0, true);
            applyDefaults(stationary);
            var health = new TestHealthProcessor();

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(moving.IsStationary, Is.False);
                Assert.That(moving.NestedHitObjects.OfType<SticksSliderTick>().All(tick => !tick.IsStationary), Is.True);
                Assert.That(moving.NestedHitObjects.OfType<SticksSliderTick>()
                    .Select(tick => health.IncreaseFor(tick, HitResult.LargeTickHit)), Is.All.EqualTo(0.015));
                Assert.That(SticksDifficultyCalculator.CalculateDifficulty(new[] { moving }).Control,
                    Is.GreaterThan(SticksDifficultyCalculator.CalculateDifficulty(new[] { stationary }).Control));
            });
        }

        private static SticksHitObject[] mixedPattern(bool useSliders, bool timed)
        {
            SticksHitObject sustain(double start, double duration, StickSide side, float angle) => useSliders
                ? stationarySlider(start, duration, side, angle, timed)
                : new SticksHold { StartTime = start, Duration = duration, Side = side, Angle = angle };

            return new[]
            {
                new SticksFlick { StartTime = 500, Side = StickSide.Left, Angle = 90 },
                sustain(1000, 2000, StickSide.Left, 30),
                new SticksFlick { StartTime = 1250, Side = StickSide.Right, Angle = 30 },
                new SticksClick { StartTime = 1500, Side = StickSide.Left },
                new SticksSlider { StartTime = 1750, Duration = 400, Side = StickSide.Right, Angle = 200, ArcAngle = 90 },
                new SticksFlick { StartTime = 2200, Side = StickSide.Right, Angle = 210 },
                sustain(3200, 1200, StickSide.Right, 250),
                new SticksSlider { StartTime = 3200, Duration = 1200, Side = StickSide.Left, Angle = 50, ArcAngle = -90 },
                new SticksFlick { StartTime = 4600, Side = StickSide.Right, Angle = 180 },
                // A legacy object can coexist with newly authored stationary sliders.
                new SticksHold { StartTime = 5000, Duration = 1000, Side = StickSide.Left, Angle = 75 },
                sustain(6500, 3000, StickSide.Left, 310),
                new SticksFlick { StartTime = 7000, Side = StickSide.Right, Angle = 310 },
                new SticksFlick { StartTime = 9800, Side = StickSide.Left, Angle = 0 },
            };
        }

        private static SticksSlider stationarySlider(double start, double duration, StickSide side, float angle, bool timed)
        {
            var slider = new SticksSlider { StartTime = start, Duration = duration, Side = side, Angle = angle, ArcAngle = 0 };
            if (timed)
                slider.SetTimedSegments(new[] { 0f, 0f, 0f }, new[] { duration * 0.25, duration * 0.35, duration * 0.4 });
            return slider;
        }

        private static void applyDefaults(SticksHitObject hitObject)
        {
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 500 });
            hitObject.ApplyDefaults(controlPoints, new BeatmapDifficulty { OverallDifficulty = 8, SliderTickRate = 1 });
        }

        private static IEnumerable<HitObject> events(HitObject hitObject)
        {
            yield return hitObject;
            foreach (HitObject nested in hitObject.NestedHitObjects.SelectMany(events))
                yield return nested;
        }

        private partial class TestHealthProcessor : SticksHealthProcessor
        {
            public TestHealthProcessor()
                : base(0)
            {
            }

            public double IncreaseFor(HitObject hitObject, HitResult result) =>
                GetHealthIncreaseFor(new JudgementResult(hitObject, hitObject.Judgement) { Type = result });
        }

        private class TestSuddenDeath : SticksModSuddenDeath
        {
            public bool FailsFor(HitObject hitObject, HitResult result) =>
                FailCondition(new TestHealthProcessor(), new JudgementResult(hitObject, hitObject.Judgement) { Type = result });
        }
    }
}
