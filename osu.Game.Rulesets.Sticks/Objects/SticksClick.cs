using System.Threading;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    /// <summary>A direction-free, full-weight timing note.</summary>
    public class SticksClick : SticksHitObject
    {
        protected override void CreateNestedHitObjects(CancellationToken cancellationToken)
        {
            base.CreateNestedHitObjects(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            AddNested(new TimingWeight { StartTime = StartTime });
        }

        // Ordinary heads have two 150-point components. A click applies its one timing
        // grade to both halves, retaining 300/100/50 points without a separate grade or aim check.
        // Keeping this as a nested result also preserves saved-score reconstruction and rewind.
        public class TimingWeight : HitObject
        {
            public override Judgement CreateJudgement() => new TimingWeightJudgement();
            protected override HitWindows CreateHitWindows() => HitWindows.Empty;
        }

        internal class TimingWeightJudgement : SticksJudgement
        {
        }
    }
}
