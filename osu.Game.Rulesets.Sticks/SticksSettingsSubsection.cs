// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.Sticks
{
    public partial class SticksSettingsSubsection : RulesetSettingsSubsection
    {
        [Resolved]
        private BeatmapManager beatmapManager { get; set; }

        [Resolved]
        private RulesetStore rulesetStore { get; set; }

        [Resolved(CanBeNull = true)]
        private IPerformFromScreenRunner screenRunner { get; set; }

        [Resolved(CanBeNull = true)]
        private INotificationOverlay notifications { get; set; }

        protected override LocalisableString Header => "Sticks";

        public SticksSettingsSubsection(SticksRuleset ruleset)
            : base(ruleset)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var config = (SticksRulesetConfigManager)Config;

            Children = new Drawable[]
            {
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Approach rate",
                    Current = config.GetBindable<float>(SticksRulesetSetting.ApproachRate),
                    KeyboardStep = 0.5f,
                    LabelFormat = value => $"AR {value:0.0} ({SticksHitObject.ApproachDurationFor(value):0} ms)",
                }),
                new SettingsButtonV2
                {
                    Text = "Create blank Sticks difficulty",
                    TooltipText = "Copy the selected osu!standard song's timing and resources, clear its objects, and open the Sticks editor.",
                    Keywords = new[] { "editor", "map", "compose", "blank", "timing" },
                    Action = () => openEditor(retainConvertedObjects: false),
                },
                new SettingsButtonV2
                {
                    Text = "Create editable converted Sticks difficulty",
                    TooltipText = "Convert the selected osu!standard objects, save them as editable Sticks objects, and open the Sticks editor.",
                    Keywords = new[] { "editor", "map", "compose", "convert" },
                    Action = () => openEditor(retainConvertedObjects: true),
                },
            };
        }

        private void openEditor(bool retainConvertedObjects)
        {
            if (screenRunner == null)
            {
                postError("The in-client Sticks editor is unavailable on this screen.");
                return;
            }

            screenRunner.PerformFromScreen(screen =>
            {
                try
                {
                    if (screen is not IOsuScreen osuScreen)
                        throw new InvalidOperationException("The current screen cannot provide a selected beatmap.");

                    RulesetInfo standardRuleset = rulesetStore.GetRuleset(0)
                                                  ?? throw new InvalidOperationException("osu!standard is unavailable.");
                    RulesetInfo sticksRuleset = rulesetStore.GetRuleset("sticks")
                                                ?? throw new InvalidOperationException("Sticks is unavailable.");
                    WorkingBeatmap created = SticksEditorBootstrap.CreateDifficulty(beatmapManager, osuScreen.Beatmap.Value, standardRuleset, sticksRuleset,
                                                                                   retainConvertedObjects);

                    osuScreen.Beatmap.Value = created;
                    osuScreen.Ruleset.Value = created.BeatmapInfo.Ruleset;
                    screen.Push(new EditorLoader());
                }
                catch (Exception exception)
                {
                    postError($"Could not create Sticks difficulty: {exception.Message}");
                }
            }, new[] { typeof(SongSelect), typeof(MainMenu) });
        }

        private void postError(string message) => notifications?.Post(new SimpleErrorNotification { Text = message });
    }
}
