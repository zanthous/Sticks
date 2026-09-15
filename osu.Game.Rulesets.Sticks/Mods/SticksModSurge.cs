using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Beatmaps;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModSurge : Mod, IApplicableToBeatmapConverter
    {
        public override string Name => "Surge";
        public override string Acronym => "SG";
        public override LocalisableString Description => "Reflect source slider speed changes with a wider range of movement speeds.";
        public override ModType Type => ModType.Conversion;
        public override bool Ranked => false;

        [SettingSource("Speed interpretation", "Follow source BPM and slider velocity, or emphasise speed changes relative to the map.")]
        public Bindable<SticksSliderBurstMode> SpeedInterpretation { get; } = new Bindable<SticksSliderBurstMode>(SticksSliderBurstMode.SourceSpeed);

        public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
        {
            if (beatmapConverter is SticksBeatmapConverter sticks)
                sticks.SliderBurstMode = SpeedInterpretation.Value;
        }
    }
}
