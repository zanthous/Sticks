// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    public class SticksSliderRepeatJudgement : Judgement
    {
        public override HitResult MaxResult => HitResult.LargeTickHit;
    }
}
