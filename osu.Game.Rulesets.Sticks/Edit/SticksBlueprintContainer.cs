// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;
using osu.Game.Rulesets.Sticks.Edit.Components;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit.Compose.Components;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit
{
    public partial class SticksBlueprintContainer : ComposeBlueprintContainer
    {
        public new SticksHitObjectComposer Composer => (SticksHitObjectComposer)base.Composer;

        public SticksBlueprintContainer(SticksHitObjectComposer composer)
            : base(composer)
        {
            AddInternal(new SticksEditorGuide());
        }

        protected override Drawable? CreateNewComboButton() => null;

        protected override IEnumerable<Drawable> CreateTernaryButtons()
        {
            yield break;
        }

        protected override SelectionHandler<HitObject> CreateSelectionHandler() => new SticksSelectionHandler();

        public override HitObjectSelectionBlueprint? CreateHitObjectBlueprintFor(HitObject hitObject) =>
            hitObject is SticksHitObject sticks ? new SticksSelectionBlueprint(sticks) : null;

        protected override bool TryMoveBlueprints(DragEvent e, IList<(SelectionBlueprint<HitObject> blueprint, Vector2[] originalSnapPositions)> blueprints)
        {
            Vector2 distanceTravelled = e.ScreenSpaceMousePosition - e.ScreenSpaceMouseDownPosition;
            Vector2 movePosition = blueprints.First().originalSnapPositions.First() + distanceTravelled;
            SelectionBlueprint<HitObject> reference = blueprints.First().blueprint;

            if (e.ShiftPressed && Composer.TryGetPlacement(movePosition, out StickSide side, out float angle))
            {
                angle = SticksEditorCoordinates.SnapAngle(angle);
                movePosition = Composer.Playfield.ToScreenSpace(SticksEditorCoordinates.PositionFor(side, angle));
            }

            return SelectionHandler.HandleMovement(new MoveSelectionEvent<HitObject>(
                reference,
                movePosition - reference.ScreenSpaceSelectionPoint));
        }
    }
}
