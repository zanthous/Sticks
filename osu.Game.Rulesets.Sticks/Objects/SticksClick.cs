using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    /// <summary>A timed button press for one hand, independent of stick position.</summary>
    public class SticksClick : SticksHitObject
    {
        internal static HitResult TimingGrade(HitResult result) => result switch
        {
            HitResult.Perfect => HitResult.Great,
            HitResult.Good => HitResult.Ok,
            HitResult.Ok => HitResult.Meh,
            _ => result,
        };

        public override Judgement CreateJudgement() => new ClickJudgement();

        protected override HitWindows CreateHitWindows() => new SticksClickHitWindows();

        // These native grades are worth 300/100/50 in Sticks. A click has no aim half.
        private class ClickJudgement : Judgement
        {
            public override HitResult MaxResult => HitResult.Perfect;
        }
    }
}
