// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    public class SticksHoldTick : SticksHitObject
    {
        public double HoldStartTime { get; set; }

        public double PreemptDuration => StartTime - HoldStartTime + ApproachDuration;

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;

        public override Judgement CreateJudgement() => new SticksSliderTickJudgement();
    }
}
