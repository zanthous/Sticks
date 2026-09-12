using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Expands the source's same-stick turns with a section-dependent parity preference.
    /// Original turn direction is retained while local rhythm and geometry vary the strength;
    /// 135 degrees is a starting preference, not a minimum separation or a snapped destination.
    /// </summary>
    internal static class SticksParityConversion
    {
        private const double default_separation = 135;
        private const double reset_beats = 2;
        private const double context_beats = 2;

        public static void Apply(IEnumerable<SticksHitObject> hitObjects, IBeatmap source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var histories = new Dictionary<StickSide, GestureHistory>();

            foreach (SticksHitObject hitObject in hitObjects.OrderBy(hitObject => hitObject.StartTime))
            {
                cancellationToken.ThrowIfCancellationRequested();

                double beatLength = source.ControlPointInfo.TimingPointAt(hitObject.StartTime).BeatLength;
                if (!double.IsFinite(beatLength) || beatLength <= 0)
                    beatLength = 500;

                hitObject.Angle = SticksHitObject.NormaliseAngle(hitObject.Angle);
                // Capture the unmodified endpoint before rotating this gesture. Subsequent
                // source turns must not be measured against our already-adjusted output.
                float sourceEndAngle = endAngle(hitObject);

                if (!histories.TryGetValue(hitObject.Side, out GestureHistory history)
                    || hitObject.StartTime - history.EndTime >= beatLength * reset_beats)
                {
                    // Repeated source directions can still deliberately alternate. Moving
                    // source patterns supply their own turn direction instead of a tie-break.
                    history = new GestureHistory { PreferredTurn = hitObject.Side == StickSide.Left ? 1 : -1 };
                    histories[hitObject.Side] = history;
                }
                else if (hitObject.StartTime >= history.EndTime && hitObject.StartTime > history.StartTime)
                {
                    float sourceTurn = SticksHitObject.DeltaAngle(history.SourceEndAngle, hitObject.Angle);
                    double magnitude = Math.Abs(sourceTurn);
                    double gapBeats = (hitObject.StartTime - history.EndTime) / beatLength;
                    double intervalBeats = (hitObject.StartTime - history.StartTime) / beatLength;
                    double weight = 1 - Math.Exp(-intervalBeats / context_beats);

                    // Initialize from the phrase itself, avoiding an artificial warm-up from
                    // the same seed values after every rest. Smooth in musical time so a tempo
                    // change or denser run can gradually change the character of the section.
                    history.MeanSourceTurn = history.MeanSourceTurn is double previousTurn
                        ? previousTurn + weight * (magnitude - previousTurn)
                        : magnitude;
                    history.MeanGapBeats = history.MeanGapBeats is double previousGap
                        ? previousGap + weight * (gapBeats - previousGap)
                        : gapBeats;

                    // Sparse passages and already-broad source movement need less added
                    // separation. These continuous ratios avoid prescribed angle buckets.
                    // Cadence uses free time after the previous endpoint: a long slider does
                    // not make an immediately following flick count as a slow passage.
                    double sourceMovement = history.MeanSourceTurn.Value / 180;
                    double cadence = history.MeanGapBeats.Value / context_beats;
                    double strength = default_separation / 180
                                      / ((1 + sourceMovement * sourceMovement) * (1 + cadence * cadence));
                    double convertedMagnitude = magnitude + strength * (180 - magnitude);

                    // Zero and straight reversals have no unique source turn direction.
                    // Do not let angle wrapping at +/-180 introduce a handedness bias.
                    int turn = magnitude > 0.001 && magnitude < 179.999 ? Math.Sign(sourceTurn) : history.PreferredTurn;
                    hitObject.Angle = SticksHitObject.NormaliseAngle((float)(history.EndAngle + turn * convertedMagnitude));
                    history.PreferredTurn = -turn;
                }

                double endTime = hitObject is IHasDuration duration ? hitObject.StartTime + duration.Duration : hitObject.StartTime;

                // Do not redirect overlapping gestures on one stick or let an intervening flick
                // erase a still-active slider/hold. The base converter owns occupancy decisions.
                if (endTime < history.EndTime)
                    continue;

                history.StartTime = hitObject.StartTime;
                history.EndTime = endTime;
                history.SourceEndAngle = sourceEndAngle;
                // Changing only the head rotates the complete path, preserving relative custom
                // segments, reversals and speed. Repeats can finish back at the head direction.
                history.EndAngle = endAngle(hitObject);
            }
        }

        private static float endAngle(SticksHitObject hitObject) => hitObject is SticksSlider slider
            ? SticksHitObject.NormaliseAngle(slider.AngleAt(slider.EndTime))
            : hitObject.Angle;

        private sealed class GestureHistory
        {
            public double StartTime = double.NegativeInfinity;
            public double EndTime = double.NegativeInfinity;
            public float EndAngle;
            public float SourceEndAngle;
            public int PreferredTurn;
            public double? MeanSourceTurn;
            public double? MeanGapBeats;
        }
    }
}
