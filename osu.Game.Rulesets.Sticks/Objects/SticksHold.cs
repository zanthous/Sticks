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

        public double TickInterval { get; private set; }

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
            TickInterval = beatLength / System.Math.Max(1, difficulty.SliderTickRate);
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
            });

            if (double.IsFinite(TickInterval) && TickInterval > 0)
            {
                HitSampleInfo sourceSample = Samples.FirstOrDefault(sample => sample.Name == HitSampleInfo.HIT_NORMAL) ?? Samples.FirstOrDefault();
                HitSampleInfo tickSample = sourceSample == null || SticksAuthoredBeatmapCodec.IsMarker(sourceSample)
                    ? new HitSampleInfo("slidertick", volume: sourceSample?.Volume ?? 100)
                    : sourceSample.With("slidertick");

                for (double tickTime = StartTime + TickInterval; tickTime < EndTime - 10; tickTime += TickInterval)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    AddNested(new SticksHoldTick
                    {
                        StartTime = tickTime,
                        HoldStartTime = StartTime,
                        Side = Side,
                        Angle = Angle,
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
                Samples = CreatePlayableSamples(),
            });
        }

        public override Judgement CreateJudgement() => new SticksIgnoreJudgement();

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;
    }
}
