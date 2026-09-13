using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using Newtonsoft.Json;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    public class SticksSlider : SticksHitObject, IHasDuration
    {
        /// <summary>
        /// Maximum number of authored path segments. The editor deliberately exposes at most this
        /// many, and enforcing the same limit on decoded/clipboard data prevents malformed maps
        /// from allocating an unbounded number of reversal drawables and path vertices.
        /// </summary>
        public const int MAX_SEGMENT_COUNT = 16;

        private double duration;
        private ControlPointInfo controlPointInfo = null!;
        private double tickRate;

        public double Duration
        {
            get => duration;
            set
            {
                duration = value;
                RefreshLegacyEditorMarker();
            }
        }

        public double EndTime => StartTime + Duration;

        private int repeatCount;
        private List<float> customSegmentArcAngles;
        private List<double> segmentDurationWeights;
        private bool deserialising;
        private List<float> pendingSerialisedSegments;
        private List<double> pendingSerialisedDurationWeights;

        [JsonProperty("segments", Order = 100, NullValueHandling = NullValueHandling.Ignore)]
        public List<float> SerialisedSegments
        {
            get => customSegmentArcAngles?.ToList();
            set
            {
                if (deserialising)
                    pendingSerialisedSegments = value;
                else if (value != null)
                    SetCustomSegments(value);
            }
        }

        [JsonProperty("segmentDurationWeights", Order = 101, NullValueHandling = NullValueHandling.Ignore)]
        public List<double> SerialisedSegmentDurationWeights
        {
            get => segmentDurationWeights?.ToList();
            set
            {
                if (deserialising)
                    pendingSerialisedDurationWeights = value;
                else if (value != null)
                    SetTimedSegments(SegmentArcAngles, value);
            }
        }

        [OnDeserializing]
        private void beginDeserialising(StreamingContext context)
        {
            deserialising = true;
            pendingSerialisedSegments = null;
            pendingSerialisedDurationWeights = null;
        }

        [OnDeserialized]
        private void finishDeserialising(StreamingContext context)
        {
            deserialising = false;

            // Untimed paths discard zero-duration stationary pieces mixed into a moving path.
            // Defer both fields so timed dwells survive either JSON property order.
            if (pendingSerialisedDurationWeights != null)
                SetTimedSegments(pendingSerialisedSegments ?? throw new JsonSerializationException("Timed slider segments are missing."), pendingSerialisedDurationWeights);
            else if (pendingSerialisedSegments != null)
                SetCustomSegments(pendingSerialisedSegments);

            pendingSerialisedSegments = null;
            pendingSerialisedDurationWeights = null;
        }

        public int RepeatCount
        {
            get => repeatCount;
            set
            {
                customSegmentArcAngles = null;
                segmentDurationWeights = null;
                repeatCount = Math.Clamp(value, 0, MAX_SEGMENT_COUNT - 1);
                RefreshLegacyEditorMarker();
            }
        }

        public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();

        public int SpanCount => SegmentCount;

        public double SpanDuration => Duration / Math.Max(1, SpanCount);

        private float arcAngle;

        public float ArcAngle
        {
            get => arcAngle;
            set
            {
                customSegmentArcAngles = null;
                segmentDurationWeights = null;
                arcAngle = value;
                RefreshLegacyEditorMarker();
            }
        }

        public int SegmentCount => customSegmentArcAngles?.Count ?? RepeatCount + 1;

        public bool HasCustomSegments => customSegmentArcAngles != null;

        [JsonIgnore]
        public bool HasTimedSegments => segmentDurationWeights != null;

        /// <summary>
        /// Fractions of <see cref="Duration"/> assigned to each segment, or null for a legacy
        /// constant-speed path. Changing the overall duration preserves these relative timings.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<double> SegmentDurationWeights => segmentDurationWeights;

        public IReadOnlyList<float> SegmentArcAngles => customSegmentArcAngles ?? createLegacySegments();

        public float TotalAngularDistance
        {
            get
            {
                if (customSegmentArcAngles == null)
                    return Math.Abs(ArcAngle) * SegmentCount;

                float total = 0;

                for (int i = 0; i < customSegmentArcAngles.Count; i++)
                    total += Math.Abs(customSegmentArcAngles[i]);

                return total;
            }
        }

        [JsonIgnore]
        public bool IsStationary => TotalAngularDistance == 0;

        public int InitialDirection
        {
            get
            {
                if (HasTimedSegments)
                {
                    for (int i = 0; i < SegmentCount; i++)
                    {
                        int direction = Math.Sign(SegmentArcAngleAt(i));
                        if (direction != 0)
                            return direction;
                    }
                }

                return Math.Sign(SegmentArcAngleAt(0));
            }
        }

        public double TickInterval { get; private set; }

        internal float BeatPulseAt(double time)
        {
            if (controlPointInfo == null)
                return 0;

            TimingControlPoint timingPoint = controlPointInfo.TimingPointAt(time);
            double beatLength = timingPoint.BeatLength;

            if (!double.IsFinite(beatLength) || beatLength <= 0)
                return 0;

            double phase = (time - timingPoint.Time) / beatLength;
            phase -= Math.Floor(phase);

            // Peak on each beat, smoothly falling and rising between adjacent beats.
            return 0.5f + 0.5f * (float)Math.Cos(phase * Math.PI * 2);
        }

        public float SegmentArcAngleAt(int index)
        {
            index = Math.Clamp(index, 0, SegmentCount - 1);
            return customSegmentArcAngles?[index] ?? (index % 2 == 0 ? ArcAngle : -ArcAngle);
        }

        public float SegmentStartAngleAt(int index)
        {
            float result = Angle;
            for (int i = 0; i < Math.Clamp(index, 0, SegmentCount); i++)
                result += SegmentArcAngleAt(i);
            return result;
        }

        public double SegmentDurationAt(int index)
        {
            if (HasTimedSegments)
                return Duration * segmentDurationWeights[Math.Clamp(index, 0, SegmentCount - 1)];

            float totalDistance = TotalAngularDistance;
            return totalDistance <= 0
                ? Duration / Math.Max(1, SegmentCount)
                : Duration * Math.Abs(SegmentArcAngleAt(index)) / totalDistance;
        }

        public double SegmentStartTimeAt(int index)
        {
            double result = StartTime;
            for (int i = 0; i < Math.Clamp(index, 0, SegmentCount); i++)
                result += SegmentDurationAt(i);
            return result;
        }

        public double SegmentEndTimeAt(int index) => SegmentStartTimeAt(index) + SegmentDurationAt(index);

        public float AngleAt(double time)
        {
            int segmentIndex = SegmentIndexAt(time);
            return SegmentStartAngleAt(segmentIndex) + SegmentArcAngleAt(segmentIndex) * (float)SegmentProgressAt(time);
        }

        /// <summary>
        /// Samples this slider's path at the requested timestamps. Legacy constant-speed paths
        /// use a distance walk; timed paths retain their independent segment speeds and dwells.
        /// </summary>
        internal void FillAngleSamples(double startTime, double endTime, Span<float> destination)
        {
            if (destination.Length == 0)
                return;

            if (HasTimedSegments)
            {
                for (int i = 0; i < destination.Length; i++)
                {
                    double sampleTime = destination.Length == 1
                        ? startTime
                        : startTime + (endTime - startTime) * i / (destination.Length - 1);
                    destination[i] = AngleAt(sampleTime);
                }

                return;
            }

            int segmentCount = SegmentCount;
            float totalDistance = TotalAngularDistance;

            if (segmentCount <= 0 || totalDistance <= 0 || Duration <= 0)
            {
                destination.Fill(Angle);
                return;
            }

            int segmentIndex = 0;
            float segmentStartAngle = Angle;
            float distanceBeforeSegment = 0;
            float segmentArc = SegmentArcAngleAt(segmentIndex);
            float segmentDistance = Math.Abs(segmentArc);

            for (int i = 0; i < destination.Length; i++)
            {
                double sampleTime = destination.Length == 1
                    ? startTime
                    : startTime + (endTime - startTime) * i / (destination.Length - 1);
                float travelledDistance = totalDistance * (float)Math.Clamp((sampleTime - StartTime) / Duration, 0, 1);

                while (segmentIndex < segmentCount - 1
                       && travelledDistance >= distanceBeforeSegment + segmentDistance)
                {
                    distanceBeforeSegment += segmentDistance;
                    segmentStartAngle += segmentArc;
                    segmentArc = SegmentArcAngleAt(++segmentIndex);
                    segmentDistance = Math.Abs(segmentArc);
                }

                float segmentProgress = segmentDistance <= 0
                    ? 1
                    : Math.Clamp((travelledDistance - distanceBeforeSegment) / segmentDistance, 0, 1);
                destination[i] = segmentStartAngle + segmentArc * segmentProgress;
            }
        }

        public int SegmentIndexAt(double time)
        {
            double clampedTime = Math.Clamp(time, StartTime, EndTime);
            for (int i = 0; i < SegmentCount - 1; i++)
            {
                if (clampedTime < SegmentEndTimeAt(i))
                    return i;
            }

            return SegmentCount - 1;
        }

        public int SpanIndexAt(double time) => SegmentIndexAt(time);

        public double SegmentProgressAt(double time)
        {
            int segmentIndex = SegmentIndexAt(time);
            double segmentStart = SegmentStartTimeAt(segmentIndex);
            double segmentDuration = SegmentDurationAt(segmentIndex);
            double progressDuration = HasTimedSegments && segmentDuration > 0 ? segmentDuration : Math.Max(1, segmentDuration);
            return Math.Clamp((time - segmentStart) / progressDuration, 0, 1);
        }

        public double SpanProgressAt(double time) => SegmentProgressAt(time);

        public double PathProgressAt(double time) => SegmentProgressAt(time);

        public bool SegmentEndsWithReversal(int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= SegmentCount - 1)
                return false;

            if (!HasTimedSegments && !IsStationary)
                return true;

            int nextDirection = Math.Sign(SegmentArcAngleAt(segmentIndex + 1));
            if (nextDirection == 0)
                return false;

            // A dwell retains the incoming direction. Only its eventual exit into the opposite
            // direction is a reversal; entering a dwell or changing speed needs no reversal cue.
            for (int previous = segmentIndex; previous >= 0; previous--)
            {
                int previousDirection = Math.Sign(SegmentArcAngleAt(previous));
                if (previousDirection != 0)
                    return previousDirection != nextDirection;
            }

            return false;
        }

        public bool CurrentSpanEndsWithReversal(double time) => SegmentEndsWithReversal(SegmentIndexAt(time));

        public (double Start, double End) RemainingPathRangeAt(double time) => (SegmentProgressAt(time), 1);

        public double RehearsalStartTime => Math.Max(StartTime - ApproachDuration, StartTime - SegmentDurationAt(0));

        public double RehearsalProgressAt(double time) =>
            Math.Clamp((time - RehearsalStartTime) / Math.Max(1, SegmentDurationAt(0)), 0, 1);

        /// <summary>
        /// Returns the segment which begins at the next reversal, or -1 when no reversal remains.
        /// Only the immediately upcoming segment is previewed to keep chained reversals readable.
        /// </summary>
        public int UpcomingSegmentIndexAt(double time)
        {
            if (time < StartTime || time >= EndTime)
                return -1;

            int upcoming = SegmentIndexAt(time) + 1;

            if (HasTimedSegments || IsStationary)
            {
                while (upcoming < SegmentCount && !SegmentEndsWithReversal(upcoming - 1))
                    upcoming++;
            }

            return upcoming < SegmentCount ? upcoming : -1;
        }

        public bool UpcomingSegmentEndsWithReversalAt(double time) => SegmentEndsWithReversal(UpcomingSegmentIndexAt(time));

        /// <summary>
        /// Returns the immediately following path segment, including a timed speed change or
        /// the movement after a dwell. Path visibility is independent of reversal judgements.
        /// </summary>
        public int UpcomingPathSegmentIndexAt(double time)
        {
            if (time < StartTime || time >= EndTime)
                return -1;

            int upcoming = SegmentIndexAt(time) + 1;
            return upcoming < SegmentCount ? upcoming : -1;
        }

        public double UpcomingPathSegmentPreviewProgressAt(double time) =>
            upcomingSegmentPreviewProgressAt(time, UpcomingPathSegmentIndexAt(time));

        /// <summary>
        /// Returns the snaking progress for the segment after the current reversal. Its cue uses
        /// the same approach duration and accelerated growth as a note head. When a span is shorter
        /// than the approach duration, previewing starts at that span's beginning rather than showing
        /// multiple future reversals simultaneously.
        /// </summary>
        public double UpcomingSegmentPreviewProgressAt(double time)
            => upcomingSegmentPreviewProgressAt(time, UpcomingSegmentIndexAt(time));

        private double upcomingSegmentPreviewProgressAt(double time, int upcoming)
        {
            if (upcoming < 1)
                return 0;

            int current = upcoming - 1;
            double boundaryTime = SegmentStartTimeAt(upcoming);
            double previewStart = Math.Max(SegmentStartTimeAt(current), boundaryTime - ApproachDuration);
            double linearProgress = Math.Clamp((time - previewStart) / Math.Max(1, boundaryTime - previewStart), 0, 1);
            return ApproachGrowthProgress(linearProgress);
        }

        public double AvailableTrackingDuration(double headHitTime) =>
            Math.Max(1, EndTime - Math.Max(StartTime, headHitTime));

        public void SetCustomSegments(IEnumerable<float> segments)
        {
            ArgumentNullException.ThrowIfNull(segments);
            List<float> values = segments.Where(float.IsFinite).Take(MAX_SEGMENT_COUNT).ToList();
            // Without independent timing, a zero piece inside a moving path has no duration.
            // Entirely stationary paths instead divide their duration across the stored pieces.
            if (values.Any(segment => segment != 0))
                values.RemoveAll(segment => segment == 0);
            if (values.Count == 0)
                throw new ArgumentException("A slider requires at least one finite segment.", nameof(segments));

            customSegmentArcAngles = values;
            segmentDurationWeights = null;
            arcAngle = values[0];
            repeatCount = values.Count - 1;
            RefreshLegacyEditorMarker();
        }

        /// <summary>
        /// Sets a piecewise angular path with independent timing at every anchor. Arcs may be
        /// stationary or smaller than a degree; durations must be positive and finite. Durations
        /// are stored as fractions, leaving the object's overall <see cref="Duration"/> intact.
        /// </summary>
        public void SetTimedSegments(IEnumerable<float> arcs, IEnumerable<double> segmentDurations)
        {
            ArgumentNullException.ThrowIfNull(arcs);
            ArgumentNullException.ThrowIfNull(segmentDurations);

            List<float> values = arcs.Take(MAX_SEGMENT_COUNT + 1).ToList();
            List<double> durations = segmentDurations.Take(MAX_SEGMENT_COUNT + 1).ToList();

            if (values.Count is < 1 or > MAX_SEGMENT_COUNT || durations.Count != values.Count)
                throw new ArgumentException($"A timed slider requires 1 to {MAX_SEGMENT_COUNT} matching arcs and durations.");

            double distance = values.Sum(value => (double)Math.Abs(value));
            if (values.Any(value => !float.IsFinite(value)) || !double.IsFinite(distance) || distance > float.MaxValue)
                throw new ArgumentException("Timed slider arcs must be finite.", nameof(arcs));

            double totalDuration = durations.Sum();
            if (durations.Any(value => !double.IsFinite(value) || value <= 0) || !double.IsFinite(totalDuration) || totalDuration <= 0)
                throw new ArgumentException("Timed slider durations must be positive and finite.", nameof(segmentDurations));

            // Avoid progressively renormalising the same fractions on repeated clipboard/codec
            // round trips. Their sum can differ from one by ordinary floating-point rounding.
            if (Math.Abs(totalDuration - 1) > 1e-12)
            {
                for (int i = 0; i < durations.Count; i++)
                    durations[i] /= totalDuration;
            }

            if (durations.Any(value => value <= 0))
                throw new ArgumentException("Timed slider duration fractions must be representable.", nameof(segmentDurations));

            customSegmentArcAngles = values;
            segmentDurationWeights = durations;
            arcAngle = values[0];
            repeatCount = values.Count - 1;
            RefreshLegacyEditorMarker();
        }

        public void ReplaceFinalSegment(float segmentArcAngle)
        {
            List<float> segments = SegmentArcAngles.ToList();
            segments[^1] = segmentArcAngle;
            if (HasTimedSegments)
                SetTimedSegments(segments, segmentDurationWeights);
            else
                SetCustomSegments(segments);
        }

        public void AppendSegmentAtConstantSpeed(float segmentArcAngle)
        {
            if (IsStationary)
                throw new InvalidOperationException("A stationary slider must be extended using a new end time.");

            if (HasTimedSegments)
            {
                if (SegmentCount >= MAX_SEGMENT_COUNT)
                    throw new InvalidOperationException("The slider already has the maximum number of segments.");

                double addedTimedDuration = Math.Abs(segmentArcAngle) / finalMovingSpeed();
                if (!float.IsFinite(segmentArcAngle) || !double.IsFinite(addedTimedDuration) || addedTimedDuration <= 0)
                    throw new ArgumentException("The segment must produce positive finite movement and duration.", nameof(segmentArcAngle));

                appendTimedSegment(segmentArcAngle, addedTimedDuration);
                return;
            }

            float totalDistance = TotalAngularDistance;
            double degreesPerMillisecond = totalDistance / Duration;
            List<float> segments = SegmentArcAngles.ToList();
            segments.Add(segmentArcAngle);
            double addedDuration = Math.Abs(segmentArcAngle) / degreesPerMillisecond;
            if (!float.IsFinite(segmentArcAngle) || !double.IsFinite(addedDuration) || addedDuration <= 0)
                throw new ArgumentException("The segment must produce positive finite movement and duration.", nameof(segmentArcAngle));
            SetCustomSegments(segments);
            Duration += addedDuration;
        }

        /// <summary>
        /// Appends an authored arc ending at the selected time, retaining every existing segment's
        /// timing and node samples. A zero arc adds a stationary span. Invalid requests leave the
        /// slider unchanged.
        /// </summary>
        public bool AppendTimedSegment(float segmentArcAngle, double newEndTime)
        {
            double addedDuration = newEndTime - EndTime;
            double newDuration = Duration + addedDuration;
            double totalDistance = SegmentArcAngles.Sum(arc => (double)Math.Abs(arc)) + Math.Abs((double)segmentArcAngle);
            if (SegmentCount >= MAX_SEGMENT_COUNT || !float.IsFinite(segmentArcAngle)
                || !double.IsFinite(StartTime) || !double.IsFinite(Duration) || Duration <= 0
                || !double.IsFinite(newEndTime) || !double.IsFinite(addedDuration) || addedDuration <= 0
                || !double.IsFinite(newDuration) || newDuration <= Duration
                || !double.IsFinite(totalDistance) || totalDistance > float.MaxValue)
                return false;

            appendTimedSegment(segmentArcAngle, addedDuration);
            return true;
        }

        /// <summary>
        /// Calculates the reversed segment required to extend this slider to <paramref name="newEndTime"/>
        /// without changing its angular speed or the timing of any existing segment.
        /// </summary>
        public float ContinuationArcAt(double newEndTime)
        {
            double addedDuration = newEndTime - EndTime;
            if (!double.IsFinite(addedDuration) || addedDuration <= 0 || Duration <= 0 || TotalAngularDistance <= 0)
                return 0;

            double degreesPerMillisecond = HasTimedSegments ? finalMovingSpeed() : TotalAngularDistance / Duration;
            int finalMovingSegment = SegmentCount - 1;
            while (HasTimedSegments && finalMovingSegment > 0 && SegmentArcAngleAt(finalMovingSegment) == 0)
                finalMovingSegment--;

            int direction = Math.Sign(SegmentArcAngleAt(finalMovingSegment)) >= 0 ? -1 : 1;
            double arc = direction * degreesPerMillisecond * addedDuration;
            return double.IsFinite(arc) && Math.Abs(arc) <= float.MaxValue ? (float)arc : 0;
        }

        /// <summary>
        /// Appends the timed continuation returned by <see cref="ContinuationArcAt"/>. No state is
        /// changed when the requested end time cannot produce a valid segment.
        /// </summary>
        public bool AppendTimedSegmentAtConstantSpeed(double newEndTime)
        {
            if (IsStationary)
            {
                double newDuration = newEndTime - StartTime;
                if (!double.IsFinite(newDuration) || !double.IsFinite(newEndTime - EndTime) || newEndTime <= EndTime || Duration <= 0)
                    return false;

                // No turn or extra checkpoint is needed to keep holding the same direction.
                Duration = newDuration;
                return true;
            }

            float segmentArcAngle = ContinuationArcAt(newEndTime);
            if (HasTimedSegments)
            {
                if (SegmentCount >= MAX_SEGMENT_COUNT || segmentArcAngle == 0)
                    return false;

                appendTimedSegment(segmentArcAngle, newEndTime - EndTime);
                return true;
            }

            if (segmentArcAngle == 0 || SegmentCount >= MAX_SEGMENT_COUNT)
                return false;

            List<float> segments = SegmentArcAngles.ToList();
            segments.Add(segmentArcAngle);
            SetCustomSegments(segments);
            Duration = newEndTime - StartTime;
            return true;
        }

        public bool RemoveFinalSegmentAtConstantSpeed()
        {
            if (SegmentCount <= 1)
                return false;

            if (HasTimedSegments)
            {
                List<float> remainingArcs = SegmentArcAngles.Take(SegmentCount - 1).ToList();
                List<double> remainingDurations = Enumerable.Range(0, SegmentCount - 1).Select(SegmentDurationAt).ToList();
                SetTimedSegments(remainingArcs, remainingDurations);
                Duration = remainingDurations.Sum();
                return true;
            }

            double removedDuration = SegmentDurationAt(SegmentCount - 1);
            List<float> segments = SegmentArcAngles.ToList();
            segments.RemoveAt(segments.Count - 1);
            double reducedDuration = Math.Max(1, Duration - removedDuration);
            SetCustomSegments(segments);
            Duration = reducedDuration;
            return true;
        }

        private double finalMovingSpeed()
        {
            for (int i = SegmentCount - 1; i >= 0; i--)
            {
                float distance = Math.Abs(SegmentArcAngleAt(i));
                double segmentDuration = SegmentDurationAt(i);
                if (distance > 0 && segmentDuration > 0)
                    return distance / segmentDuration;
            }

            return 0;
        }

        private void appendTimedSegment(float arc, double addedDuration)
        {
            List<float> arcs = SegmentArcAngles.ToList();
            List<double> durations = Enumerable.Range(0, SegmentCount).Select(SegmentDurationAt).ToList();
            arcs.Add(arc);
            durations.Add(addedDuration);
            double newDuration = Duration + addedDuration;
            SetTimedSegments(arcs, durations);
            Duration = newDuration;
        }

        private List<float> createLegacySegments()
        {
            var result = new List<float>(RepeatCount + 1);
            for (int i = 0; i <= RepeatCount; i++)
                result.Add(i % 2 == 0 ? ArcAngle : -ArcAngle);
            return result;
        }

        protected override void ApplyDefaultsToSelf(ControlPointInfo controlPointInfo, IBeatmapDifficultyInfo difficulty)
        {
            base.ApplyDefaultsToSelf(controlPointInfo, difficulty);

            double beatLength = controlPointInfo.TimingPointAt(StartTime).BeatLength;
            tickRate = Math.Max(1, difficulty.SliderTickRate);
            TickInterval = beatLength / tickRate;
            this.controlPointInfo = controlPointInfo;
        }

        protected override void CreateNestedHitObjects(CancellationToken cancellationToken)
        {
            base.CreateNestedHitObjects(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            AddNested(new SticksSliderHead
            {
                StartTime = StartTime,
                Side = Side,
                Angle = Angle,
            });

            if (double.IsFinite(TickInterval) && TickInterval > 0)
            {
                HitSampleInfo sourceSample = Samples.FirstOrDefault(sample => sample.Name == HitSampleInfo.HIT_NORMAL) ?? Samples.FirstOrDefault();
                HitSampleInfo tickSample = sourceSample == null || SticksAuthoredBeatmapCodec.IsMarker(sourceSample)
                    ? new HitSampleInfo("slidertick", volume: sourceSample?.Volume ?? 100)
                    : sourceSample.With("slidertick");

                // Stationary pieces are one continuous sustain, so their bookkeeping boundaries
                // must not restart tick phase or add arbitrary scoring checkpoints.
                int tickSpanCount = IsStationary ? 1 : SegmentCount;
                for (int segment = 0; segment < tickSpanCount; segment++)
                {
                    double segmentStartTime = SegmentStartTimeAt(segment);
                    double segmentDuration = IsStationary ? Duration : SegmentDurationAt(segment);

                    foreach (double tickTime in SticksTickGenerator.Generate(
                                 controlPointInfo,
                                 segmentStartTime,
                                 segmentStartTime + segmentDuration,
                                 tickRate,
                                 cancellationToken))
                    {
                        AddNested(new SticksSliderTick
                        {
                            StartTime = tickTime,
                            SliderStartTime = StartTime,
                            IsStationary = IsStationary,
                            Side = Side,
                            Angle = AngleAt(tickTime),
                            Samples = new[] { tickSample },
                        });
                    }
                }
            }

            for (int reversalIndex = 0; !IsStationary && reversalIndex < SegmentCount - 1; reversalIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double reversalTime = SegmentEndTimeAt(reversalIndex);

                if (HasTimedSegments && !SegmentEndsWithReversal(reversalIndex))
                {
                    AddNested(new SticksSliderTick
                    {
                        StartTime = reversalTime,
                        SliderStartTime = StartTime,
                        Side = Side,
                        Angle = AngleAt(reversalTime),
                        Samples = samplesAtNode(reversalIndex + 1),
                    });
                    continue;
                }

                AddNested(new SticksSliderRepeat
                {
                    StartTime = reversalTime,
                    SliderStartTime = StartTime,
                    SpanDuration = SegmentDurationAt(reversalIndex),
                    RepeatIndex = reversalIndex,
                    DirectionAfter = Math.Sign(SegmentArcAngleAt(reversalIndex + 1)),
                    Side = Side,
                    Angle = AngleAt(reversalTime),
                    Samples = samplesAtNode(reversalIndex + 1),
                });
            }

            for (int segment = 0; segment < SegmentCount; segment++)
            {
                float segmentArc = SegmentArcAngleAt(segment);
                double absoluteArc = Math.Abs(segmentArc);
                if (absoluteArc <= 360)
                    continue;

                double segmentStart = SegmentStartTimeAt(segment);
                double segmentDuration = SegmentDurationAt(segment);
                double loopDuration = segmentDuration * 360 / absoluteArc;

                for (int loop = 1; loop * 360 < absoluteArc - 0.001; loop++)
                {
                    double extensionTime = segmentStart + loop * loopDuration;
                    AddNested(new SticksSliderExtension
                    {
                        StartTime = extensionTime,
                        SliderStartTime = StartTime,
                        LoopDuration = loopDuration,
                        LoopIndex = loop,
                        Direction = Math.Sign(segmentArc),
                        Side = Side,
                        Angle = AngleAt(extensionTime),
                        Samples = new[] { new HitSampleInfo("slidertick") },
                    });
                }
            }

            AddNested(new SticksSliderTail
            {
                StartTime = EndTime,
                SliderStartTime = StartTime,
                IsStationary = IsStationary,
                Side = Side,
                Angle = AngleAt(EndTime),
                Samples = samplesAtNode(SegmentCount),
            });
        }

        public override Judgement CreateJudgement() => new SticksIgnoreJudgement();

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;

        private IList<HitSampleInfo> samplesAtNode(int nodeIndex)
        {
            // Sticks segments are not osu!-style repeats. In particular, custom segments may
            // have different durations. Advertising IHasRepeats makes lazer's timeline end
            // handle edit RepeatCount rather than Duration, and also places equally-spaced node
            // markers at incorrect times. Keep per-node samples as ruleset-owned data instead.
            IList<HitSampleInfo> nodeSamples = nodeIndex < NodeSamples.Count ? NodeSamples[nodeIndex] : Samples;
            if (nodeSamples.Count > 0)
                return CreatePlayableSamples(nodeSamples);

            if (Samples.Count > 0)
                return CreatePlayableSamples();

            return new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) };
        }
    }
}
