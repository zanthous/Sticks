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
        internal static bool DisableBeatmapHitsoundsForConversion { get; private set; }

        public SticksRulesetConfigManager(SettingsStore settings, RulesetInfo ruleset)
            : base(settings, ruleset)
        {
            GetBindable<bool>(SticksRulesetSetting.DisableBeatmapHitsounds).BindValueChanged(
                value => DisableBeatmapHitsoundsForConversion = value.NewValue,
                true);
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
            SetDefault(SticksRulesetSetting.ChordLinkPresentation, SticksChordLinkPresentation.FullToCentre);
            SetDefault(SticksRulesetSetting.StackedNotePresentation, SticksStackedNotePresentation.RadialSpacing);
            SetDefault(SticksRulesetSetting.NotePresentation, SticksNotePresentation.CenterOut);
            SetDefault(SticksRulesetSetting.HideInactiveCursors, false);
            SetDefault(SticksRulesetSetting.SliderTrackingSparks, true);
            SetDefault(SticksRulesetSetting.ShowCursorTrails, false);
            SetDefault(SticksRulesetSetting.DisableBeatmapHitsounds, false);
            SetDefault(SticksRulesetSetting.SaveReplays, true);
            SetDefault(SticksRulesetSetting.NoteCircleScale,
                SticksPlayfield.DEFAULT_NOTE_CIRCLE_SCALE,
                SticksPlayfield.MIN_NOTE_CIRCLE_SCALE,
                SticksPlayfield.MAX_NOTE_CIRCLE_SCALE,
                0.1f);
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
            new TrackedSetting<SticksNotePresentation>(SticksRulesetSetting.NotePresentation, presentation => new SettingDescription(
                rawValue: presentation,
                name: "Sticks note presentation",
                value: presentation.GetDescription()
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
            new TrackedSetting<bool>(SticksRulesetSetting.ShowCursorTrails, enabled => new SettingDescription(
                rawValue: enabled,
                name: "Sticks cursor trails",
                value: enabled ? "enabled" : "disabled"
            )),
            new TrackedSetting<bool>(SticksRulesetSetting.DisableBeatmapHitsounds, disabled => new SettingDescription(
                rawValue: disabled,
                name: "Sticks disable beatmap hitsounds",
                value: disabled ? "enabled" : "disabled"
            )),
            new TrackedSetting<float>(SticksRulesetSetting.NoteCircleScale, scale => new SettingDescription(
                rawValue: scale,
                name: "Sticks note circle size",
                value: $"{scale:0.0}x"
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
        NotePresentation,
        NoteCircleScale,
        RadialApproachDistance,
        RadialApproachSpeed,
        HideInactiveCursors,
        SliderTrackingSparks,
        ShowCursorTrails,
        DisableBeatmapHitsounds,
        SaveReplays,
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

    public enum SticksNotePresentation
    {
        [Description("Bracket markers")]
        BracketMarkers,

        [Description("Approach circles")]
        ApproachCircles,

        [Description("Filling arcs")]
        FillingArcs,

        [Description("Center-out")]
        CenterOut,
    }
}
