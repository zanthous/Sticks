#nullable enable

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Compose.Components;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit
{
    public partial class SticksBlueprintContainer : ComposeBlueprintContainer
    {
        public new SticksHitObjectComposer Composer => (SticksHitObjectComposer)base.Composer;

        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; } = null!;

        private SticksSlider[]? pendingContinuation;

        public void ContinueSliderPlacement(SticksSlider[] targets) => pendingContinuation = targets;

        protected override void Update()
        {
            base.Update();
            if (pendingContinuation == null)
                return;

            if (pendingContinuation.Any(slider => !editorBeatmap.HitObjects.Contains(slider)))
            {
                pendingContinuation = null;
                return;
            }

            SticksSelectionBlueprint? blueprint = SelectionBlueprints.OfType<SticksSelectionBlueprint>()
                .FirstOrDefault(candidate => ReferenceEquals(candidate.Item, pendingContinuation[0]));
            if (blueprint == null || !blueprint.IsLoaded)
                return;

            editorBeatmap.SelectedHitObjects.Clear();
            editorBeatmap.SelectedHitObjects.AddRange(pendingContinuation);
            blueprint.BeginContinuationPlacement(pendingContinuation);
            pendingContinuation = null;
        }

        public SticksBlueprintContainer(SticksHitObjectComposer composer)
            : base(composer)
        {
        }

        protected override SelectionHandler<HitObject> CreateSelectionHandler() => new SticksSelectionHandler();

        public override HitObjectSelectionBlueprint? CreateHitObjectBlueprintFor(HitObject hitObject) =>
            hitObject is SticksHitObject sticks ? new SticksSelectionBlueprint(sticks) : null;

        protected override bool TryMoveBlueprints(DragEvent e, IList<(SelectionBlueprint<HitObject> blueprint, Vector2[] originalSnapPositions)> blueprints)
        {
            Vector2 distanceTravelled = e.ScreenSpaceMousePosition - e.ScreenSpaceMouseDownPosition;
            Vector2 movePosition = blueprints.First().originalSnapPositions.First() + distanceTravelled;
            SelectionBlueprint<HitObject> reference = blueprints.First().blueprint;

            Vector2 local = Composer.Playfield.ToLocalSpace(movePosition);
            if (e.ShiftPressed && SticksEditorCoordinates.TryGetAngle(local, out float angle))
            {
                angle = SticksEditorCoordinates.SnapAngle(angle);
                float radius = (local - SticksEditorCoordinates.Centre).Length;
                movePosition = Composer.Playfield.ToScreenSpace(UI.SticksPlayfield.PointAt(angle, radius));
            }

            return SelectionHandler.HandleMovement(new MoveSelectionEvent<HitObject>(
                reference,
                movePosition - reference.ScreenSpaceSelectionPoint));
        }
    }
}
