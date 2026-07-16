// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Checks.Components;
using osu.Game.Rulesets.Sticks.Edit.Checks;

namespace osu.Game.Rulesets.Sticks.Edit
{
    public class SticksBeatmapVerifier : IBeatmapVerifier
    {
        private readonly IReadOnlyList<ICheck> checks = new ICheck[]
        {
            new CheckSticksSameStickOverlaps(),
            new CheckSticksObjectValidity(),
        };

        public IEnumerable<Issue> Run(BeatmapVerifierContext context) => checks.SelectMany(check => check.Run(context));
    }
}
