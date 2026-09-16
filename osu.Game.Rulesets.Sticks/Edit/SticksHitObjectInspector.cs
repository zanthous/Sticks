#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Compose.Components;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Rulesets.Sticks.Edit
{
    // HitObjectInspector replaces its contents every 250ms. Editable controls need to retain
    // their focus and draft until the edited field is committed.
    public partial class SticksHitObjectInspector : EditorInspector
    {
        private readonly Dictionary<string, NumericField> fields = new Dictionary<string, NumericField>();
        private readonly List<Action> refreshReadouts = new List<Action>();
        private SticksHitObject[] targets = Array.Empty<SticksHitObject>();
        private readonly HashSet<SticksHitObject> targetSet = new HashSet<SticksHitObject>();
        private string[] originalStates = Array.Empty<string>();
        private FillFlowContainer content = null!;
        private StickDropdown? side;
        private StickChoice originalSide;
        private OsuTextFlowContainer errorText = null!;
        private OsuSpriteText selectionLabel = null!;
        private bool updatingControls;
        private int segmentFieldCount;
        private int inspectedPoint;
        private bool subscribed;

        [Resolved]
        private SticksHitObjectComposer composer { get; set; } = null!;

        [Resolved]
        private Bindable<WorkingBeatmap> workingBeatmap { get; set; } = null!;

        [Resolved]
        private IEditorChangeHandler changeHandler { get; set; } = null!;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            InternalChild = content = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 6),
            };
            EditorBeatmap.SelectedHitObjects.CollectionChanged += selectionChanged;
            EditorBeatmap.HitObjectUpdated += objectUpdated;
            EditorBeatmap.HitObjectAdded += objectUpdated;
            EditorBeatmap.HitObjectRemoved += objectUpdated;
            EditorBeatmap.TransactionEnded += queueRefresh;
            composer.SelectedSliderPoint.BindValueChanged(pointSelectionChanged);
            subscribed = true;
            rebuild();
        }

        private void selectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => queueRefresh();
        private void pointSelectionChanged(ValueChangedEvent<int> e) => queueRefresh();
        private void objectUpdated(HitObject hitObject)
        {
            if (hitObject is SticksHitObject)
                queueRefresh();
        }

        private void queueRefresh() => Scheduler.AddOnce(refresh);

        private SticksHitObject[] selected() => EditorBeatmap.SelectedHitObjects.OfType<SticksHitObject>().ToArray();

        private void refresh()
        {
            if (!targets.SequenceEqual(selected()) || editableSegmentCount() != segmentFieldCount
                || inspectedPoint != currentPoint())
            {
                rebuild();
                return;
            }
            if (stateMatches())
            {
                refreshStickControl();
                return;
            }

            // Keep in-progress text, but never let it overwrite a newer external edit.
            foreach (var field in fields.Values.Where(field => field.Changed))
                field.Stale = true;
            refreshValues();
        }

        private int editableSegmentCount() => SticksInspectorEdits.CanEditSegments(targets)
            ? ((SticksSlider)targets[0]).SegmentCount : 0;

        private int currentPoint() => Math.Clamp(composer.SelectedSliderPoint.Value, 0, editableSegmentCount());

        private void refreshValues()
        {
            updatingControls = true;
            try
            {
                foreach (var field in fields.Values.Where(field => !field.Changed))
                    field.Restore();
                foreach (var refreshReadout in refreshReadouts)
                    refreshReadout();
                refreshStickControl();
                originalStates = targets.Select(stateOf).ToArray();
            }
            finally
            {
                updatingControls = false;
            }
        }

        private bool stateMatches() => targets.Select(stateOf).SequenceEqual(originalStates);

        private static string stateOf(SticksHitObject note)
        {
            string state = FormattableString.Invariant($"{note.Side}|{note.Angle:R}|{note.SizeMultiplier:R}|{note.StartTime:R}|{(note as IHasDuration)?.Duration:R}");
            if (note is SticksSlice slice)
                state += "|" + slice.Direction;
            if (note is SticksSlider slider)
                state += "|" + string.Join(";", Enumerable.Range(0, slider.SegmentCount).Select(i =>
                    FormattableString.Invariant($"{slider.SegmentArcAngleAt(i):R}/{slider.SegmentDurationAt(i):R}/{slider.NodeSizeAt(i):R}")))
                    + FormattableString.Invariant($"|{slider.NodeSizeAt(slider.SegmentCount):R}");
            return state;
        }

        private void rebuild()
        {
            updatingControls = true;
            try
            {
                rebuildControls();
            }
            finally
            {
                updatingControls = false;
            }
        }

        private void rebuildControls()
        {
            // Focus loss drains queued text/IME input. Do it while the old textbox's
            // character drawables still exist, before Clear() disposes its contents.
            // updatingControls prevents that obsolete draft from being committed.
            if (fields.Values.Any(field => field.Box.HasFocus))
                GetContainingFocusManager()?.ChangeFocus(null);

            targets = selected();
            targetSet.Clear();
            targetSet.UnionWith(targets);
            originalStates = targets.Select(stateOf).ToArray();
            segmentFieldCount = editableSegmentCount();
            inspectedPoint = currentPoint();
            composer.SelectedSliderPoint.Value = inspectedPoint;
            fields.Clear();
            refreshReadouts.Clear();
            side = null;
            content.Clear();
            if (targets.Length == 0)
            {
                content.Add(label("Select notes to edit."));
                return;
            }

            content.Add(selectionLabel = label(targets.Length == 1 ? "Note properties" : $"{targets.Length} selected notes"));
            content.Add(label("Stick"));
            content.Add(side = new StickDropdown
            {
                Name = "Stick",
                RelativeSizeAxes = Axes.X,
            });
            refreshStickControl();
            side.Current.BindValueChanged(_ => commitSide());

            bool sliders = targets.All(note => note is SticksSlider);
            if (sliders)
            {
                var pointSelector = new SliderPointDropdown
                {
                    Name = "Slider point",
                    RelativeSizeAxes = Axes.X,
                    Items = Enumerable.Range(0, segmentFieldCount + 1),
                };
                pointSelector.Current.Value = inspectedPoint;
                pointSelector.Current.BindValueChanged(value =>
                {
                    if (!updatingControls)
                        composer.SelectedSliderPoint.Value = value.NewValue;
                });
                content.Add(pointSelector);
                if (segmentFieldCount == 0)
                    content.Add(label("Select one slider or matching sliders to edit points."));
            }

            if (inspectedPoint == 0 && targets.All(note => note is not SticksClick))
                addNumber(content, "Angle", "Angle (°)", () => common(note => SticksHitObject.NormaliseAngle(note.Angle)));
            if (sliders)
            {
                int point = inspectedPoint;
                addNumber(content, $"Point {point} size", "Size (×)", () => common(note => ((SticksSlider)note).NodeSizeAt(point)));
            }
            else if (targets.All(note => note is not SticksClick and not SticksSlice))
                addNumber(content, "Size", "Size (×)", () => common(note => note.SizeMultiplier));
            if (inspectedPoint == 0)
                addNumber(content, "Start time", "Start time (ms)", () => common(note => note.StartTime));
            if (targets.All(note => note is SticksSlice))
            {
                content.Add(label("Slice direction"));
                var direction = new SliceDirectionDropdown
                {
                    RelativeSizeAxes = Axes.X,
                    Items = Enum.GetValues<SticksSliceDirection>().Cast<SticksSliceDirection?>(),
                };
                void refreshDirection()
                {
                    SticksSliceDirection[] directions = targets.Cast<SticksSlice>().Select(note => note.Direction).Distinct().ToArray();
                    direction.Current.Value = directions.Length == 1 ? directions[0] : null;
                }
                refreshDirection();
                refreshReadouts.Add(refreshDirection);
                direction.Current.BindValueChanged(value =>
                {
                    if (updatingControls || !value.NewValue.HasValue)
                        return;
                    bool changed = commit(new SticksInspectorEdit { SliceDirection = value.NewValue }, out string error);
                    refreshValues();
                    showError(changed ? string.Empty : error);
                });
                content.Add(direction);
            }

            if (sliders && inspectedPoint < segmentFieldCount)
            {
                int segment = inspectedPoint;
                content.Add(label(inspectedPoint == 0 ? "First segment" : $"Segment from point {inspectedPoint}"));
                addNumber(content, $"Segment {segment + 1} angle", "Turn (°)", () => common(note => ((SticksSlider)note).SegmentArcAngleAt(segment)));
                addNumber(content, $"Segment {segment + 1} duration", "Duration (ms)", () => common(note => ((SticksSlider)note).SegmentDurationAt(segment)));
                addNumber(content, $"Segment {segment + 1} speed", "Speed (°/s)", () => common(note =>
                {
                    var slider = (SticksSlider)note;
                    return slider.SegmentArcAngleAt(segment) / slider.SegmentDurationAt(segment) * 1000;
                }));
            }

            content.Add(errorText = new OsuTextFlowContainer(text => text.Font = OsuFont.GetFont(size: 13))
            {
                Name = "Validation error",
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Colour = Color4.Salmon,
            });
        }

        private static OsuSpriteText label(string text) => new OsuSpriteText
        {
            Text = text,
            Font = OsuFont.GetFont(size: 13),
        };

        private double? common(Func<SticksHitObject, double> value)
        {
            double first = value(targets[0]);
            return targets.All(note => value(note).Equals(first)) ? first : null;
        }

        private void addNumber(FillFlowContainer parent, string name, string caption, Func<double?> readValue)
        {
            parent.Add(label(caption));
            var box = new InspectorNumberBox
            {
                Name = name,
                RelativeSizeAxes = Axes.X,
                Height = 25,
                SelectAllOnFocus = true,
                CommitOnFocusLost = true,
                ReleaseFocusOnCommit = false,
            };
            var field = new NumericField(box, readValue);
            fields.Add(name, field);
            box.OnCommit += (_, _) => commitField(name, field);
            box.CancelEdit = () =>
            {
                if (updatingControls || !fields.TryGetValue(name, out var current) || current != field)
                    return;
                field.Restore();
                showError(string.Empty);
            };
            parent.Add(box);
        }

        private void showError(string error) => errorText.Text = error;

        private void commitField(string name, NumericField field)
        {
            if (updatingControls || !fields.TryGetValue(name, out var current) || current != field || !field.Changed)
                return;
            if (field.Stale || !stateMatches())
            {
                refresh();
                if (!fields.TryGetValue(name, out current) || current != field)
                    return;
                field.Restore();
                showError("The note changed. Its current value has been restored.");
                return;
            }
            if (!double.TryParse(field.Box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            {
                field.Restore();
                showError($"{name} needs a finite number.");
                return;
            }

            var values = new Dictionary<string, double> { [name] = value };
            var edit = editFrom(values);
            bool committed = commit(edit, out string error);
            field.Restore();
            refreshValues();
            showError(committed ? string.Empty : error);
        }

        private void refreshStickControl()
        {
            if (side == null)
                return;

            bool wasUpdating = updatingControls;
            updatingControls = true;
            try
            {
                originalSide = targets.All(note => note.Side == targets[0].Side)
                    ? choiceFor(targets[0].Side)
                    : SticksInspectorStickEdits.IsBoth(targets) ? StickChoice.Both : StickChoice.Mixed;
                bool canUseBoth = SticksInspectorStickEdits.CanUseBoth(targets,
                    EditorBeatmap.HitObjects.OfType<SticksHitObject>().ToArray(), out _);
                var choices = canUseBoth
                    ? new[] { StickChoice.Left, StickChoice.Right, StickChoice.Both }
                    : new[] { StickChoice.Left, StickChoice.Right };
                if (!side.Items.SequenceEqual(choices))
                    side.Items = choices;
                // Mixed describes this selection; it is never an action in the menu.
                side.Current.Value = originalSide;
            }
            finally
            {
                updatingControls = wasUpdating;
            }
        }

        private void commitSide()
        {
            if (updatingControls || side == null || side.Current.Value == originalSide || side.Current.Value == StickChoice.Mixed)
                return;
            if (!targets.SequenceEqual(selected()))
            {
                refresh();
                return;
            }

            if (!stateMatches())
            {
                foreach (var field in fields.Values.Where(field => field.Changed))
                    field.Stale = true;
            }

            StickSide? choice = side.Current.Value switch
            {
                StickChoice.Left => StickSide.Left,
                StickChoice.Right => StickSide.Right,
                _ => null,
            };
            var allNotes = EditorBeatmap.HitObjects.OfType<SticksHitObject>().ToArray();
            if (!SticksInspectorStickEdits.TryPrepare(targets, allNotes, choice, out var plan, out string error))
            {
                refreshValues();
                showError(error);
                return;
            }

            double? audioLength = workingBeatmap.Value.TrackLoaded && workingBeatmap.Value.Track is not TrackVirtual
                ? workingBeatmap.Value.Track.Length : null;
            if (plan.Additions.Length > 0
                && (!SticksInspectorEdits.TryPrepare(plan.Additions, new SticksInspectorEdit(), audioLength, out var additions, out error)
                    || !SticksInspectorEdits.ValidateCheckpointBudget(additions, EditorBeatmap.ControlPointInfo,
                        EditorBeatmap.Difficulty.SliderTickRate, out error)))
            {
                refreshValues();
                showError(error);
                return;
            }

            foreach (var note in plan.Additions)
            {
                note.EnsureLegacyEditorMarker();
                note.ApplyDefaults(EditorBeatmap.ControlPointInfo, EditorBeatmap.Difficulty);
            }

            // Adding/removing the opposite hand and updating selection form one undo step.
            // Text drafts in other fields are not submitted by a stick choice.
            changeHandler.BeginChange();
            try
            {
                foreach (var note in plan.Removals)
                    EditorBeatmap.Remove(note);
                foreach (var update in plan.Updates)
                {
                    update.Apply();
                    EditorBeatmap.Update(update.Target);
                }
                foreach (var note in plan.Additions)
                    EditorBeatmap.Add(note);
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.AddRange(plan.Selection);
            }
            finally
            {
                changeHandler.EndChange();
            }

            targets = plan.Selection;
            targetSet.Clear();
            targetSet.UnionWith(targets);
            selectionLabel.Text = targets.Length == 1 ? "Note properties" : $"{targets.Length} selected notes";
            if (editableSegmentCount() != segmentFieldCount || inspectedPoint != currentPoint())
                rebuild();
            else
                refreshValues();
            showError(string.Empty);
        }

        private static SticksInspectorEdit editFrom(IReadOnlyDictionary<string, double> values)
        {
            double? read(string name) => values.TryGetValue(name, out double value) ? value : null;
            var segmentAngles = new Dictionary<int, double>();
            var segmentDurations = new Dictionary<int, double>();
            var segmentSpeeds = new Dictionary<int, double>();
            var nodeSizes = new Dictionary<int, double>();
            for (int i = 0; i <= SticksSlider.MAX_SEGMENT_COUNT; i++)
            {
                if (read($"Point {i} size") is double size)
                    nodeSizes.Add(i, size);
            }
            for (int i = 0; i < SticksSlider.MAX_SEGMENT_COUNT; i++)
            {
                if (read($"Segment {i + 1} angle") is double angle)
                    segmentAngles.Add(i, angle);
                if (read($"Segment {i + 1} duration") is double duration)
                    segmentDurations.Add(i, duration);
                if (read($"Segment {i + 1} speed") is double speed)
                    segmentSpeeds.Add(i, speed);
            }

            return new SticksInspectorEdit
            {
                Angle = read("Angle"),
                SizeMultiplier = read("Size"),
                StartTime = read("Start time"),
                EndTime = read("End time"),
                Duration = read("Duration"),
                SegmentAngles = segmentAngles,
                SegmentDurations = segmentDurations,
                SegmentSpeeds = segmentSpeeds,
                NodeSizes = nodeSizes,
            };
        }

        private bool commit(SticksInspectorEdit edit, out string error)
        {
            // Focus can move onto another note before focus-lost is delivered. These targets
            // belong to the field that was edited, never the newly selected note.
            var currentObjects = EditorBeatmap.HitObjects.ToHashSet();
            if (targets.Any(note => !currentObjects.Contains(note)))
            {
                error = "The edited note no longer exists.";
                queueRefresh();
                return false;
            }
            double? audioLength = workingBeatmap.Value.TrackLoaded && workingBeatmap.Value.Track is not TrackVirtual
                ? workingBeatmap.Value.Track.Length : null;
            if (!SticksInspectorEdits.TryPrepare(targets, edit, audioLength, out var plans, out error))
                return false;
            if (!SticksInspectorEdits.ValidateCheckpointBudget(plans, EditorBeatmap.ControlPointInfo, EditorBeatmap.Difficulty.SliderTickRate, out error))
                return false;
            if (introducesCollision(plans))
            {
                error = "This would put two notes on the same stick at the same time.";
                return false;
            }

            changeHandler.BeginChange();
            try
            {
                foreach (var plan in plans)
                {
                    plan.Apply();
                    EditorBeatmap.Update(plan.Target);
                }
            }
            finally
            {
                changeHandler.EndChange();
            }
            return true;
        }

        private bool introducesCollision(SticksInspectorEditPlan[] plans)
        {
            if (plans.All(plan => plan.Side == plan.Target.Side && plan.StartTime == plan.Target.StartTime))
                return false;

            var others = EditorBeatmap.HitObjects.OfType<SticksHitObject>().Where(note => !targetSet.Contains(note))
                                      .GroupBy(note => (note.Side, Click: note is SticksClick))
                                      .ToDictionary(group => group.Key, group => group.OrderBy(note => note.StartTime).ToArray());
            foreach (var plan in plans)
            {
                if (plan.Side == plan.Target.Side && plan.StartTime == plan.Target.StartTime)
                    continue;
                if (!others.TryGetValue((plan.Side, plan.Target is SticksClick), out var group))
                    continue;
                int low = 0;
                int high = group.Length;
                while (low < high)
                {
                    int mid = (low + high) / 2;
                    if (group[mid].StartTime < plan.StartTime - 0.5)
                        low = mid + 1;
                    else
                        high = mid;
                }
                for (int i = low; i < group.Length && group[i].StartTime <= plan.StartTime + 0.5; i++)
                {
                    if (plan.Target.Side != group[i].Side || Math.Abs(plan.Target.StartTime - group[i].StartTime) > 0.5)
                        return true;
                }
            }
            return false;
        }

        protected override void Dispose(bool isDisposing)
        {
            updatingControls = true;
            if (subscribed)
            {
                EditorBeatmap.SelectedHitObjects.CollectionChanged -= selectionChanged;
                EditorBeatmap.HitObjectUpdated -= objectUpdated;
                EditorBeatmap.HitObjectAdded -= objectUpdated;
                EditorBeatmap.HitObjectRemoved -= objectUpdated;
                EditorBeatmap.TransactionEnded -= queueRefresh;
                composer.SelectedSliderPoint.ValueChanged -= pointSelectionChanged;
            }
            base.Dispose(isDisposing);
        }

        internal enum StickChoice
        {
            Mixed,
            Left,
            Right,
            Both,
        }

        private static StickChoice choiceFor(StickSide stick) => stick == StickSide.Left ? StickChoice.Left : StickChoice.Right;

        private partial class SliceDirectionDropdown : OsuDropdown<SticksSliceDirection?>
        {
            protected override LocalisableString GenerateItemText(SticksSliceDirection? item) => item switch
            {
                SticksSliceDirection.Neutral => "Either direction",
                SticksSliceDirection.Clockwise => "Clockwise",
                SticksSliceDirection.Counterclockwise => "Counterclockwise",
                _ => "Mixed",
            };
        }

        private partial class StickDropdown : OsuDropdown<StickChoice>
        {
            protected override LocalisableString GenerateItemText(StickChoice item) =>
                item.ToString();
        }

        private partial class SliderPointDropdown : OsuDropdown<int>
        {
            protected override LocalisableString GenerateItemText(int item) => item == 0 ? "Head" : $"Point {item}";
        }

        private partial class InspectorNumberBox : OsuTextBox
        {
            public Action? CancelEdit { get; set; }

            protected override bool OnKeyDown(KeyDownEvent e)
            {
                if (e.Key == Key.Escape)
                {
                    CancelEdit?.Invoke();
                    return true;
                }
                return base.OnKeyDown(e);
            }
        }

        private sealed class NumericField
        {
            public OsuTextBox Box { get; }
            private readonly Func<double?> readValue;
            private string original = string.Empty;
            public bool Changed => Box.Text != original;
            public bool Stale { get; set; }

            public NumericField(OsuTextBox box, Func<double?> readValue)
            {
                Box = box;
                this.readValue = readValue;
                Restore();
            }

            public void Restore()
            {
                double? value = readValue();
                original = value?.ToString("0.######", CultureInfo.InvariantCulture) ?? string.Empty;
                Box.Text = original;
                Box.PlaceholderText = value.HasValue ? string.Empty : "Mixed";
                Stale = false;
            }
        }
    }
}
