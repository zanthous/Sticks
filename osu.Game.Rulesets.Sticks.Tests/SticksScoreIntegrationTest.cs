// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Difficulty;
using osu.Game.Rulesets.Sticks.Edit.Setup;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksScoreIntegrationTest
    {
        [Test]
        public void TestAngleErrorIsRetainedInHitEventAndRewindsCleanly()
        {
            var processor = new SticksScoreProcessor(new SticksRuleset());
            var angle = new SticksAngleComponent { HitError = 12.5f };
            JudgementResult hit = result(angle, HitResult.Ok);

            processor.ApplyResult(hit);

            Assert.Multiple(() =>
            {
                Assert.That(processor.HitEvents, Has.Count.EqualTo(1));
                Assert.That(processor.HitEvents[0].Position, Is.Not.Null);
                Assert.That(processor.HitEvents[0].Position!.Value.X, Is.EqualTo(12.5f));
            });

            processor.RevertResult(hit);
            Assert.That(processor.HitEvents, Is.Empty);

            angle.HitError = null;
            processor.ApplyResult(result(angle, HitResult.Miss));
            Assert.That(processor.HitEvents.Single().Position, Is.Null,
                "A timeout or otherwise unmeasured miss must not reuse an earlier angle error.");
        }

        [Test]
        public void TestSticksStatisticsSeparateTimingAngleTrackingAndTails()
        {
            var timing = new SticksFlick();
            var angleA = new SticksAngleComponent();
            var angleB = new SticksAngleComponent();

            HitEvent[] events =
            {
                hitEvent(-12, HitResult.Great, timing),
                hitEvent(0, HitResult.Great, angleA, new Vector2(5, 0)),
                hitEvent(0, HitResult.Ok, angleB, new Vector2(15, 0)),
                hitEvent(0, HitResult.LargeTickHit, new SticksSliderTick()),
                hitEvent(0, HitResult.LargeTickMiss, new SticksHoldTick()),
                hitEvent(0, HitResult.SliderTailHit, new SticksSliderTail()),
                hitEvent(0, HitResult.IgnoreMiss, new SticksHoldTail()),
            };

            SticksScoreStatistics.Summary summary = SticksScoreStatistics.Calculate(events);

            Assert.Multiple(() =>
            {
                Assert.That(summary.TimingEvents, Has.Count.EqualTo(1));
                Assert.That(summary.AverageAngleError, Is.EqualTo(10).Within(0.001));
                Assert.That(summary.AngleError95thPercentile, Is.EqualTo(14.5).Within(0.001));
                Assert.That(summary.TrackingHits, Is.EqualTo(1));
                Assert.That(summary.TrackingTotal, Is.EqualTo(2));
                Assert.That(summary.TailHits, Is.EqualTo(1));
                Assert.That(summary.TailTotal, Is.EqualTo(2));
            });
        }

        [Test]
        public void TestPerformanceUsesSeparateStandardStyleComponents()
        {
            var calculator = new SticksPerformanceCalculator();
            SticksDifficultyAttributes attributes = performanceAttributes();
            var performance = (SticksPerformanceAttributes)calculator.Calculate(perfectScore(), attributes);

            double expectedTotal = Math.Pow(
                Math.Pow(performance.Mechanical, SticksPerformanceCalculator.PERFORMANCE_NORM_EXPONENT)
                + Math.Pow(performance.Reading, SticksPerformanceCalculator.PERFORMANCE_NORM_EXPONENT)
                + Math.Pow(performance.Control, SticksPerformanceCalculator.PERFORMANCE_NORM_EXPONENT)
                + Math.Pow(performance.Coordination, SticksPerformanceCalculator.PERFORMANCE_NORM_EXPONENT)
                + Math.Pow(performance.Accuracy, SticksPerformanceCalculator.PERFORMANCE_NORM_EXPONENT),
                1 / SticksPerformanceCalculator.PERFORMANCE_NORM_EXPONENT)
                * SticksPerformanceCalculator.PERFORMANCE_BASE_MULTIPLIER;

            Assert.Multiple(() =>
            {
                Assert.That(performance.Mechanical, Is.GreaterThan(0));
                Assert.That(performance.Reading, Is.GreaterThan(0));
                Assert.That(performance.Control, Is.GreaterThan(0));
                Assert.That(performance.Coordination, Is.GreaterThan(0));
                Assert.That(performance.Accuracy, Is.GreaterThan(0));
                Assert.That(performance.Total, Is.EqualTo(expectedTotal).Within(0.0000001));
                Assert.That(SticksPerformanceCalculator.DifficultyToPerformance(3), Is.EqualTo(108),
                    "Skill difficulty must use osu!standard's 4 * difficulty^3 conversion.");
                Assert.That(new SticksRuleset().CreatePerformanceCalculator(), Is.TypeOf<SticksPerformanceCalculator>());
            });
        }

        [Test]
        public void TestDedicatedAccuracyUsesStandardFormulaAndExcludesTracking()
        {
            var calculator = new SticksPerformanceCalculator();
            SticksDifficultyAttributes attributes = performanceAttributes();
            ScoreInfo perfect = perfectScore();
            ScoreInfo droppedTracking = perfectScore(trackingHits: 10, trackingMisses: 10, maxCombo: 80);

            var perfectPerformance = (SticksPerformanceAttributes)calculator.Calculate(perfect, attributes);
            var droppedPerformance = (SticksPerformanceAttributes)calculator.Calculate(droppedTracking, attributes);
            double expectedAccuracy = Math.Pow(1.52163, 5) * 2.83 * Math.Pow(100 / 1000.0, 0.3);

            Assert.Multiple(() =>
            {
                Assert.That(perfectPerformance.Accuracy, Is.EqualTo(expectedAccuracy).Within(0.0000001));
                Assert.That(droppedPerformance.Accuracy, Is.EqualTo(perfectPerformance.Accuracy).Within(0.0000001),
                    "Tracking must not be counted once in head accuracy and then penalised a second time.");
                Assert.That(droppedPerformance.Control, Is.LessThan(perfectPerformance.Control));
                Assert.That(droppedPerformance.Total, Is.LessThan(perfectPerformance.Total));
                Assert.That(droppedPerformance.EffectiveMissCount, Is.GreaterThan(0));
            });
        }

        [Test]
        public void TestStoredScoreFallbackRemovesTrackingFromHeadAccuracy()
        {
            var calculator = new SticksPerformanceCalculator();
            SticksDifficultyAttributes attributes = performanceAttributes();
            var storedScore = new ScoreInfo
            {
                // Deliberately includes the dropped tick. The PP accuracy component must instead
                // reconstruct head-only accuracy from the native head result statistics.
                Accuracy = 0.5,
                MaxCombo = 80,
            };
            storedScore.Statistics[HitResult.Great] = 200;
            storedScore.Statistics[HitResult.LargeTickHit] = 19;
            storedScore.Statistics[HitResult.LargeTickMiss] = 1;
            storedScore.Statistics[HitResult.SliderTailHit] = 10;

            var performance = (SticksPerformanceAttributes)calculator.Calculate(storedScore, attributes);
            double expectedAccuracy = Math.Pow(1.52163, 5) * 2.83 * Math.Pow(100 / 1000.0, 0.3);

            Assert.Multiple(() =>
            {
                Assert.That(performance.Accuracy, Is.EqualTo(expectedAccuracy).Within(0.0000001));
                Assert.That(performance.EffectiveMissCount, Is.GreaterThan(0));
            });
        }

        [Test]
        public void TestHeadAccuracyAffectsSkillsByStandardComponentRules()
        {
            var calculator = new SticksPerformanceCalculator();
            SticksDifficultyAttributes attributes = performanceAttributes();
            var perfect = (SticksPerformanceAttributes)calculator.Calculate(perfectScore(), attributes);
            var inaccurate = (SticksPerformanceAttributes)calculator.Calculate(
                scoreWithHeadResults(90, 10, 0, 0, 20, 0, 10, 130), attributes);

            Assert.Multiple(() =>
            {
                Assert.That(inaccurate.Mechanical, Is.LessThan(perfect.Mechanical));
                Assert.That(inaccurate.Reading, Is.LessThan(perfect.Reading));
                Assert.That(inaccurate.Control, Is.LessThan(perfect.Control));
                Assert.That(inaccurate.Coordination, Is.LessThan(perfect.Coordination));
                Assert.That(inaccurate.Accuracy, Is.LessThan(perfect.Accuracy));
                Assert.That(inaccurate.Total, Is.LessThan(perfect.Total));
            });
        }

        [Test]
        public void TestComboIsBreakEvidenceRatherThanGlobalMultiplier()
        {
            var calculator = new SticksPerformanceCalculator();
            SticksDifficultyAttributes attributes = performanceAttributes();
            ScoreInfo fullCombo = perfectScore(maxCombo: 130);
            ScoreInfo lowerComboWithoutRecordedBreak = perfectScore(maxCombo: 65);

            double fullComboPerformance = calculator.Calculate(fullCombo, attributes).Total;
            double lowerComboPerformance = calculator.Calculate(lowerComboWithoutRecordedBreak, attributes).Total;

            Assert.That(lowerComboPerformance, Is.EqualTo(fullComboPerformance).Within(0.0000001),
                "As in current osu!standard, combo may locate recorded breaks but cannot create misses on its own.");
        }

        [Test]
        public void TestNoFailDoesNotChangePerformance()
        {
            var calculator = new SticksPerformanceCalculator();
            SticksDifficultyAttributes attributes = performanceAttributes();
            ScoreInfo score = scoreWithHeadResults(98, 0, 0, 2, 18, 2, 10, 60);
            double withoutNoFail = calculator.Calculate(score, attributes).Total;

            score.Mods = new Mod[] { new SticksModNoFail() };
            double withNoFail = calculator.Calculate(score, attributes).Total;

            Assert.That(withNoFail, Is.EqualTo(withoutNoFail).Within(0.0000001));
        }

        [Test]
        public void TestBeatmapAttributesOmitMapApproachRate()
        {
            var ruleset = new SticksRuleset();
            var beatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, new BeatmapDifficulty
            {
                CircleSize = 4,
                ApproachRate = 10,
                OverallDifficulty = 5,
                DrainRate = 5,
            });

            var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, []).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(attributes.Select(attribute => attribute.Acronym), Is.EqualTo(new[] { "CS", "OD", "HP" }));
                Assert.That(attributes, Has.None.Matches<osu.Game.Rulesets.Difficulty.RulesetBeatmapAttribute>(attribute => attribute.Acronym == "AR"));
                Assert.That(attributes.Single(attribute => attribute.Acronym == "CS").AdditionalMetrics
                                      .Select(metric => metric.Name.ToString()),
                    Does.Contain("Primary hit angle"));
                Assert.That(attributes.Single(attribute => attribute.Acronym == "OD").AdditionalMetrics, Is.Not.Empty);
            });
        }

        [Test]
        public void TestEditorUsesSticksDifficultySection()
        {
            var sections = new SticksRuleset().CreateEditorSetupSections().ToArray();

            Assert.That(sections, Has.One.TypeOf<SticksDifficultySection>());
            Assert.That(sections, Has.None.TypeOf<osu.Game.Screens.Edit.Setup.DifficultySection>());
        }

        private static SticksDifficultyAttributes performanceAttributes() => new SticksDifficultyAttributes
        {
            StarRating = 5,
            MaxCombo = 130,
            MechanicalDifficulty = 2.5,
            ReadingDifficulty = 2,
            ControlDifficulty = 1.8,
            CoordinationDifficulty = 1.5,
            MechanicalDifficultStrainCount = 25,
            ReadingDifficultStrainCount = 20,
            ControlDifficultStrainCount = 15,
            CoordinationDifficultStrainCount = 12,
            AccuracyObjectCount = 100,
            TrackingObjectCount = 20,
            TailObjectCount = 10,
            OverallDifficulty = 5,
            ClockRate = 1,
        };

        private static ScoreInfo perfectScore(int trackingHits = 20, int trackingMisses = 0, int maxCombo = 130) =>
            scoreWithHeadResults(100, 0, 0, 0, trackingHits, trackingMisses, 10, maxCombo);

        private static ScoreInfo scoreWithHeadResults(int great, int ok, int meh, int miss,
                                                      int trackingHits, int trackingMisses, int tailHits, int maxCombo)
        {
            var events = new List<HitEvent>();

            addHeadEvents(events, great, HitResult.Great);
            addHeadEvents(events, ok, HitResult.Ok);
            addHeadEvents(events, meh, HitResult.Meh);
            addHeadEvents(events, miss, HitResult.Miss);

            for (int i = 0; i < trackingHits; i++)
                events.Add(hitEvent(0, HitResult.LargeTickHit, new SticksSliderTick()));

            for (int i = 0; i < trackingMisses; i++)
                events.Add(hitEvent(0, HitResult.LargeTickMiss, new SticksSliderTick()));

            for (int i = 0; i < tailHits; i++)
                events.Add(hitEvent(0, HitResult.SliderTailHit, new SticksSliderTail()));

            return new ScoreInfo
            {
                MaxCombo = maxCombo,
                HitEvents = events,
            };
        }

        private static void addHeadEvents(ICollection<HitEvent> events, int count, HitResult result)
        {
            for (int i = 0; i < count; i++)
            {
                events.Add(hitEvent(0, result, new SticksFlick()));
                events.Add(hitEvent(0, result, new SticksAngleComponent()));
            }
        }

        private static JudgementResult result(SticksHitObject hitObject, HitResult type) => new JudgementResult(hitObject, hitObject.CreateJudgement())
        {
            Type = type,
        };

        private static HitEvent hitEvent(double timeOffset, HitResult result, SticksHitObject hitObject, Vector2? position = null) =>
            new HitEvent(timeOffset, 1, result, hitObject, null!, position);
    }
}
