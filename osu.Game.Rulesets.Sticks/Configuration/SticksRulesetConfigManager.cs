// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osu.Framework.Configuration.Tracking;
using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;

namespace osu.Game.Rulesets.Sticks.Configuration
{
    public class SticksRulesetConfigManager : RulesetConfigManager<SticksRulesetSetting>
    {
        public SticksRulesetConfigManager(SettingsStore settings, RulesetInfo ruleset)
            : base(settings, ruleset)
        {
        }

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();
            SetDefault(SticksRulesetSetting.ApproachRate, 5f, 0f, 12f, 0.1f);
            SetDefault(SticksRulesetSetting.FlickActivationThreshold,
                SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD,
                SticksInputTracker.MIN_ACTIVATION_THRESHOLD,
                SticksInputTracker.MAX_ACTIVATION_THRESHOLD,
                0.01f);
            SetDefault(SticksRulesetSetting.RadialStackedNoteSpacing, true);
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
            new TrackedSetting<bool>(SticksRulesetSetting.RadialStackedNoteSpacing, enabled => new SettingDescription(
                rawValue: enabled,
                name: "Sticks stacked-note spacing",
                value: enabled ? "Radial" : "Disabled"
            )),
        };
    }

    public enum SticksRulesetSetting
    {
        ApproachRate,
        FlickActivationThreshold,
        RadialStackedNoteSpacing,
    }
}
