using System;
using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Source slider checkpoints for experimental accompaniment. Uses the shared osu! event
    /// generator so repeat spans mirror tick positions rather than restarting their rhythm.
    /// </summary>
    internal static class SticksCounterpointCheckpoints
    {
        internal const int MAX_SPANS = 16;
        internal const int MAX_TICKS = 32;
        private const double minimum_tick_interval = 0.001;
        private const double maximum_normalised_distance = 100000;

        internal static IEnumerable<SliderEventDescriptor> Generate(HitObject original, IBeatmap beatmap,
                                                                     CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (original is not IHasDuration { Duration: > 0 } duration
                || !double.IsFinite(original.StartTime) || !double.IsFinite(duration.Duration) || !double.IsFinite(duration.EndTime))
                yield break;

            int repeats = original is IHasRepeats repeated ? repeated.RepeatCount : 0;
            // Reject excessive repeats, rather than changing their period by clamping the
            // span count. Eligible sources always retain all repeats and their true tail.
            if (repeats < 0 || repeats >= MAX_SPANS)
                yield break;
            int spans = repeats + 1;
            double spanDuration = duration.Duration / spans;
            double beatLength = beatmap.ControlPointInfo.TimingPointAt(original.StartTime).BeatLength;
            double tickRate = beatmap.Difficulty.SliderTickRate;
            double tickDistance = beatLength > 0 && double.IsFinite(beatLength) && tickRate > 0 && double.IsFinite(tickRate)
                ? beatLength / tickRate : 0;

            // Matches OsuBeatmapConverter's pre-v8 tick-distance adjustment. The source
            // duration already includes SV; only the historical tick-density multiplier
            // belongs here. Modern sliders do not get this multiplier a second time.
            if (beatmap.BeatmapVersion < 8)
            {
                double sliderVelocity = beatmap.ControlPointInfo is LegacyControlPointInfo legacy
                    ? legacy.DifficultyPointAt(original.StartTime).SliderVelocity
                    : (original as IHasSliderVelocity)?.SliderVelocityMultiplier ?? 1;
                tickDistance = sliderVelocity > 0 && double.IsFinite(sliderVelocity) ? tickDistance / sliderVelocity : 0;
            }

            // With velocity=1 and distance=span duration, path distance is measured in
            // milliseconds. The shared generator's ten-millisecond tail exclusion and
            // mirrored repeat ticks therefore retain their usual temporal meaning.
            double ticksPerSpan = tickDistance > minimum_tick_interval && double.IsFinite(tickDistance)
                ? Math.Max(0, Math.Ceiling((spanDuration - 10) / tickDistance) - 1)
                : double.PositiveInfinity;
            bool generateTicks = original is not IHasGenerateTicks { GenerateTicks: false }
                                 && spanDuration <= maximum_normalised_distance
                                 && ticksPerSpan * spans <= MAX_TICKS;
            if (!generateTicks)
                tickDistance = 0;

            int ticks = 0;
            foreach (SliderEventDescriptor checkpoint in SliderEventGenerator.Generate(original.StartTime, spanDuration, 1,
                         tickDistance, spanDuration, spans, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (checkpoint.Type is SliderEventType.Head or SliderEventType.LegacyLastTick)
                    continue;
                // The preflight bound also protects reverse spans, whose upstream iterator
                // buffers their ticks before yielding. This final guard handles rounding at
                // the count boundary without ever moving or inventing a checkpoint.
                if (checkpoint.Type == SliderEventType.Tick && ticks++ >= MAX_TICKS)
                    continue;
                yield return checkpoint;
            }
        }
    }
}
