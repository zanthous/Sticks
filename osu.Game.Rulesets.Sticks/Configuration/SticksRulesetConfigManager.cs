// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Configuration.Tracking;
using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Sticks.Objects;

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
            SetDefault(SticksRulesetSetting.ApproachRate, 5f, 0f, 10f, 0.1f);
        }

        public override TrackedSettings CreateTrackedSettings() => new TrackedSettings
        {
            new TrackedSetting<float>(SticksRulesetSetting.ApproachRate, approachRate => new SettingDescription(
                rawValue: approachRate,
                name: "Sticks approach rate",
                value: $"AR {approachRate:0.0} ({SticksHitObject.ApproachDurationFor(approachRate):0} ms)"
            )),
        };
    }

    public enum SticksRulesetSetting
    {
        ApproachRate,
    }
}
