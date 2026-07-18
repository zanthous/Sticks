// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.ComponentModel;
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
        public SticksRulesetConfigManager(SettingsStore settings, RulesetInfo ruleset)
            : base(settings, ruleset)
        {
        }

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();
            SetDefault(SticksRulesetSetting.ApproachRate, 8f, 0f, 12f, 0.1f);
            SetDefault(SticksRulesetSetting.FlickActivationThreshold,
                SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD,
                SticksInputTracker.MIN_ACTIVATION_THRESHOLD,
                SticksInputTracker.MAX_ACTIVATION_THRESHOLD,
                0.01f);
            SetDefault(SticksRulesetSetting.ChordLinkPresentation, SticksChordLinkPresentation.FullToCentre);
            SetDefault(SticksRulesetSetting.StackedNotePresentation, SticksStackedNotePresentation.RadialSpacing);
            SetDefault(SticksRulesetSetting.RadialApproachDistance,
                SticksPlayfield.DEFAULT_RADIAL_APPROACH_DISTANCE,
                0f,
                120f,
                1f);
            SetDefault(SticksRulesetSetting.RadialApproachSpeed,
                SticksPlayfield.DEFAULT_RADIAL_APPROACH_SPEED,
                0.25f,
                4f,
                0.05f);
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
            new TrackedSetting<SticksChordLinkPresentation>(SticksRulesetSetting.ChordLinkPresentation, presentation => new SettingDescription(
                rawValue: presentation,
                name: "Sticks synced-note links",
                value: presentation.GetDescription()
            )),
            new TrackedSetting<SticksStackedNotePresentation>(SticksRulesetSetting.StackedNotePresentation, presentation => new SettingDescription(
                rawValue: presentation,
                name: "Sticks stacked-note presentation",
                value: presentation.GetDescription()
            )),
            new TrackedSetting<float>(SticksRulesetSetting.RadialApproachDistance, distance => new SettingDescription(
                rawValue: distance,
                name: "Sticks radial approach distance",
                value: $"{distance:0}"
            )),
            new TrackedSetting<float>(SticksRulesetSetting.RadialApproachSpeed, speed => new SettingDescription(
                rawValue: speed,
                name: "Sticks radial approach speed",
                value: $"{speed:0.00}x"
            )),
        };
    }

    public enum SticksRulesetSetting
    {
        ApproachRate,
        FlickActivationThreshold,
        ChordLinkPresentation,
        StackedNotePresentation,
        RadialApproachDistance,
        RadialApproachSpeed,
    }

    public enum SticksChordLinkPresentation
    {
        [Description("Full to center")]
        FullToCentre,

        [Description("Short cues")]
        Short,

        [Description("Hidden")]
        Hidden,
    }

    public enum SticksStackedNotePresentation
    {
        [Description("Show stacked")]
        ShowStacked,

        [Description("Radial spacing")]
        RadialSpacing,

        [Description("Radial approach")]
        RadialApproach,
    }
}
