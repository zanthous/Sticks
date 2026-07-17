// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    /// <summary>
    /// Extracts Sticks-specific results statistics from lazer hit events.
    /// </summary>
    public static class SticksScoreStatistics
    {
        public static Summary Calculate(IEnumerable<HitEvent> source)
        {
            HitEvent[] events = source.ToArray();
            HitEvent[] timingEvents = events.Where(hitEvent =>
                hitEvent.HitObject is ISticksAccuracyComponent
                {
                    AccuracyComponent: SticksAccuracyComponent.Timing,
                }).ToArray();

            double[] angleErrors = events.Where(hitEvent =>
                                               hitEvent.HitObject is ISticksAccuracyComponent
                                               {
                                                   AccuracyComponent: SticksAccuracyComponent.Angle,
                                               }
                                               && hitEvent.Position.HasValue)
                                         .Select(hitEvent => (double)Math.Abs(hitEvent.Position!.Value.X))
                                         .OrderBy(value => value)
                                         .ToArray();

            HitEvent[] trackingEvents = events.Where(hitEvent => hitEvent.HitObject is
                SticksSliderTick or SticksSliderRepeat or SticksSliderExtension or SticksHoldTick).ToArray();
            HitEvent[] tailEvents = events.Where(hitEvent => hitEvent.HitObject is SticksSliderTail or SticksHoldTail).ToArray();

            return new Summary(
                timingEvents,
                angleErrors.Length == 0 ? null : angleErrors.Average(),
                percentile(angleErrors, 0.95),
                trackingEvents.Count(hitEvent => hitEvent.Result.IsHit()),
                trackingEvents.Length,
                tailEvents.Count(hitEvent => hitEvent.Result.IsHit()),
                tailEvents.Length);
        }

        private static double? percentile(IReadOnlyList<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0)
                return null;

            double index = Math.Clamp(percentile, 0, 1) * (sortedValues.Count - 1);
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);
            double interpolation = index - lower;
            return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * interpolation;
        }

        public readonly record struct Summary(
            IReadOnlyList<HitEvent> TimingEvents,
            double? AverageAngleError,
            double? AngleError95thPercentile,
            int TrackingHits,
            int TrackingTotal,
            int TailHits,
            int TailTotal);
    }
}
