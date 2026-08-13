// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Linq;
using System.Threading;
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
    public class SticksHold : SticksHitObject, IHasDuration
    {
        private double duration;
        private ControlPointInfo controlPointInfo = null!;
        private double tickRate;

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
            phase -= System.Math.Floor(phase);
            return 0.5f + 0.5f * (float)System.Math.Cos(phase * System.Math.PI * 2);
        }

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

        protected override void ApplyDefaultsToSelf(ControlPointInfo controlPointInfo, IBeatmapDifficultyInfo difficulty)
        {
            base.ApplyDefaultsToSelf(controlPointInfo, difficulty);

            double beatLength = controlPointInfo.TimingPointAt(StartTime).BeatLength;
            tickRate = System.Math.Max(1, difficulty.SliderTickRate);
            TickInterval = beatLength / tickRate;
            this.controlPointInfo = controlPointInfo;
        }

        protected override void CreateNestedHitObjects(CancellationToken cancellationToken)
        {
            base.CreateNestedHitObjects(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            AddNested(new SticksHoldHead
            {
                StartTime = StartTime,
                Side = Side,
                Angle = Angle,
                DefaultHitAngleAdjustment = DefaultHitAngleAdjustment,
            });

            if (double.IsFinite(TickInterval) && TickInterval > 0)
            {
                HitSampleInfo sourceSample = Samples.FirstOrDefault(sample => sample.Name == HitSampleInfo.HIT_NORMAL) ?? Samples.FirstOrDefault();
                HitSampleInfo tickSample = sourceSample == null || SticksAuthoredBeatmapCodec.IsMarker(sourceSample)
                    ? new HitSampleInfo("slidertick", volume: sourceSample?.Volume ?? 100)
                    : sourceSample.With("slidertick");

                foreach (double tickTime in SticksTickGenerator.Generate(controlPointInfo, StartTime, EndTime, tickRate, cancellationToken))
                {
                    AddNested(new SticksHoldTick
                    {
                        StartTime = tickTime,
                        HoldStartTime = StartTime,
                        Side = Side,
                        Angle = Angle,
                        DefaultHitAngleAdjustment = DefaultHitAngleAdjustment,
                        Samples = new[] { tickSample },
                    });
                }
            }

            AddNested(new SticksHoldTail
            {
                StartTime = EndTime,
                HoldStartTime = StartTime,
                Side = Side,
                Angle = Angle,
                DefaultHitAngleAdjustment = DefaultHitAngleAdjustment,
                Samples = CreatePlayableSamples(),
            });
        }

        public override Judgement CreateJudgement() => new SticksIgnoreJudgement();

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;
    }
}
