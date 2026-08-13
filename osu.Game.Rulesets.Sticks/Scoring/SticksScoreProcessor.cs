// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

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
            judgement is SticksAngleJudgement ? HitResult.SmallTickHit : base.GetSimulatedHitResult(judgement);

        public override int GetBaseScoreForResult(HitResult result) =>
            result == HitResult.SmallTickHit ? 300 : base.GetBaseScoreForResult(result);

        protected override double GetComboScoreChange(JudgementResult result) => isAngleComponent(result)
            ? 0
            : base.GetComboScoreChange(result);

        protected override void ApplyScoreChange(JudgementResult result)
        {
            base.ApplyScoreChange(result);

            if (!isAngleComponent(result) || !result.IsHit)
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

            if (!isAngleComponent(result) || !result.IsHit)
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

        private static bool isAngleComponent(JudgementResult result) =>
            result.HitObject is ISticksAccuracyComponent { AccuracyComponent: SticksAccuracyComponent.Angle };
    }
}
