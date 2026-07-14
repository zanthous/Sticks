// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    public class SticksSliderTailJudgement : Judgement
    {
        public override HitResult MaxResult => HitResult.SliderTailHit;

        public override HitResult MinResult => HitResult.IgnoreMiss;
    }
}
