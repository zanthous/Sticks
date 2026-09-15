using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Configuration.Tracking;
using osu.Framework.Extensions;
using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;

namespace osu.Game.Rulesets.Sticks.Configuration
{
    public class SticksRulesetConfigManager : RulesetConfigManager<SticksRulesetSetting>
    {
        internal static bool DisableBeatmapHitsoundsForConversion { get; private set; }

        public SticksRulesetConfigManager(SettingsStore settings, RulesetInfo ruleset)
            : base(settings, ruleset)
        {
            GetBindable<bool>(SticksRulesetSetting.DisableBeatmapHitsounds).BindValueChanged(
                value => DisableBeatmapHitsoundsForConversion = value.NewValue,
                true);
        }

        private readonly object flickThresholdLockSync = new object();
        private LeasedBindable<float> flickThresholdLease;
        private int flickThresholdLockCount;

        internal IDisposable LockFlickActivationThreshold()
        {
            // Retries can create their recorder before the previous one is disposed.
            lock (flickThresholdLockSync)
            {
                if (flickThresholdLockCount == 0)
                    flickThresholdLease = GetBindable<float>(SticksRulesetSetting.FlickActivationThreshold).BeginLease(false);

                flickThresholdLockCount++;
            }

            return new InvokeOnDisposal(() =>
            {
                lock (flickThresholdLockSync)
                {
                    if (--flickThresholdLockCount != 0)
                        return;

                    flickThresholdLease.Return();
                    flickThresholdLease = null;
                }
            });
        }

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();
            SetDefault(SticksRulesetSetting.ApproachRate, 7.5f, 0f, 12f, 0.1f);
            SetDefault(SticksRulesetSetting.FlickActivationThreshold,
                SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD,
                SticksInputTracker.MIN_ACTIVATION_THRESHOLD,
                SticksInputTracker.MAX_ACTIVATION_THRESHOLD,
                0.01f);
            SetDefault(SticksRulesetSetting.HideInactiveCursors, false);
            SetDefault(SticksRulesetSetting.SliderTrackingSparks, true);
            SetDefault(SticksRulesetSetting.SliderHeadFollowsPath, false);
            SetDefault(SticksRulesetSetting.HitEffects, SticksHitEffectMode.Perfect);
            SetDefault(SticksRulesetSetting.ShowCursorTrails, false);
            SetDefault(SticksRulesetSetting.UseSkinColours, true);
            SetDefault(SticksRulesetSetting.DisableBeatmapHitsounds, false);
            SetDefault(SticksRulesetSetting.SaveReplays, true);
            SetDefault(SticksRulesetSetting.LeftStickColour, (Colour4)SticksPlayfield.LEFT_COLOUR);
            SetDefault(SticksRulesetSetting.RightStickColour, (Colour4)SticksPlayfield.RIGHT_COLOUR);
            SetDefault(SticksRulesetSetting.OverlapColour, (Colour4)SticksPlayfield.OVERLAP_COLOUR);
        }

        public override TrackedSettings CreateTrackedSettings() => new TrackedSettings
        {
            new TrackedSetting<float>(SticksRulesetSetting.ApproachRate, approachRate => new SettingDescription(
                rawValue: approachRate,
                name: "Sticks approach rate",
                value: $"AR {approachRate:0.0} ({SticksHitObject.ApproachDurationFor(approachRate):0} ms)"
            )),
            new TrackedSetting<float>(SticksRulesetSetting.FlickActivationThreshold, threshold => new SettingDescription(
                rawValue: threshold,
                name: "Sticks flick activation",
                value: $"{threshold * 100:0}%"
            )),
            new TrackedSetting<bool>(SticksRulesetSetting.HideInactiveCursors, hidden => new SettingDescription(
                rawValue: hidden,
                name: "Sticks hide inactive cursors",
                value: hidden ? "enabled" : "disabled"
            )),
            new TrackedSetting<bool>(SticksRulesetSetting.SliderTrackingSparks, enabled => new SettingDescription(
                rawValue: enabled,
                name: "Sticks contact effects",
                value: enabled ? "enabled" : "disabled"
            )),
            new TrackedSetting<bool>(SticksRulesetSetting.SliderHeadFollowsPath, enabled => new SettingDescription(
                rawValue: enabled,
                name: "Sticks slider heads follow path",
                value: enabled ? "enabled" : "disabled"
            )),
            new TrackedSetting<bool>(SticksRulesetSetting.ShowCursorTrails, enabled => new SettingDescription(
                rawValue: enabled,
                name: "Sticks cursor trails",
                value: enabled ? "enabled" : "disabled"
            )),
            new TrackedSetting<SticksHitEffectMode>(SticksRulesetSetting.HitEffects, mode => new SettingDescription(
                rawValue: mode,
                name: "Sticks hit effects",
                value: mode.GetDescription()
            )),
            new TrackedSetting<bool>(SticksRulesetSetting.DisableBeatmapHitsounds, disabled => new SettingDescription(
                rawValue: disabled,
                name: "Sticks disable beatmap hitsounds",
                value: disabled ? "enabled" : "disabled"
            )),
            new TrackedSetting<Colour4>(SticksRulesetSetting.LeftStickColour, colour => new SettingDescription(
                rawValue: colour,
                name: "Sticks left stick color",
                value: colour.ToHex()
            )),
            new TrackedSetting<Colour4>(SticksRulesetSetting.RightStickColour, colour => new SettingDescription(
                rawValue: colour,
                name: "Sticks right stick color",
                value: colour.ToHex()
            )),
            new TrackedSetting<Colour4>(SticksRulesetSetting.OverlapColour, colour => new SettingDescription(
                rawValue: colour,
                name: "Sticks overlap color",
                value: colour.ToHex()
            )),
        };
    }

    public enum SticksRulesetSetting
    {
        ApproachRate,
        FlickActivationThreshold,
        HideInactiveCursors,
        SliderTrackingSparks,
        ShowCursorTrails,
        DisableBeatmapHitsounds,
        SaveReplays,
        LeftStickColour,
        RightStickColour,
        OverlapColour,
        UseSkinColours,
        HitEffects,
        SliderHeadFollowsPath,
    }

    public enum SticksHitEffectMode
    {
        Perfect,
        Always,
        Never,
    }
}
