// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using System.Linq;
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
    public class SticksSlider : SticksHitObject, IHasRepeats
    {
        private double duration;

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

        [JsonProperty("segments", Order = 100, NullValueHandling = NullValueHandling.Ignore)]
        public List<float> SerialisedSegments
        {
            get => customSegmentArcAngles?.ToList();
            set
            {
                if (value != null)
                    SetCustomSegments(value);
            }
        }

        public int RepeatCount
        {
            get => repeatCount;
            set
            {
                customSegmentArcAngles = null;
                repeatCount = Math.Max(0, value);
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
                arcAngle = value;
                RefreshLegacyEditorMarker();
            }
        }

        public int SegmentCount => customSegmentArcAngles?.Count ?? RepeatCount + 1;

        public bool HasCustomSegments => customSegmentArcAngles != null;

        public IReadOnlyList<float> SegmentArcAngles => customSegmentArcAngles ?? createLegacySegments();

        public float TotalAngularDistance => customSegmentArcAngles == null
            ? Math.Abs(ArcAngle) * SegmentCount
            : customSegmentArcAngles.Sum(segment => Math.Abs(segment));

        public int InitialDirection => Math.Sign(SegmentArcAngleAt(0)) == 0 ? 1 : Math.Sign(SegmentArcAngleAt(0));

        public double TickInterval { get; private set; }

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
            return Math.Clamp((time - segmentStart) / Math.Max(1, SegmentDurationAt(segmentIndex)), 0, 1);
        }

        public double SpanProgressAt(double time) => SegmentProgressAt(time);

        public double PathProgressAt(double time) => SegmentProgressAt(time);

        public bool CurrentSpanEndsWithReversal(double time) => SegmentIndexAt(time) < SegmentCount - 1;

        public (double Start, double End) RemainingPathRangeAt(double time) => (SegmentProgressAt(time), 1);

        public double RehearsalStartTime => Math.Max(StartTime - ApproachDuration, StartTime - SegmentDurationAt(0));

        public double RehearsalProgressAt(double time) =>
            Math.Clamp((time - RehearsalStartTime) / Math.Max(1, SegmentDurationAt(0)), 0, 1);

        public double AvailableTrackingDuration(double headHitTime) =>
            Math.Max(1, EndTime - Math.Max(StartTime, headHitTime));

        public void SetCustomSegments(IEnumerable<float> segments)
        {
            List<float> values = segments.Where(segment => float.IsFinite(segment) && Math.Abs(segment) >= 1).ToList();
            if (values.Count == 0)
                throw new ArgumentException("A slider requires at least one non-zero segment.", nameof(segments));

            customSegmentArcAngles = values;
            arcAngle = values[0];
            repeatCount = values.Count - 1;
            RefreshLegacyEditorMarker();
        }

        public void ReplaceFinalSegment(float segmentArcAngle)
        {
            List<float> segments = SegmentArcAngles.ToList();
            segments[^1] = segmentArcAngle;
            SetCustomSegments(segments);
        }

        public void AppendSegmentAtConstantSpeed(float segmentArcAngle)
        {
            float totalDistance = TotalAngularDistance;
            double degreesPerMillisecond = totalDistance / Math.Max(1, Duration);
            List<float> segments = SegmentArcAngles.ToList();
            segments.Add(segmentArcAngle);
            double addedDuration = Math.Abs(segmentArcAngle) / Math.Max(0.001, degreesPerMillisecond);
            SetCustomSegments(segments);
            Duration += addedDuration;
        }

        public bool RemoveFinalSegmentAtConstantSpeed()
        {
            if (SegmentCount <= 1)
                return false;

            float totalDistance = TotalAngularDistance;
            double degreesPerMillisecond = totalDistance / Math.Max(1, Duration);
            List<float> segments = SegmentArcAngles.ToList();
            float removed = segments[^1];
            segments.RemoveAt(segments.Count - 1);
            double reducedDuration = Math.Max(1, Duration - Math.Abs(removed) / Math.Max(0.001, degreesPerMillisecond));
            SetCustomSegments(segments);
            Duration = reducedDuration;
            return true;
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
            TickInterval = beatLength / Math.Max(1, difficulty.SliderTickRate);
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

                for (int segment = 0; segment < SegmentCount; segment++)
                {
                    double segmentStartTime = SegmentStartTimeAt(segment);
                    double segmentDuration = SegmentDurationAt(segment);

                    for (double tickTime = segmentStartTime + TickInterval; tickTime < segmentStartTime + segmentDuration - 10; tickTime += TickInterval)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        AddNested(new SticksSliderTick
                        {
                            StartTime = tickTime,
                            SliderStartTime = StartTime,
                            Side = Side,
                            Angle = AngleAt(tickTime),
                            Samples = new[] { tickSample },
                        });
                    }
                }
            }

            for (int reversalIndex = 0; reversalIndex < SegmentCount - 1; reversalIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double reversalTime = SegmentEndTimeAt(reversalIndex);
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
                Side = Side,
                Angle = AngleAt(EndTime),
                Samples = samplesAtNode(SegmentCount),
            });
        }

        public override Judgement CreateJudgement() => new SticksIgnoreJudgement();

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;

        private IList<HitSampleInfo> samplesAtNode(int nodeIndex)
        {
            IList<HitSampleInfo> nodeSamples = this.GetNodeSamples(nodeIndex);
            if (nodeSamples.Count > 0)
                return CreatePlayableSamples(nodeSamples);

            if (Samples.Count > 0)
                return CreatePlayableSamples();

            return new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) };
        }
    }
}
