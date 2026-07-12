// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    public partial class SticksHealthProcessor : HealthProcessor
    {
        protected override double GetHealthIncreaseFor(JudgementResult result) => result.IsHit ? 0.04 : 0;

        protected override bool CheckDefaultFailCondition(JudgementResult result) => false;
    }
}
