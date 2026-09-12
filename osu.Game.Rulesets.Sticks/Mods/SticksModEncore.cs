using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Beatmaps;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModEncore : Mod, IApplicableToBeatmapConverter
    {
        public override string Name => "Encore";
        public override string Acronym => "EN";
        public override LocalisableString Description => "Enable additional note types when converting standard maps.";
        public override ModType Type => ModType.Conversion;
        public override bool Ranked => false;

        public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
        {
            if (beatmapConverter is SticksBeatmapConverter sticksConverter)
                sticksConverter.AddClickNotes = true;
        }
    }
}
