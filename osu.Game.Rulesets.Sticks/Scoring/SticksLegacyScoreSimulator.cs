// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring.Legacy;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    /// <summary>
    /// Satisfies lazer's editor compatibility interface. Sticks scores are custom-ruleset scores
    /// and never enter legacy score conversion under normal operation.
    /// </summary>
    internal sealed class SticksLegacyScoreSimulator : ILegacyScoreSimulator
    {
        public LegacyScoreAttributes Simulate(IWorkingBeatmap workingBeatmap, IBeatmap playableBeatmap) => new LegacyScoreAttributes
        {
            MaxCombo = playableBeatmap.HitObjects.Count,
        };

        public double GetLegacyScoreMultiplier(IReadOnlyList<Mod> mods, LegacyBeatmapConversionDifficultyInfo difficulty) => 1;
    }
}
