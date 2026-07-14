// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

#nullable enable

using System.Linq;
using osu.Framework.Allocation;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit.Compose.Components;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit
{
    public partial class SticksSelectionHandler : EditorSelectionHandler
    {
        [Resolved]
        private SticksHitObjectComposer? composer { get; set; }

        public override bool HandleMovement(MoveSelectionEvent<HitObject> moveEvent)
        {
            SticksHitObject[] hitObjects = SelectedItems.OfType<SticksHitObject>().ToArray();
            if (hitObjects.Length == 0 || moveEvent.Blueprint.Item is not SticksHitObject reference)
                return false;

            Vector2 target = moveEvent.Blueprint.ScreenSpaceSelectionPoint + moveEvent.ScreenSpaceDelta;
            if (composer?.TryGetPlacement(target, out StickSide targetSide, out float targetAngle) != true)
                return false;

            if (hitObjects.Length == 1)
            {
                reference.Side = targetSide;
                reference.Angle = targetAngle;
                EditorBeatmap.Update(reference);
                return true;
            }

            float angleDelta = SticksHitObject.DeltaAngle(reference.Angle, targetAngle);
            foreach (SticksHitObject hitObject in hitObjects)
            {
                hitObject.Angle = SticksHitObject.NormaliseAngle(hitObject.Angle + angleDelta);
                EditorBeatmap.Update(hitObject);
            }

            return true;
        }
    }
}
