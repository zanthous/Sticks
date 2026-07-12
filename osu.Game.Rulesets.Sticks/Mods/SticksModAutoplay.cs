// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Replays;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModAutoplay : ModAutoplay
    {
        public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods) =>
            new ModReplayData(new SticksAutoGenerator(beatmap).Generate(), new ModCreatedUser { Username = "SticksBot" });
    }
}
