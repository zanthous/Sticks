using System;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Beatmaps;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModParityDuet : Mod, IApplicableToBeatmapConverter
    {
        public override string Name => "Parity + Duet";
        public override string Acronym => "PD";
        public override LocalisableString Description => "Experimental conversion: Duet's two-stick patterns with Parity's section-dependent direction changes. Applies to standard maps.";
        public override ModType Type => ModType.Conversion;
        public override bool Ranked => false;
        public override Type[] IncompatibleMods => new[] { typeof(SticksModParity), typeof(SticksModDuet) };

        public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
        {
            if (beatmapConverter is SticksBeatmapConverter sticksConverter)
                sticksConverter.ConversionMode = SticksConversionMode.ParityDuet;
        }
    }
}
