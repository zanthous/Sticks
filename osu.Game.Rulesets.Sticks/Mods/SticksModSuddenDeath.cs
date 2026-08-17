using osu.Framework.Bindables;
using osu.Game.Configuration;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModSuddenDeath : ModSuddenDeath
    {
        [SettingSource("Also fail when missing a slider tail")]
        public BindableBool FailOnSliderTail { get; } = new BindableBool();

        protected override bool FailCondition(HealthProcessor healthProcessor, JudgementResult result)
        {
            if (base.FailCondition(healthProcessor, result))
                return true;

            return FailOnSliderTail.Value
                   && result.HitObject is SticksSliderTail
                   && !result.IsHit;
        }
    }
}
