// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    public class SticksSliderRepeat : SticksHitObject
    {
        public double SliderStartTime { get; set; }

        public double SpanDuration { get; set; }

        public int RepeatIndex { get; set; }

        public int DirectionAfter { get; set; }

        public double PreemptDuration => StartTime - SliderStartTime + ApproachDuration;

        public double DisplayPreempt => SpanDuration;

        public static bool IsAngleInRange(float angleError, float primaryHitAngle = VISIBLE_ARC_SPAN, float secondaryHitAngle = VISIBLE_ARC_SPAN) =>
            Math.Abs(angleError) <= (primaryHitAngle + secondaryHitAngle) / 2;

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;

        public override Judgement CreateJudgement() => new SticksSliderRepeatJudgement();
    }
}
