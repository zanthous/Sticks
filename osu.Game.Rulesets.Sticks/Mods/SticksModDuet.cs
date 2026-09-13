using System;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Beatmaps;

namespace osu.Game.Rulesets.Sticks.Mods
{
    // Retired from the mod selector. System registration preserves existing saved DU acronyms.
    public class SticksModDuet : Mod, IApplicableToBeatmapConverter
    {
        public override string Name => "Duet";
        public override string Acronym => "DU";
        public override LocalisableString Description => "Experimental conversion: turn source rhythms and shapes into coordinated two-stick phrases. Applies to standard maps.";
        public override ModType Type => ModType.System;
        public override bool Ranked => false;
        public override Type[] IncompatibleMods => new[] { typeof(SticksModParity), typeof(SticksModParityDuet) };

        public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
        {
            if (beatmapConverter is SticksBeatmapConverter sticksConverter)
            {
                sticksConverter.ConversionMode = SticksConversionMode.Duet;
                sticksConverter.UseCounterpoint = false;
            }
        }
    }
}
