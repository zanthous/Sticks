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

#if !STICKS_RULESET_API_2026_818
        protected override LocalisableString Header => "Sticks";
#endif

        public SticksSettingsSubsection(SticksRuleset ruleset)
            : base(ruleset)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var config = (SticksRulesetConfigManager)Config;
            var stackedNotePresentation = config.GetBindable<SticksStackedNotePresentation>(SticksRulesetSetting.StackedNotePresentation);
            var notePresentation = config.GetBindable<SticksNotePresentation>(SticksRulesetSetting.NotePresentation);
            if (notePresentation.Value is SticksNotePresentation.ApproachCircles or SticksNotePresentation.FillingArcs)
                notePresentation.Value = SticksNotePresentation.CenterOut;

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
            var contactEffects = new SettingsItemV2(new FormCheckBox
            {
                Caption = "Contact effects",
                HintText = "Show restrained contact feedback when hitting notes and tracking or completing sliders and holds in center-out mode.",
                Current = config.GetBindable<bool>(SticksRulesetSetting.SliderTrackingSparks),
            });
            var hideInactiveCursors = new SettingsItemV2(new FormCheckBox
            {
                Caption = "Hide inactive cursors",
                HintText = "In center-out mode, only show a cursor while held at least 90% outward or moving outward beyond 20%.",
                Current = config.GetBindable<bool>(SticksRulesetSetting.HideInactiveCursors),
            });
            var chordLinkPresentation = new SettingsItemV2(new FormEnumDropdown<SticksChordLinkPresentation>
            {
                Caption = "Synced-note links",
                Current = config.GetBindable<SticksChordLinkPresentation>(SticksRulesetSetting.ChordLinkPresentation),
            });
            var stackedNotePresentationSetting = new SettingsItemV2(new FormEnumDropdown<SticksStackedNotePresentation>
            {
                Caption = "Stacked note presentation",
                Current = stackedNotePresentation,
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
                new SettingsItemV2(new FormEnumDropdown<SticksNotePresentation>
                {
                    Caption = "Note presentation",
                    Current = notePresentation,
                    Items = new[]
                    {
                        SticksNotePresentation.CenterOut,
                        SticksNotePresentation.BracketMarkers,
                    },
                }),
                hideInactiveCursors,
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show cursor trails",
                    HintText = "Show a short continuous trail behind both stick cursors.",
                    Current = config.GetBindable<bool>(SticksRulesetSetting.ShowCursorTrails),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Disable beatmap hitsounds",
                    HintText = "For converted osu! beatmaps, ignore mapped hitsounds and use the default normal hit sound at full volume.",
                    Current = config.GetBindable<bool>(SticksRulesetSetting.DisableBeatmapHitsounds),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Save Sticks replays",
                    HintText = "Keep controller input for completed plays and failed plays you explicitly save. Scores are still saved when this is disabled.",
                    Current = config.GetBindable<bool>(SticksRulesetSetting.SaveReplays),
                }),
                new SettingsColour
                {
                    LabelText = "Left stick color",
                    TooltipText = "Color used by the left stick, its notes, and its duration paths.",
                    Current = config.GetBindable<Colour4>(SticksRulesetSetting.LeftStickColour),
                },
                new SettingsColour
                {
                    LabelText = "Right stick color",
                    TooltipText = "Color used by the right stick, its notes, and its duration paths.",
                    Current = config.GetBindable<Colour4>(SticksRulesetSetting.RightStickColour),
                },
                new SettingsColour
                {
                    LabelText = "Overlap color",
                    TooltipText = "Color used where simultaneous left and right objects overlap.",
                    Current = config.GetBindable<Colour4>(SticksRulesetSetting.OverlapColour),
                },
                contactEffects,
                chordLinkPresentation,
                stackedNotePresentationSetting,
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
                    Text = "Restore selected authored Sticks difficulty",
                    TooltipText = "Restore a Sticks difficulty which lazer's external-edit import changed to osu!standard. All authored Sticks data must still be intact.",
                    Keywords = new[] { "editor", "map", "restore", "recover", "external" },
                    Action = restoreAuthoredDifficulty,
                },
                new SettingsButtonV2
                {
                    Text = "Export selected Sticks difficulty (.osz)",
                    TooltipText = "Package the selected authored Sticks difficulty with its song and resources for another Sticks player.",
                    Keywords = new[] { "export", "share", "map", "beatmap", "osz" },
                    Action = exportSelectedDifficulty,
                },
            };

            void updateConditionalVisibility()
            {
                bool centerOut = notePresentation.Value == SticksNotePresentation.CenterOut;
                bool brackets = notePresentation.Value == SticksNotePresentation.BracketMarkers;
                bool showRadialApproachControls = brackets && stackedNotePresentation.Value == SticksStackedNotePresentation.RadialApproach;

                hideInactiveCursors.CanBeShown.Value = centerOut;
                contactEffects.CanBeShown.Value = centerOut;
                chordLinkPresentation.CanBeShown.Value = brackets;
                stackedNotePresentationSetting.CanBeShown.Value = brackets;
                radialApproachDistance.CanBeShown.Value = showRadialApproachControls;
                radialApproachSpeed.CanBeShown.Value = showRadialApproachControls;
            }

            stackedNotePresentation.BindValueChanged(_ => updateConditionalVisibility(), true);
            notePresentation.BindValueChanged(_ => updateConditionalVisibility(), true);
        }

        private void restoreAuthoredDifficulty()
        {
            if (screenRunner == null)
            {
                postError("Sticks beatmap recovery is unavailable on this screen.");
                return;
            }

            screenRunner.PerformFromScreen(screen =>
            {
                try
                {
                    if (screen is not IOsuScreen osuScreen)
                        throw new InvalidOperationException("The current screen cannot provide a selected beatmap.");

                    RulesetInfo sticksRuleset = rulesetStore.GetRuleset("sticks")
                                                ?? throw new InvalidOperationException("Sticks is unavailable.");
                    WorkingBeatmap restored = SticksEditorBootstrap.RestoreAuthoredDifficulty(beatmapManager, realmAccess, osuScreen.Beatmap.Value, sticksRuleset);

                    osuScreen.Beatmap.Value = restored;
                    osuScreen.Ruleset.Value = restored.BeatmapInfo.Ruleset;
                    screen.Push(new EditorLoader());
                }
                catch (Exception exception)
                {
                    postError($"Could not restore Sticks difficulty: {exception.Message}");
                }
            }, new[] { typeof(SongSelect), typeof(MainMenu) });
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
                    WorkingBeatmap created = SticksEditorBootstrap.CreateDifficulty(beatmapManager, realmAccess, osuScreen.Beatmap.Value, standardRuleset, sticksRuleset,
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
