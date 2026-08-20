using System;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModStrum : Mod, IApplicableToDrawableRuleset<SticksHitObject>
    {
        public override string Name => "Strum";

        public override string Acronym => "ST";

        public override LocalisableString Description =>
            "Aim each stick outwards, then press its trigger or shoulder button to hit notes instead of flicking.";

        public override ModType Type => ModType.Fun;

        public override Type[] IncompatibleMods => new[] { typeof(ModAutoplay), typeof(ModRelax) };

        public void ApplyToDrawableRuleset(DrawableRuleset<SticksHitObject> drawableRuleset) =>
            ((SticksPlayfield)drawableRuleset.Playfield).StrumMode = true;
    }
}
