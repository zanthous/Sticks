using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModRelax : ModRelax, IApplicableToDrawableRuleset<SticksHitObject>, IHasNoTimedInputs
    {
        public override LocalisableString Description =>
            "Aim each stick by direction. Flicking, recharging, and holding are automatic.";

        public void ApplyToDrawableRuleset(DrawableRuleset<SticksHitObject> drawableRuleset) =>
            ((SticksPlayfield)drawableRuleset.Playfield).RelaxMode = true;
    }
}
