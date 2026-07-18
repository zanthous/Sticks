// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
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

        [Resolved]
        private Storage storage { get; set; }

        [Resolved]
        private RealmAccess realmAccess { get; set; }

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
            var stackedNotePresentation = config.GetBindable<SticksStackedNotePresentation>(SticksRulesetSetting.StackedNotePresentation);
            var radialApproachDistance = new SettingsItemV2(new FormSliderBar<float>
            {
                Caption = "Radial approach distance",
                Current = config.GetBindable<float>(SticksRulesetSetting.RadialApproachDistance),
                KeyboardStep = 1,
                LabelFormat = value => $"{value:0}",
            });
            var radialApproachSpeed = new SettingsItemV2(new FormSliderBar<float>
            {
                Caption = "Radial approach speed",
                Current = config.GetBindable<float>(SticksRulesetSetting.RadialApproachSpeed),
                KeyboardStep = 0.05f,
                LabelFormat = value => $"{value:0.00}x",
            });

            Children = new Drawable[]
            {
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Approach rate",
                    Current = config.GetBindable<float>(SticksRulesetSetting.ApproachRate),
                    KeyboardStep = 0.5f,
                    LabelFormat = value => $"AR {value:0.0} ({SticksHitObject.ApproachDurationFor(value):0} ms)",
                }),
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Flick activation",
                    Current = config.GetBindable<float>(SticksRulesetSetting.FlickActivationThreshold),
                    KeyboardStep = 0.01f,
                    LabelFormat = value =>
                        $"{value * 100:0}% (recharge at {SticksInputTracker.RechargeThresholdFor(value) * 100:0}%)",
                }),
                new SettingsItemV2(new FormEnumDropdown<SticksChordLinkPresentation>
                {
                    Caption = "Synced-note links",
                    Current = config.GetBindable<SticksChordLinkPresentation>(SticksRulesetSetting.ChordLinkPresentation),
                }),
                new SettingsItemV2(new FormEnumDropdown<SticksStackedNotePresentation>
                {
                    Caption = "Stacked note presentation",
                    Current = stackedNotePresentation,
                }),
                radialApproachDistance,
                radialApproachSpeed,
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
                new SettingsButtonV2
                {
                    Text = "Export selected Sticks difficulty (.osz)",
                    TooltipText = "Package the selected authored Sticks difficulty with its song and resources for another Sticks player.",
                    Keywords = new[] { "export", "share", "map", "beatmap", "osz" },
                    Action = exportSelectedDifficulty,
                },
            };

            stackedNotePresentation.BindValueChanged(presentation =>
            {
                bool showRadialApproachControls = presentation.NewValue == SticksStackedNotePresentation.RadialApproach;
                radialApproachDistance.CanBeShown.Value = showRadialApproachControls;
                radialApproachSpeed.CanBeShown.Value = showRadialApproachControls;
            }, true);
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

        private void exportSelectedDifficulty()
        {
            if (screenRunner == null)
            {
                postError("Sticks beatmap export is unavailable on this screen.");
                return;
            }

            screenRunner.PerformFromScreen(screen =>
            {
                try
                {
                    if (screen is not IOsuScreen osuScreen)
                        throw new InvalidOperationException("The current screen cannot provide a selected beatmap.");

                    WorkingBeatmap selected = osuScreen.Beatmap.Value;
                    if (selected.BeatmapInfo.Ruleset.ShortName != "sticks")
                        throw new InvalidOperationException("Select an authored Sticks difficulty first.");

                    BeatmapInfo persisted = beatmapManager.QueryBeatmap(info => info.ID == selected.BeatmapInfo.ID)
                                            ?? throw new InvalidOperationException("The selected difficulty is not available in the local database.");
                    BeatmapSetInfo set = persisted.BeatmapSet
                                         ?? throw new InvalidOperationException("The selected difficulty has no beatmap set.");

                    var exporter = new SticksBeatmapPackageExporter(storage, persisted.ID)
                    {
                        PostNotification = notification => notifications?.Post(notification),
                    };

                    exporter.ExportAsync(set.ToLive(realmAccess)).ContinueWith(task =>
                    {
                        Exception exception = task.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown export failure.");
                        Schedule(() => postError($"Could not export Sticks difficulty: {exception.Message}"));
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
                catch (Exception exception)
                {
                    postError($"Could not export Sticks difficulty: {exception.Message}");
                }
            }, new[] { typeof(SongSelect), typeof(MainMenu) });
        }

        private void postError(string message) => notifications?.Post(new SimpleErrorNotification { Text = message });
    }
}
