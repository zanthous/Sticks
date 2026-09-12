using System;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Beatmaps;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModParity : Mod, IApplicableToBeatmapConverter
    {
        public override string Name => "Parity";
        public override string Acronym => "PA";
        public override LocalisableString Description => "Experimental conversion: broad direction changes shaped by each section's rhythm and movement. Applies to standard maps.";
        public override ModType Type => ModType.Conversion;
        public override bool Ranked => false;
        public override Type[] IncompatibleMods => new[] { typeof(SticksModDuet), typeof(SticksModParityDuet) };

        public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
        {
            if (beatmapConverter is SticksBeatmapConverter sticksConverter)
                sticksConverter.ConversionMode = SticksConversionMode.Parity;
        }
    }
}
