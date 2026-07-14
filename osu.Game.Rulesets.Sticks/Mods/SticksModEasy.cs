// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModEasy : ModEasy
    {
        public override LocalisableString Description => "Larger hit angles, more forgiving HP drain, and less accuracy required.";

        // Sticks approach rate is a player setting, so it is intentionally excluded.
        public override void ApplyToDifficulty(BeatmapDifficulty difficulty)
        {
            difficulty.CircleSize *= ADJUST_RATIO;
            difficulty.OverallDifficulty *= ADJUST_RATIO;
            difficulty.DrainRate *= ADJUST_RATIO;
        }
    }
}
