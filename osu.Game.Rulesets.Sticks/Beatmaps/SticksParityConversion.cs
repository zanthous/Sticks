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

        public static void Apply(IEnumerable<SticksHitObject> hitObjects, IBeatmap source, CancellationToken cancellationToken,
                                 bool readableChords = false, float? fullHitAngle = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var histories = new Dictionary<StickSide, GestureHistory>();
            SticksHitObject[] ordered = hitObjects.OrderBy(hitObject => hitObject.StartTime).ToArray();
            float width = fullHitAngle ?? SticksHitObject.HitAngleForCircleSize(
                float.IsFinite(source.Difficulty.CircleSize) ? source.Difficulty.CircleSize : SticksHitObject.DEFAULT_CIRCLE_SIZE);

            for (int first = 0; first < ordered.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int end = first + 1;
                while (end < ordered.Length && ordered[end].StartTime - ordered[first].StartTime < SticksChordGeometry.TIME_TOLERANCE)
                    end++;

                if (end - first == 2 && SticksChordGeometry.IsDirectionalPair(ordered[first], ordered[first + 1]))
                {
                    SticksHitObject a = ordered[first];
                    SticksHitObject b = ordered[first + 1];
                    float sourceA = a.Angle;
                    float sourceB = b.Angle;
                    bool keepSharedTarget = Math.Abs(SticksHitObject.DeltaAngle(sourceA, sourceB)) < SticksChordGeometry.ANGLE_TOLERANCE
                                            || readableChords && SticksChordGeometry.ShouldStack(sourceA, width, sourceB, width);
                    PendingGesture pendingA = prepare(a, source, histories);
                    PendingGesture pendingB = prepare(b, source, histories);
                    bool resolve = keepSharedTarget || readableChords && SticksChordGeometry.ShouldStack(a.Angle, width, b.Angle, width);
                    if (resolve)
                    {
                        float sourceHeading = SticksChordGeometry.SharedAngle(sourceA, sourceB, sourceA);
                        float shared = SticksChordGeometry.SharedAngle(a.Angle, b.Angle, sourceHeading);
                        a.Angle = shared;
                        b.Angle = shared;
                    }
                    commit(pendingA, resolve);
                    commit(pendingB, resolve);
                }
                else
                {
                    // Retain sequential history handling for ordinary objects and
                    // malformed same-hand groups; only true pairs share a decision.
                    for (int i = first; i < end; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        commit(prepare(ordered[i], source, histories), false);
                    }
                }
                first = end;
            }
        }

        private static PendingGesture prepare(SticksHitObject hitObject, IBeatmap source, Dictionary<StickSide, GestureHistory> histories)
        {
            double beatLength = source.ControlPointInfo.TimingPointAt(hitObject.StartTime).BeatLength;
            if (!double.IsFinite(beatLength) || beatLength <= 0)
                beatLength = 500;

            hitObject.Angle = SticksHitObject.NormaliseAngle(hitObject.Angle);
            // Capture the unmodified endpoint before rotating this gesture. Subsequent
            // source turns must not be measured against our already-adjusted output.
            float sourceEndAngle = endAngle(hitObject);
            float? previousEndAngle = null;

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
                previousEndAngle = history.EndAngle;
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

            return new PendingGesture(hitObject, history, sourceEndAngle, endTime, previousEndAngle);
        }

        private static void commit(PendingGesture pending, bool resolvedPair)
        {
            SticksHitObject hitObject = pending.HitObject;
            GestureHistory history = pending.History;

            // Do not redirect overlapping gestures on one stick or let an intervening flick
            // erase a still-active slider/hold. The base converter owns occupancy decisions.
            if (pending.EndTime < history.EndTime)
                return;

            if (resolvedPair && pending.PreviousEndAngle is float previous)
            {
                float actualTurn = SticksHitObject.DeltaAngle(previous, hitObject.Angle);
                if (Math.Abs(actualTurn) > 0.001 && Math.Abs(actualTurn) < 179.999)
                    history.PreferredTurn = -Math.Sign(actualTurn);
            }

            history.StartTime = hitObject.StartTime;
            history.EndTime = pending.EndTime;
            history.SourceEndAngle = pending.SourceEndAngle;
            // Changing only the head rotates the complete path, preserving relative custom
            // segments, reversals and speed. Repeats can finish back at the head direction.
            history.EndAngle = endAngle(hitObject);
        }

        private readonly record struct PendingGesture(SticksHitObject HitObject, GestureHistory History, float SourceEndAngle,
                                                       double EndTime, float? PreviousEndAngle);

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
