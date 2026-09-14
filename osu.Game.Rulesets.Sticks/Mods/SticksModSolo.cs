using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Beatmaps;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModSolo : Mod, IApplicableToBeatmapConverter
    {
        public override string Name => "Solo";
        public override string Acronym => "SO";
        public override LocalisableString Description => "Convert standard maps with only one active note or slider at a time.";
        public override ModType Type => ModType.Conversion;
        public override bool Ranked => false;

        public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
        {
            if (beatmapConverter is SticksBeatmapConverter sticksConverter)
                sticksConverter.SoloConversion = true;
        }
    }
}
