using System;
using System.Linq;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModDifficultyAdjust : ModDifficultyAdjust, IApplicableToHitObject, IApplicableToBeatmapConverter,
                                             IApplicableToDrawableRuleset<SticksHitObject>, IApplicableToRate
    {
        public override LocalisableString Description => "Experiment with Sticks gameplay dimensions and converter features.";

        public override Type[] IncompatibleMods => base.IncompatibleMods
                                                       .Append(typeof(ModRateAdjust))
                                                       .Append(typeof(ModTimeRamp))
                                                       .Append(typeof(ModAdaptiveSpeed))
                                                       .ToArray();

        [SettingSource("Primary hit angle", "Override the full angular width awarded a 300.", LAST_SETTING_ORDER + 1,
            SettingControlType = typeof(DifficultyAdjustSettingsControl))]
        public DifficultyBindable PrimaryHitAngle { get; } = new DifficultyBindable
        {
            MinValue = 4,
            MaxValue = 90,
            Precision = 1,
            ReadCurrentFromDifficulty = difficulty => SticksHitObject.HitAngleForCircleSize(difficulty.CircleSize),
        };

        [SettingSource("Secondary hit angle", "Override the additional full angular width awarded a 100.", LAST_SETTING_ORDER + 2,
            SettingControlType = typeof(DifficultyAdjustSettingsControl))]
        public DifficultyBindable SecondaryHitAngle { get; } = new DifficultyBindable
        {
            MinValue = 0,
            MaxValue = 90,
            Precision = 1,
            ReadCurrentFromDifficulty = difficulty => SticksHitObject.HitAngleForCircleSize(difficulty.CircleSize) / 2,
        };

        [SettingSource("Disable reversals", "Convert repeated sliders into continuous one-way motion.", LAST_SETTING_ORDER + 3)]
        public BindableBool DisableReversals { get; } = new BindableBool();

        [SettingSource("80% stick travel", "Treat 80% physical stick distance as the edge of the playfield.", LAST_SETTING_ORDER + 4)]
        public BindableBool UseEightyPercentStickTravel { get; } = new BindableBool();

        [SettingSource("Speed", "Adjust gameplay and audio playback speed.", LAST_SETTING_ORDER + 5, SettingControlType = typeof(MultiplierSettingsSlider))]
        public BindableDouble SpeedChange { get; } = new BindableDouble(1)
        {
            MinValue = 0.5,
            MaxValue = 2,
            Precision = 0.01,
        };

        private readonly RateAdjustModHelper rateAdjustHelper;
        private readonly BindableBool adjustPitch = new BindableBool();

        public SticksModDifficultyAdjust()
        {
            rateAdjustHelper = new RateAdjustModHelper(SpeedChange);
            rateAdjustHelper.HandleAudioAdjustments(adjustPitch);
        }

        public double ApplyToRate(double time, double rate = 1) => rate * SpeedChange.Value;

        public void ApplyToTrack(IAdjustableAudioComponent track) => rateAdjustHelper.ApplyToTrack(track);

        public void ApplyToSample(IAdjustableAudioComponent sample) => sample.AddAdjustment(AdjustableProperty.Frequency, SpeedChange);

        public void ApplyToHitObject(HitObject hitObject)
        {
            if (hitObject is SticksHitObject sticksObject)
                applyAngles(sticksObject);
        }

        public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
        {
            if (beatmapConverter is SticksBeatmapConverter sticksConverter)
                sticksConverter.DisableReversals = DisableReversals.Value;
        }

        public void ApplyToDrawableRuleset(DrawableRuleset<SticksHitObject> drawableRuleset)
        {
            if (drawableRuleset.Playfield is SticksPlayfield playfield)
            {
                playfield.PhysicalStickDistanceAtGameEdge = UseEightyPercentStickTravel.Value ? 0.8f : 1;
            }
        }

        private void applyAngles(SticksHitObject hitObject)
        {
            if (PrimaryHitAngle.Value is float primary)
                hitObject.PrimaryHitAngle = primary;

            if (SecondaryHitAngle.Value is float secondary)
                hitObject.SecondaryHitAngle = secondary;

            foreach (HitObject nested in hitObject.NestedHitObjects)
            {
                if (nested is SticksHitObject sticksNested)
                    applyAngles(sticksNested);
            }
        }
    }
}
