// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    public class SticksSliderTick : SticksHitObject
    {
        public double SliderStartTime { get; set; }

        public double PreemptDuration => StartTime - SliderStartTime + ApproachDuration;

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;

        public override Judgement CreateJudgement() => new SticksSliderTickJudgement();
    }
}
