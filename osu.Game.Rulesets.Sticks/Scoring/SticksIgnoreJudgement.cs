using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    public class SticksIgnoreJudgement : Judgement
    {
        public override HitResult MaxResult => HitResult.IgnoreHit;
    }
}
