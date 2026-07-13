// Copyright (c) Zanthous. Licensed under the MIT Licence.

#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    public partial class SticksHoldPlacementBlueprint : SticksPlacementBlueprint<SticksHold>
    {
        [Resolved]
        private IBeatSnapProvider? beatSnapProvider { get; set; }

        private double spatialDuration;
        private double clockDuration;

        public SticksHoldPlacementBlueprint()
            : base(new SticksHold())
        {
        }

        protected override bool IsValidForPlacement => base.IsValidForPlacement
                                                       && (PlacementActive == PlacementState.Waiting || HitObject.Duration > 0);

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (PlacementActive == PlacementState.Waiting && e.Button == MouseButton.Left && HasValidPosition)
            {
                BeginPlacement(true);
                spatialDuration = clockDuration = minimumDuration();
                updateDuration();
                return true;
            }

            if (PlacementActive == PlacementState.Active && e.Button == MouseButton.Right)
            {
                EndPlacement(true);
                return true;
            }

            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (PlacementActive == PlacementState.Active && e.Button == MouseButton.Left)
                EndPlacement(true);

            base.OnMouseUp(e);
        }

        protected override void ActivePointerMoved(Vector2 localPosition, float angle, bool isInLane)
        {
            float radians = HitObject.Angle * MathF.PI / 180;
            Vector2 radial = new Vector2(MathF.Cos(radians), MathF.Sin(radians));
            float pointerRadius = Vector2.Dot(localPosition - SticksEditorCoordinates.Centre, radial);
            float laneRadius = SticksPlayfield.RadiusFor(HitObject.Side);
            float railLength = HitObject.Side == StickSide.Left
                ? pointerRadius - laneRadius
                : laneRadius - pointerRadius;

            if (railLength <= 0)
                return;

            double step = minimumDuration();
            spatialDuration = Math.Max(step, Math.Round(railLength / 0.06 / step) * step);
            updateDuration();
        }

        protected override void TimeUpdated(double time)
        {
            if (PlacementActive == PlacementState.Active)
            {
                clockDuration = Math.Max(minimumDuration(), time - HitObject.StartTime);
                updateDuration();
            }
        }

        private void updateDuration() => HitObject.Duration = Math.Max(spatialDuration, clockDuration);

        private double minimumDuration() => beatSnapProvider?.GetBeatLengthAtTime(HitObject.StartTime) ?? 100;
    }
}
