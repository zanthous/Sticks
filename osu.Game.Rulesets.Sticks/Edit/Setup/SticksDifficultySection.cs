using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit.Setup;

namespace osu.Game.Rulesets.Sticks.Edit.Setup
{
    /// <summary>
    /// Shows only map-level difficulty settings which Sticks consumes.
    /// Player-global approach rate and standard slider velocity are intentionally absent.
    /// </summary>
    public partial class SticksDifficultySection : SetupSection
    {
        private FormSliderBar<float> circleSizeSlider = null!;
        private FormSliderBar<float> healthDrainSlider = null!;
        private FormSliderBar<float> overallDifficultySlider = null!;
        private FormSliderBar<double> tickRateSlider = null!;

        public override LocalisableString Title => EditorSetupStrings.DifficultyHeader;

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                circleSizeSlider = new FormSliderBar<float>
                {
                    Caption = BeatmapsetsStrings.ShowStatsCs,
                    HintText = "Controls the angular size of Sticks hit windows.",
                    Current = new BindableFloat(Beatmap.Difficulty.CircleSize)
                    {
                        Default = SticksHitObject.DEFAULT_CIRCLE_SIZE,
                        MinValue = 0,
                        MaxValue = 10,
                        Precision = 0.1f,
                    },
                    TransferValueOnCommit = true,
                    TabbableContentContainer = this,
                },
                healthDrainSlider = new FormSliderBar<float>
                {
                    Caption = BeatmapsetsStrings.ShowStatsDrain,
                    HintText = "Controls passive health drain and miss penalties.",
                    Current = new BindableFloat(Beatmap.Difficulty.DrainRate)
                    {
                        Default = BeatmapDifficulty.DEFAULT_DIFFICULTY,
                        MinValue = 0,
                        MaxValue = 10,
                        Precision = 0.1f,
                    },
                    TransferValueOnCommit = true,
                    TabbableContentContainer = this,
                },
                overallDifficultySlider = new FormSliderBar<float>
                {
                    Caption = BeatmapsetsStrings.ShowStatsAccuracy,
                    HintText = "Controls timing judgement windows.",
                    Current = new BindableFloat(Beatmap.Difficulty.OverallDifficulty)
                    {
                        Default = BeatmapDifficulty.DEFAULT_DIFFICULTY,
                        MinValue = 0,
                        MaxValue = 10,
                        Precision = 0.1f,
                    },
                    TransferValueOnCommit = true,
                    TabbableContentContainer = this,
                },
                tickRateSlider = new FormSliderBar<double>
                {
                    Caption = EditorSetupStrings.TickRate,
                    HintText = "Controls beat-aligned tracking checkpoints on holds and sliders.",
                    KeyboardStep = 1,
                    Current = new BindableDouble(Beatmap.Difficulty.SliderTickRate)
                    {
                        Default = 1,
                        MinValue = 1,
                        MaxValue = 4,
                        Precision = 1,
                    },
                    TransferValueOnCommit = true,
                    TabbableContentContainer = this,
                },
            };

            foreach (FormSliderBar<float> item in Children.OfType<FormSliderBar<float>>())
                item.Current.ValueChanged += _ => updateValues();

            tickRateSlider.Current.ValueChanged += _ => updateValues();
        }

        private void updateValues()
        {
            Beatmap.Difficulty.CircleSize = circleSizeSlider.Current.Value;
            Beatmap.Difficulty.DrainRate = healthDrainSlider.Current.Value;
            Beatmap.Difficulty.OverallDifficulty = overallDifficultySlider.Current.Value;
            Beatmap.Difficulty.SliderTickRate = tickRateSlider.Current.Value;

            Beatmap.UpdateAllHitObjects();
            Beatmap.SaveState();
        }
    }
}
