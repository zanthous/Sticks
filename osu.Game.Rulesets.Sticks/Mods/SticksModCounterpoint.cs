#nullable enable

using System;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Beatmaps;

namespace osu.Game.Rulesets.Sticks.Mods
{
    // Retired from the mod selector. System registration preserves existing saved CP acronyms.
    public class SticksModCounterpoint : Mod, IApplicableToBeatmapConverter
    {
        public override string Name => "Counterpoint";
        public override string Acronym => "CP";
        public override LocalisableString Description => "The two-stick arrangement now used by default.";
        public override ModType Type => ModType.System;
        public override bool Ranked => false;
        public override Type[] IncompatibleMods => new[] { typeof(SticksModDuet), typeof(SticksModParityDuet) };

        internal Action<string, double, double, int, int>? ArrangementObserved { get; set; }

        public void ApplyToBeatmapConverter(IBeatmapConverter converter)
        {
            if (converter is SticksBeatmapConverter sticks)
            {
                sticks.UseCounterpoint = true;
                sticks.CounterpointArrangementObserved = ArrangementObserved;
            }
        }
    }
}
