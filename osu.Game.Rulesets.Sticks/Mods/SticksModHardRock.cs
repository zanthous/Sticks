using System;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Sticks.Mods
{
    /// <summary>
    /// Standard-style Hard Rock difficulty increases, excluding approach rate because Sticks AR
    /// is a player-controlled setting. Angular difficulty is derived from circle size.
    /// </summary>
    public class SticksModHardRock : ModHardRock
    {
        public override void ApplyToDifficulty(BeatmapDifficulty difficulty)
        {
            base.ApplyToDifficulty(difficulty);

            difficulty.OverallDifficulty = Math.Min(difficulty.OverallDifficulty * ADJUST_RATIO, 10);
            difficulty.CircleSize = Math.Min(difficulty.CircleSize * 1.3f, 10);
        }
    }
}
