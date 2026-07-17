// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Sticks.Difficulty
{
    /// <summary>
    /// Calculates local Sticks performance using the component structure and score treatment used
    /// by osu!standard. Sticks-specific skills remain separate until the final performance norm.
    /// </summary>
    public class SticksPerformanceCalculator : PerformanceCalculator
    {
        public const double PERFORMANCE_BASE_MULTIPLIER = 1.12;
        public const double PERFORMANCE_NORM_EXPONENT = 1.1;

        public SticksPerformanceCalculator()
            : base(new SticksRuleset())
        {
        }

        protected override PerformanceAttributes CreatePerformanceAttributes(ScoreInfo score, DifficultyAttributes attributes)
        {
            var sticksAttributes = (SticksDifficultyAttributes)attributes;
            var results = new ScoreResults(score, sticksAttributes);
            SkillRatings ratings = calibrateSkillRatings(sticksAttributes);

            double mechanicalValue = computeMechanicalValue(ratings.Mechanical, results, sticksAttributes);
            double readingValue = computeReadingValue(ratings.Reading, results, sticksAttributes);
            double controlValue = computeControlValue(ratings.Control, results, sticksAttributes);
            double coordinationValue = computeCoordinationValue(ratings.Coordination, results, sticksAttributes);
            double accuracyValue = computeAccuracyValue(results.HeadAccuracy, results.AccuracyObjectCount, results.RateAdjustedOverallDifficulty);

            double totalValue = DiffUtils.Norm(
                PERFORMANCE_NORM_EXPONENT,
                mechanicalValue,
                readingValue,
                controlValue,
                coordinationValue,
                accuracyValue) * PERFORMANCE_BASE_MULTIPLIER;

            return new SticksPerformanceAttributes
            {
                Mechanical = mechanicalValue,
                Reading = readingValue,
                Control = controlValue,
                Coordination = coordinationValue,
                Accuracy = accuracyValue,
                EffectiveMissCount = results.EffectiveMissCount,
                Total = totalValue,
            };
        }

        /// <summary>
        /// Matches osu!standard's conversion from one skill difficulty rating to performance.
        /// </summary>
        internal static double DifficultyToPerformance(double difficulty) => 4 * DiffUtils.Pow(difficulty, 3);

        private static SkillRatings calibrateSkillRatings(SticksDifficultyAttributes attributes)
        {
            var raw = new SkillRatings(
                Math.Max(0, attributes.MechanicalDifficulty),
                Math.Max(0, attributes.ReadingDifficulty),
                Math.Max(0, attributes.ControlDifficulty),
                Math.Max(0, attributes.CoordinationDifficulty));

            double rawPerformance = DiffUtils.Norm(
                PERFORMANCE_NORM_EXPONENT,
                DifficultyToPerformance(raw.Mechanical),
                DifficultyToPerformance(raw.Reading),
                DifficultyToPerformance(raw.Control),
                DifficultyToPerformance(raw.Coordination));

            if (rawPerformance <= 0 || attributes.StarRating <= 0)
                return default;

            // osu!standard obtains stars by taking the cube root of its combined base skill
            // performance. Scale Sticks' independently-calibrated skill ratings so the same
            // relationship produces the displayed Sticks star rating before score penalties.
            double rawStars = Math.Cbrt(rawPerformance * PERFORMANCE_BASE_MULTIPLIER);
            double scale = attributes.StarRating / rawStars;
            return raw.Scale(scale);
        }

        private static double computeMechanicalValue(double difficulty, ScoreResults results, SticksDifficultyAttributes attributes)
        {
            if (difficulty <= 0)
                return 0;

            double value = DifficultyToPerformance(difficulty);
            value *= calculateMissPenalty(results.EffectiveMissCount, attributes.MechanicalDifficultStrainCount);

            double? deviation = results.CalculateTimingDeviation();
            if (deviation == null)
            {
                // Scores produced without hit events cannot separate timing and angle result
                // distributions. Preserve a useful value using standard's aim accuracy scaling.
                value *= results.TimingAccuracy;
                return value;
            }

            value *= calculateHighDeviationNerf(difficulty, deviation.Value);

            // This is osu!standard's speed accuracy treatment: harder mechanical difficulty asks
            // for a smaller effective timing window, then compares the score deviation to it.
            double effectiveHitWindow = 20 * DiffUtils.Pow(4 / difficulty, 0.35);
            double effectiveAccuracy = DiffUtils.Erf(effectiveHitWindow / deviation.Value);
            value *= DiffUtils.Pow(effectiveAccuracy, 2);
            return value;
        }

        private static double computeReadingValue(double difficulty, ScoreResults results, SticksDifficultyAttributes attributes)
        {
            if (difficulty <= 0)
                return 0;

            double value = DifficultyToPerformance(difficulty);
            value *= calculateMissPenalty(results.EffectiveMissCount, attributes.ReadingDifficultStrainCount);

            // osu!standard's reading component uses a deliberately harsh cubic accuracy scale.
            value *= DiffUtils.Pow(results.HeadAccuracy, 3);
            return value;
        }

        private static double computeControlValue(double difficulty, ScoreResults results, SticksDifficultyAttributes attributes)
        {
            if (difficulty <= 0)
                return 0;

            double value = DifficultyToPerformance(difficulty);

            // Control is Sticks' closest analogue to standard aim and continuous slider aim.
            value *= calculateLengthBonus(results.AccuracyObjectCount);
            value *= calculateMissPenalty(results.EffectiveMissCount, attributes.ControlDifficultStrainCount);
            value *= results.AngleAccuracy;

            // Standard's difficult-slider nerf cubes the successfully-followed proportion when
            // all of a skill comes from sliders. Apply that only to Sticks' control component,
            // rather than counting tracking loss again in global score accuracy.
            value *= DiffUtils.Pow(results.TrackingCompletion, 3);
            return value;
        }

        private static double computeCoordinationValue(double difficulty, ScoreResults results, SticksDifficultyAttributes attributes)
        {
            if (difficulty <= 0)
                return 0;

            double value = DifficultyToPerformance(difficulty);
            value *= calculateMissPenalty(results.EffectiveMissCount, attributes.CoordinationDifficultStrainCount);

            // Coordination is an execution skill, so use standard aim's linear accuracy scaling.
            value *= results.HeadAccuracy;
            return value;
        }

        private static double computeAccuracyValue(double accuracy, int accuracyObjectCount, double overallDifficulty)
        {
            if (accuracyObjectCount <= 0)
                return 0;

            // This is osu!standard's dedicated accuracy component. The Sticks input is the
            // equally-weighted timing/angle head accuracy; tracking checkpoints and tails are
            // intentionally excluded from this percentage.
            double value = DiffUtils.Pow(1.52163, overallDifficulty) * DiffUtils.Pow(accuracy, 24) * 2.83;
            value *= accuracyObjectCount < 1000
                ? DiffUtils.Pow(accuracyObjectCount / 1000.0, 0.3)
                : DiffUtils.Pow(accuracyObjectCount / 1000.0, 0.1);
            return value;
        }

        private static double calculateLengthBonus(int totalHits) =>
            0.95 + 0.35 * Math.Min(1, totalHits / 2000.0)
                 + (totalHits > 2000 ? Math.Log10(totalHits / 2000.0) * 0.5 : 0);

        private static double calculateMissPenalty(double missCount, double difficultStrainCount)
        {
            if (missCount <= 0)
                return 1;

            // Exact osu!standard miss-penalty shape. A component with no meaningful difficult
            // strains has no performance to preserve after a miss.
            double strainDenominator = 4 * Math.Log(difficultStrainCount);
            return strainDenominator > 0
                ? 0.93 / (missCount / strainDenominator + 1)
                : 0;
        }

        private static double calculateHighDeviationNerf(double difficulty, double deviation)
        {
            double performanceValue = DifficultyToPerformance(difficulty);
            double excessDifficultyCutoff = 100 + 220 * DiffUtils.Pow(22 / deviation, 6.5);
            if (performanceValue <= excessDifficultyCutoff)
                return 1;

            const double scale = 50;
            double adjustedValue = scale * (Math.Log((performanceValue - excessDifficultyCutoff) / scale + 1)
                                            + excessDifficultyCutoff / scale);

            double lerp = 1 - DiffUtils.ReverseLerp(deviation, 22, 27);
            adjustedValue = double.Lerp(adjustedValue, performanceValue, lerp);
            return adjustedValue / performanceValue;
        }

        private readonly record struct SkillRatings(double Mechanical, double Reading, double Control, double Coordination)
        {
            public SkillRatings Scale(double scale) => new SkillRatings(
                Mechanical * scale,
                Reading * scale,
                Control * scale,
                Coordination * scale);
        }

        private sealed class ScoreResults
        {
            private readonly ResultCounts timing;
            private readonly double greatHitWindow;
            private readonly double okHitWindow;
            private readonly double mehHitWindow;

            public double TimingAccuracy { get; }
            public double AngleAccuracy { get; }
            public double HeadAccuracy => (TimingAccuracy + AngleAccuracy) / 2;
            public double TrackingCompletion { get; }
            public double EffectiveMissCount { get; }
            public int AccuracyObjectCount { get; }
            public double RateAdjustedOverallDifficulty { get; }

            public ScoreResults(ScoreInfo score, SticksDifficultyAttributes attributes)
            {
                HitEvent[] events = score.HitEvents.ToArray();

                timing = countResults(events.Where(isTimingEvent));
                ResultCounts angle = countResults(events.Where(isAngleEvent));
                ResultCounts aggregateHeads = resultCountsFromStatistics(score);
                double fallbackHeadAccuracy = aggregateHeads.Total > 0
                    ? aggregateHeads.Accuracy
                    : Math.Clamp(score.Accuracy, 0, 1);

                TimingAccuracy = timing.Total > 0 ? timing.Accuracy : fallbackHeadAccuracy;
                AngleAccuracy = angle.Total > 0 ? angle.Accuracy : fallbackHeadAccuracy;
                AccuracyObjectCount = attributes.AccuracyObjectCount > 0
                    ? attributes.AccuracyObjectCount
                    : Math.Max(timing.Total, angle.Total);

                int trackingHits = events.Count(hitEvent => isTrackingEvent(hitEvent) && hitEvent.Result.IsHit());
                int trackingMisses = events.Count(hitEvent => isTrackingEvent(hitEvent) && !hitEvent.Result.IsHit());
                int tailHits = events.Count(hitEvent => isTailEvent(hitEvent) && hitEvent.Result.IsHit());
                int tailMisses = events.Count(hitEvent => isTailEvent(hitEvent) && !hitEvent.Result.IsHit());

                if (trackingHits + trackingMisses == 0)
                {
                    trackingHits = resultCount(score, HitResult.LargeTickHit);
                    trackingMisses = resultCount(score, HitResult.LargeTickMiss);
                }

                if (tailHits + tailMisses == 0 && attributes.TailObjectCount > 0)
                {
                    tailHits = resultCount(score, HitResult.SliderTailHit);
                    tailMisses = Math.Max(0, attributes.TailObjectCount - tailHits);
                }

                int trackingTotal = trackingHits + trackingMisses + tailHits + tailMisses;
                TrackingCompletion = trackingTotal > 0
                    ? (double)(trackingHits + tailHits) / trackingTotal
                    : 1;

                double headMisses = timing.Total > 0
                    ? timing.Miss
                    : resultCount(score, HitResult.Miss) / 2.0;
                EffectiveMissCount = calculateEffectiveMissCount(
                    score.MaxCombo,
                    attributes.MaxCombo,
                    headMisses,
                    trackingMisses,
                    tailMisses,
                    attributes.TrackingObjectCount > 0 || trackingHits + trackingMisses > 0);

                double clockRate = double.IsFinite(attributes.ClockRate) && attributes.ClockRate > 0
                    ? attributes.ClockRate
                    : 1;
                var hitWindows = new SticksHitWindows();
                hitWindows.SetDifficulty(attributes.OverallDifficulty);
                greatHitWindow = hitWindows.WindowFor(HitResult.Great) / clockRate;
                okHitWindow = hitWindows.WindowFor(HitResult.Ok) / clockRate;
                mehHitWindow = hitWindows.WindowFor(HitResult.Meh) / clockRate;
                RateAdjustedOverallDifficulty = (79.5 - greatHitWindow) / 6;
            }

            public double? CalculateTimingDeviation()
            {
                if (timing.Great + timing.Ok + timing.Meh <= 0)
                    return null;

                double n = Math.Max(1, timing.Great + timing.Ok);
                double p = timing.Great / n;

                // osu!standard's 99% one-tailed confidence bound.
                const double z = 2.32634787404;
                double pLowerBound = Math.Min(p,
                    (n * p + z * z / 2) / (n + z * z)
                    - z / (n + z * z) * Math.Sqrt(n * p * (1 - p) + z * z / 4));

                double deviation;

                if (pLowerBound > 0.01)
                {
                    deviation = greatHitWindow / (DiffUtils.SQRT2 * DiffUtils.ErfInv(pLowerBound));
                    double okTailAmount = Math.Sqrt(2 / Math.PI) * okHitWindow
                                          * Math.Exp(-0.5 * DiffUtils.Pow(okHitWindow / deviation, 2))
                                          / (deviation * DiffUtils.Erf(okHitWindow / (DiffUtils.SQRT2 * deviation)));
                    deviation *= Math.Sqrt(1 - okTailAmount);
                }
                else
                {
                    deviation = okHitWindow / Math.Sqrt(3);
                }

                double mehVariance = (mehHitWindow * mehHitWindow
                                      + okHitWindow * mehHitWindow
                                      + okHitWindow * okHitWindow) / 3;
                deviation = Math.Sqrt(((timing.Great + timing.Ok) * DiffUtils.Pow(deviation, 2)
                                       + timing.Meh * mehVariance)
                                      / (timing.Great + timing.Ok + timing.Meh));
                return deviation;
            }

            private static double calculateEffectiveMissCount(int scoreMaxCombo, int mapMaxCombo, double headMisses,
                                                               int trackingMisses, int tailMisses, bool hasTracking)
            {
                if (!hasTracking)
                    return headMisses;

                double missCount = headMisses;
                double fullComboThreshold = Math.Max(0, mapMaxCombo - tailMisses);

                if (scoreMaxCombo < fullComboThreshold)
                    missCount = fullComboThreshold / Math.Max(1, scoreMaxCombo);

                // As in current non-classic osu!standard scoring, combo can estimate where a
                // break happened but cannot invent more breaks than recorded misses permit.
                missCount = Math.Min(missCount, headMisses + trackingMisses);
                return Math.Max(headMisses, missCount);
            }

            private static ResultCounts countResults(IEnumerable<HitEvent> events)
            {
                var result = new ResultCounts();

                foreach (HitEvent hitEvent in events)
                {
                    switch (hitEvent.Result)
                    {
                        case HitResult.Great:
                            result.Great++;
                            break;

                        case HitResult.Ok:
                            result.Ok++;
                            break;

                        case HitResult.Meh:
                            result.Meh++;
                            break;

                        case HitResult.Miss:
                            result.Miss++;
                            break;
                    }
                }

                return result;
            }

            private static ResultCounts resultCountsFromStatistics(ScoreInfo score) => new ResultCounts
            {
                Great = resultCount(score, HitResult.Great),
                Ok = resultCount(score, HitResult.Ok),
                Meh = resultCount(score, HitResult.Meh),
                Miss = resultCount(score, HitResult.Miss),
            };

            private static bool isTimingEvent(HitEvent hitEvent) => hitEvent.HitObject is ISticksAccuracyComponent
            {
                AccuracyComponent: SticksAccuracyComponent.Timing,
            };

            private static bool isAngleEvent(HitEvent hitEvent) => hitEvent.HitObject is ISticksAccuracyComponent
            {
                AccuracyComponent: SticksAccuracyComponent.Angle,
            };

            private static bool isTrackingEvent(HitEvent hitEvent) => hitEvent.HitObject is
                SticksSliderTick or SticksSliderRepeat or SticksSliderExtension or SticksHoldTick;

            private static bool isTailEvent(HitEvent hitEvent) => hitEvent.HitObject is SticksSliderTail or SticksHoldTail;

            private static int resultCount(ScoreInfo score, HitResult result) =>
                score.Statistics.TryGetValue(result, out int count) ? count : 0;

            private sealed class ResultCounts
            {
                public int Great;
                public int Ok;
                public int Meh;
                public int Miss;

                public int Total => Great + Ok + Meh + Miss;
                public double Accuracy => Total > 0
                    ? (Great * 300.0 + Ok * 100 + Meh * 50) / (Total * 300.0)
                    : 1;
            }
        }
    }
}
