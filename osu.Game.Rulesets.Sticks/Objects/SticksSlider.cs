// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.Sticks.Objects
{
    public class SticksSlider : SticksHitObject, IHasRepeats
    {
        public const double REQUIRED_TRACKING_FRACTION = 0.5;

        public double Duration { get; set; }

        public double EndTime => StartTime + Duration;

        private int repeatCount;

        public int RepeatCount
        {
            get => repeatCount;
            set => repeatCount = Math.Max(0, value);
        }

        public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();

        public int SpanCount => RepeatCount + 1;

        public double SpanDuration => Duration / SpanCount;

        public float ArcAngle { get; set; }

        public int InitialDirection => ArcAngle < 0 ? -1 : 1;

        public double TickInterval { get; private set; }

        public float AngleAt(double time)
        {
            return Angle + ArcAngle * (float)PathProgressAt(time);
        }

        public int SpanIndexAt(double time)
        {
            double elapsed = Math.Clamp(time - StartTime, 0, Duration);
            return Math.Min(SpanCount - 1, (int)(elapsed / Math.Max(1, SpanDuration)));
        }

        public double SpanProgressAt(double time)
        {
            int spanIndex = SpanIndexAt(time);
            double elapsedInSpan = Math.Clamp(time - StartTime - spanIndex * SpanDuration, 0, SpanDuration);
            return elapsedInSpan / Math.Max(1, SpanDuration);
        }

        public double PathProgressAt(double time)
        {
            double spanProgress = SpanProgressAt(time);
            return SpanIndexAt(time) % 2 == 0 ? spanProgress : 1 - spanProgress;
        }

        public bool CurrentSpanEndsWithReversal(double time) => SpanIndexAt(time) < RepeatCount;

        public (double Start, double End) RemainingPathRangeAt(double time)
        {
            double pathProgress = PathProgressAt(time);
            return SpanIndexAt(time) % 2 == 0
                ? (pathProgress, 1)
                : (0, pathProgress);
        }

        public double RehearsalStartTime => Math.Max(StartTime - ApproachDuration, StartTime - SpanDuration);

        public double RehearsalProgressAt(double time) =>
            Math.Clamp((time - RehearsalStartTime) / Math.Max(1, SpanDuration), 0, 1);

        public double AvailableTrackingDuration(double headHitTime) =>
            Math.Max(1, EndTime - Math.Max(StartTime, headHitTime));

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

            if (double.IsFinite(TickInterval) && TickInterval > 0)
            {
                HitSampleInfo tickSample = (Samples.FirstOrDefault(sample => sample.Name == HitSampleInfo.HIT_NORMAL) ?? Samples.FirstOrDefault())?.With("slidertick")
                                               ?? new HitSampleInfo("slidertick");

                for (int span = 0; span < SpanCount; span++)
                {
                    double spanStartTime = StartTime + span * SpanDuration;

                    for (double tickTime = spanStartTime + TickInterval; tickTime < spanStartTime + SpanDuration - 10; tickTime += TickInterval)
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

            for (int repeatIndex = 0; repeatIndex < RepeatCount; repeatIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double repeatTime = StartTime + (repeatIndex + 1) * SpanDuration;
                int directionAfter = repeatIndex % 2 == 0 ? -Math.Sign(ArcAngle) : Math.Sign(ArcAngle);

                AddNested(new SticksSliderRepeat
                {
                    StartTime = repeatTime,
                    SliderStartTime = StartTime,
                    SpanDuration = SpanDuration,
                    RepeatIndex = repeatIndex,
                    DirectionAfter = directionAfter,
                    Side = Side,
                    Angle = AngleAt(repeatTime),
                    Samples = samplesAtNode(repeatIndex + 1),
                });
            }

            double absoluteArc = Math.Abs(ArcAngle);
            if (absoluteArc > 360)
            {
                double loopDuration = SpanDuration * 360 / absoluteArc;

                for (int span = 0; span < SpanCount; span++)
                {
                    int direction = Math.Sign(ArcAngle) * (span % 2 == 0 ? 1 : -1);

                    for (int loop = 1; loop * 360 < absoluteArc - 0.001; loop++)
                    {
                        double extensionTime = StartTime + span * SpanDuration + loop * loopDuration;
                        AddNested(new SticksSliderExtension
                        {
                            StartTime = extensionTime,
                            SliderStartTime = StartTime,
                            LoopDuration = loopDuration,
                            LoopIndex = loop,
                            Direction = direction,
                            Side = Side,
                            Angle = AngleAt(extensionTime),
                            Samples = new[] { new HitSampleInfo("slidertick") },
                        });
                    }
                }
            }

            AddNested(new SticksSliderTail
            {
                StartTime = EndTime,
                SliderStartTime = StartTime,
                Side = Side,
                Angle = AngleAt(EndTime),
                Samples = samplesAtNode(RepeatCount + 1),
            });
        }

        private IList<HitSampleInfo> samplesAtNode(int nodeIndex)
        {
            IList<HitSampleInfo> nodeSamples = this.GetNodeSamples(nodeIndex);
            if (nodeSamples.Count > 0)
                return nodeSamples.Select(sample => sample.With()).ToArray();

            if (Samples.Count > 0)
                return Samples.Select(sample => sample.With()).ToArray();

            return new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) };
        }
    }
}
