using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    public class SticksJudgement : Judgement
    {
        public override HitResult MaxResult => HitResult.Great;
    }
}
