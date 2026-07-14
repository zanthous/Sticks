// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    /// <summary>
    /// Marks a completed full loop of a slider. The stick continues in the same direction;
    /// this is a tracking checkpoint and never requires returning to neutral.
    /// </summary>
    public class SticksSliderExtension : SticksHitObject
    {
        public double SliderStartTime { get; set; }

        public double LoopDuration { get; set; }

        public int LoopIndex { get; set; }

        public int Direction { get; set; }

        public double PreemptDuration => StartTime - SliderStartTime + ApproachDuration;

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;

        public override Judgement CreateJudgement() => new SticksSliderTickJudgement();
    }
}
