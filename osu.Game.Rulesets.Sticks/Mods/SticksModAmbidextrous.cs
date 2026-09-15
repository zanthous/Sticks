using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModAmbidextrous : Mod, IApplicableToDrawableRuleset<SticksHitObject>
    {
        public override string Name => "Ambidextrous";
        public override string Acronym => "AM";
        public override LocalisableString Description => "Play either colour with either controller side.";
        public override ModType Type => ModType.DifficultyReduction;
        public override bool Ranked => false;
        public void ApplyToDrawableRuleset(DrawableRuleset<SticksHitObject> drawableRuleset) =>
            ((SticksPlayfield)drawableRuleset.Playfield).EitherStick = true;
    }
}
