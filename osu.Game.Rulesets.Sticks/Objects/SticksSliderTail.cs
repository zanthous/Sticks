// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    public class SticksSliderTail : SticksHitObject
    {
        public double SliderStartTime { get; set; }

        /// <summary>
        /// Keeps the tail alive from the same point as its parent slider, including on sliders
        /// which last longer than the framework's default nested-object lifetime window.
        /// </summary>
        public double PreemptDuration => StartTime - SliderStartTime + ApproachDuration;

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;

        public override Judgement CreateJudgement() => new SticksSliderTailJudgement();
    }
}
