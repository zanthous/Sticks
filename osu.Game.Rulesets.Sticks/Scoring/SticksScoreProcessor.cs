using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    public partial class SticksScoreProcessor : ScoreProcessor
    {
        public SticksScoreProcessor(Ruleset ruleset)
            : base(ruleset)
        {
        }

        protected override IEnumerable<HitObject> EnumerateHitObjects(IBeatmap beatmap) =>
            base.EnumerateHitObjects(beatmap).OrderBy(hitObject => hitObject.GetEndTime());

        protected override HitResult GetSimulatedHitResult(Judgement judgement) =>
            judgement is SticksAngleJudgement or SticksClick.TimingWeightJudgement ? HitResult.SmallTickHit : base.GetSimulatedHitResult(judgement);

        // Timing and aim each own half of a 300-point head. SmallTickHit is the
        // combo-neutral carrier for the second half, including a click's repeated timing grade.
        public override int GetBaseScoreForResult(HitResult result) => result switch
        {
            HitResult.SmallTickHit => 150,
            HitResult.Great or HitResult.Ok or HitResult.Meh => base.GetBaseScoreForResult(result) / 2,
            _ => base.GetBaseScoreForResult(result),
        };

        protected override double GetComboScoreChange(JudgementResult result) => result.HitObject switch
        {
            SticksClick.TimingWeight => 0,
            ISticksAccuracyComponent { AccuracyComponent: SticksAccuracyComponent.Angle } => 0,
            SticksClick or SticksSlice => 2 * base.GetComboScoreChange(result),
            // Timing owns the entire head's combo contribution, including aim's half.
            ISticksAccuracyComponent { AccuracyComponent: SticksAccuracyComponent.Timing } => 2 * base.GetComboScoreChange(result),
            _ => base.GetComboScoreChange(result),
        };

        protected override void ApplyScoreChange(JudgementResult result)
        {
            base.ApplyScoreChange(result);

            if (!isComboNeutralComponent(result) || !result.IsHit)
                return;

            // The timing component owns this note's single combo increment. Accuracy still sees
            // both equally-weighted native judgements, while a failed angle remains a real miss
            // and therefore retains the base combo break.
            Combo.Value -= result.ComboAfterJudgement - result.ComboAtJudgement;
            HighestCombo.Value -= result.HighestComboAfterJudgement - result.HighestComboAtJudgement;
        }

        protected override void RemoveScoreChange(JudgementResult result)
        {
            base.RemoveScoreChange(result);

            if (!isComboNeutralComponent(result) || !result.IsHit)
                return;

            // ScoreProcessor performs its normal combo reversion before reaching this hook.
            // Restore the exact delta suppressed above to make rewinding symmetric.
            Combo.Value += result.ComboAfterJudgement - result.ComboAtJudgement;
            HighestCombo.Value += result.HighestComboAfterJudgement - result.HighestComboAtJudgement;
        }

        protected override HitEvent CreateHitEvent(JudgementResult result)
        {
            HitEvent hitEvent = base.CreateHitEvent(result);

            // HitEvent's position is the only ruleset-owned measurement payload retained by
            // lazer's score/result pipeline. Sticks uses X for absolute angular error; all other
            // events intentionally retain a null position.
            return result.HitObject is SticksAngleComponent { HitError: float angleError }
                ? hitEvent.With(new Vector2(angleError, 0))
                : hitEvent;
        }

        private static bool isComboNeutralComponent(JudgementResult result) =>
            result.HitObject is SticksClick.TimingWeight or ISticksAccuracyComponent { AccuracyComponent: SticksAccuracyComponent.Angle };
    }
}
