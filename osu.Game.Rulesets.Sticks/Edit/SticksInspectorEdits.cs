#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Edit
{
    /// <summary>
    /// Only changed form fields are populated. Preparing an edit never changes the selected notes.
    /// </summary>
    internal sealed record SticksInspectorEdit
    {
        public StickSide? Side { get; init; }
        public double? Angle { get; init; }
        public double? SizeMultiplier { get; init; }
        public SticksSliceDirection? SliceDirection { get; init; }
        public double? StartTime { get; init; }
        public double? EndTime { get; init; }
        public double? Duration { get; init; }
        public IReadOnlyDictionary<int, double>? SegmentAngles { get; init; }
        public IReadOnlyDictionary<int, double>? SegmentDurations { get; init; }
    }

    /// <summary>
    /// A validated snapshot. The caller applies every plan inside one editor change transaction.
    /// </summary>
    internal sealed class SticksInspectorEditPlan
    {
        private readonly double? duration;
        private readonly float[]? segments;
        private readonly double[]? segmentDurations;
        private readonly double[]? durationWeights;
        private readonly bool replacePath;
        private readonly float untimedDistance;

        public SticksHitObject Target { get; }
        public StickSide Side { get; }
        public float Angle { get; }
        public float SizeMultiplier { get; }
        public SticksSliceDirection? SliceDirection { get; }
        public double StartTime { get; }
        public double EndTime => StartTime + (duration ?? 0);

        internal IReadOnlyList<float> SegmentAngles => segments ?? Array.Empty<float>();

        internal SticksInspectorEditPlan(SticksHitObject target, StickSide side, float angle, double startTime,
                                        double? duration, float[]? segments, double[]? segmentDurations, bool replacePath, float sizeMultiplier, SticksSliceDirection? sliceDirection)
        {
            Target = target;
            SizeMultiplier = sizeMultiplier;
            SliceDirection = sliceDirection;
            Side = side;
            Angle = angle;
            StartTime = startTime;
            this.duration = duration;
            this.segments = segments;
            this.segmentDurations = segmentDurations;
            this.replacePath = replacePath;
            if (replacePath)
            {
                durationWeights = segmentDurations!.ToArray();
                double total = durationWeights.Sum();
                if (Math.Abs(total - 1) > 1e-12)
                {
                    for (int i = 0; i < durationWeights.Length; i++)
                        durationWeights[i] /= total;
                }
            }
            else if (target is SticksSlider slider)
            {
                durationWeights = slider.SegmentDurationWeights?.ToArray();
                untimedDistance = slider.TotalAngularDistance;
            }
        }

        internal double SegmentDurationAt(int index)
        {
            if (durationWeights != null)
                return duration!.Value * durationWeights[index];

            return untimedDistance <= 0
                ? duration!.Value / segments!.Length
                : duration!.Value * Math.Abs(segments![index]) / untimedDistance;
        }

        public void Apply()
        {
            Target.SizeMultiplier = SizeMultiplier;
            if (Target is SticksSlice slice && SliceDirection.HasValue)
                slice.Direction = SliceDirection.Value;
            Target.StartTime = StartTime;
            if (Target.Side != Side)
                Target.Side = Side;
            if (Target.Angle != Angle)
                Target.Angle = Angle;

            if (Target is SticksSlider slider)
            {
                if (replacePath)
                    slider.SetTimedSegments(segments!, segmentDurations!);
                if (duration.HasValue && slider.Duration != duration.Value)
                    slider.Duration = duration.Value;
            }
        }
    }

    internal static class SticksInspectorEdits
    {
        private const double simultaneous_tolerance = 0.5;
        internal const int MAX_GENERATED_CHECKPOINTS = 4096;

        public static bool CanEditSegments(IReadOnlyList<SticksHitObject> selection)
        {
            if (selection.Count == 0 || selection[0] is not SticksSlider first)
                return false;

            return selection.All(note => note is SticksSlider slider
                                         && slider.SegmentArcAngles.SequenceEqual(first.SegmentArcAngles)
                                         && Enumerable.Range(0, first.SegmentCount).All(index =>
                                             Math.Abs(slider.SegmentDurationAt(index) - first.SegmentDurationAt(index)) <= 0.000001));
        }

        public static bool TryPrepare(IReadOnlyList<SticksHitObject> selection, SticksInspectorEdit edit, double? maxEndTime,
                                      out SticksInspectorEditPlan[] plans, out string error)
        {
            plans = Array.Empty<SticksInspectorEditPlan>();
            error = string.Empty;

            if (selection.Count == 0)
                return fail("Select at least one note.", out error);
            if (maxEndTime.HasValue && (!double.IsFinite(maxEndTime.Value) || maxEndTime.Value < 0))
                return fail("The audio length must be finite and nonnegative.", out error);
            if (edit.Side.HasValue && edit.Side != StickSide.Left && edit.Side != StickSide.Right)
                return fail("Choose the left or right stick.", out error);

            if (edit.SliceDirection.HasValue && (!Enum.IsDefined(edit.SliceDirection.Value) || selection.Any(note => note is not SticksSlice)))
                return fail("Direction requires a selection of Slice notes.", out error);
            if (edit.SizeMultiplier.HasValue && selection.Any(note => note is SticksClick or SticksSlice))
                return fail("Clicks and Slices have a fixed size.", out error);

            bool segmentAnglesChanged = edit.SegmentAngles?.Count > 0;
            bool segmentDurationsChanged = edit.SegmentDurations?.Count > 0;
            bool globalDurationChanged = edit.EndTime.HasValue || edit.Duration.HasValue;
            if (edit.EndTime.HasValue && edit.Duration.HasValue)
                return fail("Change either the end time or the duration, not both together.", out error);
            if (globalDurationChanged && segmentDurationsChanged)
                return fail("Change either the overall duration or individual segment durations in one edit.", out error);
            if ((globalDurationChanged || segmentAnglesChanged || segmentDurationsChanged) && selection.Any(note => note is not SticksSlider))
                return fail("Duration and path fields require a selection containing only sliders.", out error);
            if ((segmentAnglesChanged || segmentDurationsChanged) && !CanEditSegments(selection))
                return fail("Edit segment fields together only when the selected sliders have matching paths and segment durations.", out error);

            var prepared = new SticksInspectorEditPlan[selection.Count];
            for (int i = 0; i < selection.Count; i++)
            {
                SticksHitObject target = selection[i];
                StickSide side = edit.Side ?? target.Side;
                double startTime = edit.StartTime ?? target.StartTime;
                double angle = edit.Angle ?? target.Angle;
                double size = edit.SizeMultiplier ?? target.SizeMultiplier;
                double width = target.PrimaryHitAngle / target.SizeMultiplier * size;
                if (!double.IsFinite(size) || size <= 0 || size > float.MaxValue || !double.IsFinite(width) || width < 1 || width > 360)
                    return fail("Size must give an angular width between 1° and 360°.", out error);
                if (!double.IsFinite(startTime) || startTime < 0)
                    return fail("Start time must be finite and nonnegative.", out error);
                if (!tryAngle(angle, out float finalAngle))
                    return fail("Angle must be finite and representable.", out error);
                if (!renderableAngle(finalAngle))
                    return fail("The angle is too large to represent its rendered position.", out error);

                double? duration = target is IHasDuration sustained ? sustained.Duration : null;
                float[]? finalSegments = null;
                double[]? finalSegmentDurations = null;
                bool replacePath = false;
                if (target is SticksSlider slider)
                {
                    float[] segments = slider.SegmentArcAngles.ToArray();
                    double[] durations = Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentDurationAt).ToArray();
                    if (segments.Length is < 1 or > SticksSlider.MAX_SEGMENT_COUNT)
                        return fail($"Sliders require between 1 and {SticksSlider.MAX_SEGMENT_COUNT} segments.", out error);

                    if (edit.SegmentAngles != null)
                    {
                        foreach ((int index, double value) in edit.SegmentAngles)
                        {
                            if (index < 0 || index >= segments.Length)
                                return fail("A selected segment no longer exists.", out error);
                            if (!tryAngle(value, out float segment))
                                return fail($"Segment {index + 1}'s angle must be finite and representable.", out error);
                            segments[index] = segment;
                        }
                    }

                    if (edit.SegmentDurations != null)
                    {
                        foreach ((int index, double value) in edit.SegmentDurations)
                        {
                            if (index < 0 || index >= durations.Length)
                                return fail("A selected segment no longer exists.", out error);
                            durations[index] = value;
                        }
                    }

                    double oldDuration = slider.Duration;
                    if (segmentDurationsChanged)
                        duration = durations.Sum();
                    else if (edit.EndTime.HasValue)
                        duration = edit.EndTime.Value - startTime;
                    else if (edit.Duration.HasValue)
                        duration = edit.Duration.Value;

                    if (!duration.HasValue || !double.IsFinite(duration.Value) || duration.Value <= 0)
                        return fail("Slider duration must be positive and finite, with the end after the start.", out error);

                    if (globalDurationChanged)
                    {
                        if (!double.IsFinite(oldDuration) || oldDuration <= 0)
                            return fail("The current slider duration is invalid.", out error);
                        for (int segment = 0; segment < durations.Length; segment++)
                            durations[segment] = durations[segment] / oldDuration * duration.Value;
                    }

                    if (!validPath(finalAngle, startTime, duration.Value, segments, durations, out error))
                        return false;

                    // Legacy markers store only three decimal places. Use the existing precise
                    // timed representation when an explicit duration edit would otherwise change
                    // on save (or round a tiny, valid duration down to zero).
                    bool needsPreciseDuration = globalDurationChanged && !slider.HasTimedSegments
                                                && !legacyDurationPreserved(duration.Value);
                    replacePath = segmentAnglesChanged || segmentDurationsChanged || needsPreciseDuration;
                    finalSegments = segments;
                    finalSegmentDurations = durations;
                }

                double endTime = startTime + (duration ?? 0);
                if (!double.IsFinite(endTime) || endTime < startTime || duration.HasValue && (duration <= 0 || endTime == startTime))
                    return fail("The note's end time must be finite and after its start.", out error);
                if (maxEndTime.HasValue && endTime > maxEndTime.Value)
                    return fail("The note must end within the audio track.", out error);

                prepared[i] = new SticksInspectorEditPlan(target, side, finalAngle, startTime, duration, finalSegments, finalSegmentDurations, replacePath, (float)size, edit.SliceDirection);
            }

            if ((edit.Side.HasValue || edit.StartTime.HasValue) && introducesCollision(prepared))
                return fail("This edit would put two notes for the same stick at the same time.", out error);

            plans = prepared;
            return true;
        }

        public static bool ValidateCheckpointBudget(IReadOnlyList<SticksInspectorEditPlan> plans, ControlPointInfo controlPoints,
                                                    double tickRate, out string error)
        {
            error = string.Empty;
            if (!double.IsFinite(tickRate))
                return fail("The slider tick rate must be finite.", out error);
            tickRate = Math.Max(1, tickRate);

            foreach (SticksInspectorEditPlan plan in plans)
            {
                if (plan.Target is not SticksSlider)
                    continue;

                bool stationary = plan.SegmentAngles.All(arc => arc == 0);
                double checkpoints = 2 + (stationary ? 0 : plan.SegmentAngles.Count - 1);
                foreach (float arc in plan.SegmentAngles)
                {
                    // Count full-turn objects in double precision before allocating anything or
                    // reaching the generator's integer loop arithmetic.
                    checkpoints += Math.Max(0, Math.Ceiling((Math.Abs((double)arc) - 0.001) / 360) - 1);
                    if (checkpoints > MAX_GENERATED_CHECKPOINTS)
                        return checkpointBudgetExceeded(out error);
                }

                int remaining = MAX_GENERATED_CHECKPOINTS - (int)checkpoints;
                double segmentStart = plan.StartTime;
                int tickSpans = stationary ? 1 : plan.SegmentAngles.Count;
                for (int i = 0; i < tickSpans; i++)
                {
                    double segmentEnd = stationary ? plan.EndTime : segmentStart + plan.SegmentDurationAt(i);
                    int ticks = SticksTickGenerator.Generate(controlPoints, segmentStart, segmentEnd, tickRate, CancellationToken.None)
                                                  .Take(remaining + 1).Count();
                    if (ticks > remaining)
                        return checkpointBudgetExceeded(out error);
                    remaining -= ticks;
                    segmentStart = segmentEnd;
                }
            }

            return true;
        }

        private static bool checkpointBudgetExceeded(out string error) =>
            fail($"A slider would generate more than {MAX_GENERATED_CHECKPOINTS} checkpoints. Reduce its rotations or duration.", out error);

        private static bool introducesCollision(SticksInspectorEditPlan[] plans)
        {
            // Compare each timestamp window's original extremes instead of every pair. This also
            // handles thousands of existing stacked notes without a quadratic selection scan.
            foreach (var group in plans.GroupBy(plan => (plan.Side, Click: plan.Target is SticksClick)))
            {
                SticksInspectorEditPlan[] sorted = group.OrderBy(plan => plan.StartTime).ToArray();
                var originalTimes = new SortedSet<(double Time, int Index)>();
                var originalSides = new Dictionary<StickSide, int>();
                int first = 0;
                for (int i = 0; i < sorted.Length; i++)
                {
                    SticksInspectorEditPlan current = sorted[i];
                    while (first < i && current.StartTime - sorted[first].StartTime > simultaneous_tolerance)
                    {
                        SticksHitObject removed = sorted[first].Target;
                        originalTimes.Remove((removed.StartTime, first));
                        originalSides[removed.Side]--;
                        first++;
                    }

                    originalSides.TryGetValue(current.Target.Side, out int sameSideCount);
                    if (originalTimes.Count > 0
                        && (sameSideCount != originalTimes.Count
                            || current.Target.StartTime - originalTimes.Min.Time > simultaneous_tolerance
                            || originalTimes.Max.Time - current.Target.StartTime > simultaneous_tolerance))
                        return true;

                    originalTimes.Add((current.Target.StartTime, i));
                    originalSides[current.Target.Side] = sameSideCount + 1;
                }
            }

            return false;
        }

        private static bool validPath(float startAngle, double startTime, double duration, float[] arcs, double[] durations, out string error)
        {
            error = string.Empty;
            double distance = arcs.Sum(value => Math.Abs((double)value));
            if (arcs.Any(value => !float.IsFinite(value)) || !double.IsFinite(distance) || distance > float.MaxValue)
                return fail("The slider's total angular distance must be finite and representable.", out error);

            float angle = startAngle;
            float accumulatedDistance = 0;
            double time = startTime;
            for (int i = 0; i < arcs.Length; i++)
            {
                angle += arcs[i];
                accumulatedDistance += Math.Abs(arcs[i]);
                if (!float.IsFinite(accumulatedDistance))
                    return fail("The slider's angular distance cannot be represented by its renderer.", out error);
                if (!float.IsFinite(angle) || !renderableAngle(angle))
                    return fail($"Segment {i + 1}'s endpoint angle cannot be represented.", out error);

                double span = durations[i];
                double weight = span / duration;
                double nextTime = time + span;
                if (!double.IsFinite(span) || span <= 0 || !double.IsFinite(weight) || weight <= 0
                    || weight * duration <= 0 || !double.IsFinite(nextTime) || nextTime <= time)
                    return fail($"Segment {i + 1} must have a positive, finite duration and a distinct end time.", out error);

                if (Math.Abs(arcs[i]) > 360)
                {
                    // Full-turn checkpoints use this arithmetic in SticksSlider. Their interval
                    // must remain finite and distinguishable from the surrounding timestamps.
                    double loopDuration = span * 360 / Math.Abs(arcs[i]);
                    if (!double.IsFinite(loopDuration) || loopDuration <= 0
                        || time + loopDuration <= time || nextTime - loopDuration >= nextTime)
                        return fail($"Segment {i + 1}'s full-turn checkpoint times cannot be represented.", out error);
                }
                time = nextTime;
            }

            return true;
        }

        private static bool tryAngle(double value, out float angle)
        {
            angle = (float)value;
            return double.IsFinite(value) && float.IsFinite(angle) && (value == 0 || angle != 0);
        }

        private static bool renderableAngle(float angle) => float.IsFinite(angle * MathF.PI);

        private static bool legacyDurationPreserved(double duration) =>
            double.TryParse(duration.ToString("0.###", CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double decoded) && decoded == duration;

        private static bool fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
