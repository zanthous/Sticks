#nullable enable

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Compose.Components;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit
{
    public partial class SticksSelectionHandler : EditorSelectionHandler
    {
        [Resolved]
        private SticksHitObjectComposer? composer { get; set; }

        [Resolved]
        private EditorClock? editorClock { get; set; }

        protected override void Update()
        {
            base.Update();

            // A point outside the visible timeline has no handle to outline.
            if (SelectedBlueprints.Count > 0)
                SelectionBox.Alpha = SelectedBlueprints.Any(blueprint => blueprint is not SticksSelectionBlueprint sticks || sticks.HasVisibleSelection) ? 1 : 0;
        }

        public override bool HandleMovement(MoveSelectionEvent<HitObject> moveEvent)
        {
            SticksHitObject[] hitObjects = SelectedItems.OfType<SticksHitObject>().ToArray();
            if (hitObjects.Length == 0 || moveEvent.Blueprint.Item is not SticksHitObject reference)
                return false;

            Vector2 target = moveEvent.Blueprint.ScreenSpaceSelectionPoint + moveEvent.ScreenSpaceDelta;
            if (composer == null || !SticksEditorCoordinates.TryGetAngle(composer.Playfield.ToLocalSpace(target), out float targetAngle))
                return false;

            // Both hands now share a radius. Drag relative to the visible head/contact,
            // retaining the hand and the original path even partway through a slider.
            float referenceAngle = SticksEditorCoordinates.AngleAt(reference, editorClock?.CurrentTimeAccurate ?? reference.StartTime);
            float angleDelta = SticksHitObject.DeltaAngle(referenceAngle, targetAngle);
            foreach (SticksHitObject hitObject in hitObjects)
            {
                hitObject.Angle = SticksHitObject.NormaliseAngle(hitObject.Angle + angleDelta);
                EditorBeatmap.Update(hitObject);
            }

            return true;
        }

        protected override IEnumerable<MenuItem> GetContextMenuItemsForSelection(IEnumerable<SelectionBlueprint<HitObject>> selection)
        {
            yield return new OsuMenuItem("Swap sticks", MenuItemType.Standard, () =>
                EditorBeatmap.PerformOnSelection(hitObject =>
                {
                    if (hitObject is SticksHitObject sticks)
                        sticks.Side = sticks.Side == StickSide.Left ? StickSide.Right : StickSide.Left;
                }));

            foreach (MenuItem item in base.GetContextMenuItemsForSelection(selection))
                yield return item;
        }
    }
}
