// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    public abstract class SticksHitObject : HitObject
    {
        public const float VISIBLE_ARC_SPAN = 20;
        public const float PRECISE_HALF_ANGLE = VISIBLE_ARC_SPAN / 2;
        public const float LENIENT_HALF_ANGLE = VISIBLE_ARC_SPAN;

        public float PrimaryHitAngle { get; set; } = VISIBLE_ARC_SPAN;

        public float SecondaryHitAngle { get; set; } = VISIBLE_ARC_SPAN;

        public float PreciseHalfAngle => PrimaryHitAngle / 2;

        public float LenientHalfAngle => (PrimaryHitAngle + SecondaryHitAngle) / 2;

        public StickSide Side { get; set; }

        public float Angle { get; set; }

        public bool ShowSyncedNoteLink { get; set; } = true;

        public ChordLinkStyle ChordLinkStyle { get; set; } = global::osu.Game.Rulesets.Sticks.Objects.ChordLinkStyle.ToCentre;

        public StickSide? SyncedNoteSide { get; set; }

        public float SyncedNoteAngle { get; set; }

        public double ApproachDuration { get; private set; } = 850;

        public static double ApproachDurationFor(float approachRate) =>
            IBeatmapDifficultyInfo.DifficultyRange(approachRate, 1200, 850, 500);

        public void ApplyPlayerApproachRate(float approachRate)
        {
            ApproachDuration = ApproachDurationFor(approachRate);

            foreach (HitObject nested in NestedHitObjects)
            {
                if (nested is SticksHitObject sticksNested)
                    sticksNested.ApplyPlayerApproachRate(approachRate);
            }
        }

        public override Judgement CreateJudgement() => new SticksJudgement();

        public HitResult ResultForCurrentAngleError(float angleError)
        {
            angleError = Math.Abs(angleError);

            if (angleError <= PreciseHalfAngle)
                return HitResult.Great;

            if (angleError <= LenientHalfAngle)
                return HitResult.Ok;

            return HitResult.Miss;
        }

        public static HitResult ResultForAngleError(float angleError)
        {
            angleError = Math.Abs(angleError);
            if (angleError <= PRECISE_HALF_ANGLE) return HitResult.Great;
            if (angleError <= LENIENT_HALF_ANGLE) return HitResult.Ok;
            return HitResult.Miss;
        }

        /// <summary>
        /// Makes an approaching note grow slowly while it is first being read, then accelerate
        /// towards its final size as the hit time approaches.
        /// </summary>
        public static double ApproachGrowthProgress(double linearProgress)
        {
            linearProgress = Math.Clamp(linearProgress, 0, 1);
            return linearProgress * linearProgress * linearProgress;
        }

        protected override void ApplyDefaultsToSelf(ControlPointInfo controlPointInfo, IBeatmapDifficultyInfo difficulty)
        {
            base.ApplyDefaultsToSelf(controlPointInfo, difficulty);
            ApproachDuration = ApproachDurationFor(difficulty.ApproachRate);
        }

        public static float NormaliseAngle(float angle)
        {
            angle %= 360;
            return angle < 0 ? angle + 360 : angle;
        }

        public static float DeltaAngle(float from, float to)
        {
            float delta = NormaliseAngle(to) - NormaliseAngle(from);
            if (delta > 180) delta -= 360;
            if (delta < -180) delta += 360;
            return delta;
        }

    }
}
