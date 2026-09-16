#nullable enable

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Compose.Components;
using osuTK;

#if STICKS_RULESET_API_2026_818
using SticksCompositionTool = osu.Game.Rulesets.Edit.Tools.CompositionTool<osu.Game.Rulesets.Sticks.SticksAction>;
#else
using SticksCompositionTool = osu.Game.Rulesets.Edit.Tools.CompositionTool;
#endif

namespace osu.Game.Rulesets.Sticks.Edit
{
    [Cached]
    public partial class SticksHitObjectComposer :
#if STICKS_RULESET_API_2026_818
        HitObjectComposer<SticksHitObject, SticksAction>
#else
        HitObjectComposer<SticksHitObject>
#endif
    {
        internal double PlayerApproachDuration => ((DrawableSticksRuleset)DrawableRuleset).PlayerApproachDuration;

        internal readonly Bindable<int> SelectedSliderPoint = new Bindable<int>();

        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; } = null!;

        private bool selectionSubscribed;

        public SticksHitObjectComposer(SticksRuleset ruleset)
            : base(ruleset)
        {
        }

#if STICKS_RULESET_API_2026_818
        public override Bindable<TernaryState>? SelectionNewComboState => null;
#endif

        protected override IReadOnlyList<SticksCompositionTool> CompositionTools => new SticksCompositionTool[]
        {
            new SticksFlickCompositionTool(),
            new SticksSliderCompositionTool(),
            new SticksClickCompositionTool(),
        };

        public override bool CursorInPlacementArea => base.CursorInPlacementArea
            || SticksEditorCoordinates.TryGetPlacement(Playfield.ToLocalSpace(GetContainingInputManager().CurrentState.Mouse.Position), out _, out _);

        protected override ComposeBlueprintContainer CreateBlueprintContainer() => new SticksBlueprintContainer(this);

        protected override Drawable CreateHitObjectInspector() => new SticksHitObjectInspector();

        protected override void LoadComplete()
        {
            base.LoadComplete();
            AddInternal(new SticksTimelineMarkerAttachment());
            editorBeatmap.SelectedHitObjects.CollectionChanged += selectionChanged;
            selectionSubscribed = true;
        }

        private void selectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => SelectedSliderPoint.Value = 0;

        internal void SelectSliderPoint(SticksSlider slider, int point)
        {
            if (!editorBeatmap.SelectedHitObjects.Contains(slider)
                || !SticksInspectorEdits.CanEditSegments(editorBeatmap.SelectedHitObjects.OfType<SticksHitObject>().ToArray()))
            {
                editorBeatmap.SelectedHitObjects.Clear();
                editorBeatmap.SelectedHitObjects.Add(slider);
            }
            SelectedSliderPoint.Value = System.Math.Clamp(point, 0, slider.SegmentCount);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (selectionSubscribed)
                editorBeatmap.SelectedHitObjects.CollectionChanged -= selectionChanged;
            base.Dispose(isDisposing);
        }

        public void ContinueSliderPlacement(SticksSlider[] targets)
        {
            SetSelectTool();
            ((SticksBlueprintContainer)BlueprintContainer).ContinueSliderPlacement(targets);
        }

        public bool TryGetPlacement(Vector2 screenSpacePosition, out StickSide side, out float angle) =>
            SticksEditorCoordinates.TryGetPlacement(Playfield.ToLocalSpace(screenSpacePosition), out side, out angle);
    }
}
