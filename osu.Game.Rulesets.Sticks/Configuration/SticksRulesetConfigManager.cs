using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Configuration.Tracking;
using osu.Framework.Extensions;
using osu.Framework.Threading;
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
            MigrateLegacySettings();
        }

        /// <summary>
        /// The radius "Hide centered cursors" always hid cursors within, before the radius could be chosen.
        /// </summary>
        public const float LEGACY_CURSOR_HIDE_RADIUS = 0.2f;

        /// <summary>
        /// Carries "Hide centered cursors", which was on or off, over to the radius that replaced it: on becomes the
        /// radius it always used. The old setting is then cleared, so this happens once.
        /// </summary>
        internal void MigrateLegacySettings()
        {
            if (!Get<bool>(SticksRulesetSetting.HideInactiveCursors))
                return;

            SetValue(SticksRulesetSetting.CursorHideRadius, LEGACY_CURSOR_HIDE_RADIUS);
            SetValue(SticksRulesetSetting.HideInactiveCursors, false);
        }

        private readonly object flickThresholdLockSync = new object();
        private bool flickThresholdWasDisabled;
        private int flickThresholdLockCount;

        internal IDisposable LockFlickActivationThreshold(Scheduler updateScheduler)
        {
            ArgumentNullException.ThrowIfNull(updateScheduler);
            var threshold = GetOriginalBindable<float>(SticksRulesetSetting.FlickActivationThreshold);
            // Retries can create their recorder before the previous one is disposed.
            // We only need to prevent edits, not acquire exclusive write access to the setting.
            lock (flickThresholdLockSync)
            {
                if (flickThresholdLockCount == 0)
                {
                    flickThresholdWasDisabled = threshold.Disabled;
                    threshold.Disabled = true;
                }

                flickThresholdLockCount++;
            }

            bool released = false;
            // Drawable disposal runs in the background. Disabled propagates directly
            // into loaded settings controls, so release the lock on the update thread.
            // Queue the decrement too: a retry started before this runs must inherit
            // the existing lock, not capture its disabled state as the original value.
            return new InvokeOnDisposal(() => updateScheduler.Add(() =>
            {
                lock (flickThresholdLockSync)
                {
                    // Drawable cleanup can run more than once; InvokeOnDisposal itself
                    // deliberately invokes its callback on every Dispose call.
                    if (released)
                        return;
                    released = true;

                    if (--flickThresholdLockCount == 0)
                        threshold.Disabled = flickThresholdWasDisabled;
                }
            }, forceScheduled: false));
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
            SetDefault(SticksRulesetSetting.CursorHideRadius, 0f, 0f, SticksPlayfield.MAX_CURSOR_HIDE_RADIUS, 0.01f);
            SetDefault(SticksRulesetSetting.PlayfieldDim, 0.7f, 0f, 1f, 0.01f);
            SetDefault(SticksRulesetSetting.Perspective, 0f, 0f, 1f, 0.01f);
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
            SetDefault(SticksRulesetSetting.OsuJoystickDeadzone, SticksRawStickInput.NOTHING_REMEMBERED);
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
            new TrackedSetting<float>(SticksRulesetSetting.CursorHideRadius, radius => new SettingDescription(
                rawValue: radius,
                name: "Sticks hide cursors near center",
                value: radius > 0 ? $"within {radius * 100:0}%" : "off"
            )),
            new TrackedSetting<float>(SticksRulesetSetting.PlayfieldDim, dim => new SettingDescription(
                rawValue: dim,
                name: "Sticks playfield dim",
                value: $"{dim * 100:0}%"
            )),
            new TrackedSetting<float>(SticksRulesetSetting.Perspective, perspective => new SettingDescription(
                rawValue: perspective,
                name: "Sticks 3D perspective",
                value: perspective > 0 ? $"{perspective * 100:0}%" : "off"
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
        /// <summary>
        /// Was "Hide centered cursors", on or off. No longer shown: it is read once to carry it over to
        /// <see cref="CursorHideRadius"/>, then cleared.
        /// </summary>
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
        PlayfieldDim,
        Perspective,

        /// <summary>
        /// How close to the center a stick's cursor is hidden, as a fraction of the way to the hit ring. 0 never hides it.
        /// </summary>
        CursorHideRadius,

        /// <summary>
        /// Not a Sticks preference: the player's osu! joystick deadzone, kept here while
        /// <see cref="SticksRawStickInput"/> has it switched off so that it can be put back.
        /// </summary>
        OsuJoystickDeadzone,
    }

    public enum SticksHitEffectMode
    {
        Perfect,
        Always,
        Never,
    }
}
