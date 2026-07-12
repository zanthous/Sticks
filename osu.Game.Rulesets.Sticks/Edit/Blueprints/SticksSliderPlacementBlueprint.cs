// Copyright (c) Zanthous. Licensed under the MIT Licence.

#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    public partial class SticksSliderPlacementBlueprint : SticksPlacementBlueprint<SticksSlider>
    {
        [Resolved]
        private IBeatSnapProvider? beatSnapProvider { get; set; }

        private float lastPointerAngle;
        private bool hasLastPointerAngle;
        private double arcDuration;
        private double clockDuration;

        public SticksSliderPlacementBlueprint()
            : base(new SticksSlider())
        {
        }

        protected override bool IsValidForPlacement => base.IsValidForPlacement
                                                       && (PlacementActive == PlacementState.Waiting
                                                           || HitObject.Duration > 0 && Math.Abs(HitObject.ArcAngle) >= 1);

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (PlacementActive == PlacementState.Waiting && e.Button == MouseButton.Left && HasValidPosition)
            {
                BeginPlacement(true);
                arcDuration = clockDuration = minimumDuration();
                updateDuration();
                HitObject.ArcAngle = 0;
                lastPointerAngle = HitObject.Angle;
                hasLastPointerAngle = true;
                return true;
            }

            if (PlacementActive == PlacementState.Active && e.Button == MouseButton.Left && Math.Abs(HitObject.ArcAngle) >= 1)
            {
                EndPlacement(true);
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
            if (PlacementActive == PlacementState.Active && e.Button == MouseButton.Left && Math.Abs(HitObject.ArcAngle) >= 1)
                EndPlacement(true);

            base.OnMouseUp(e);
        }

        protected override void ActivePointerMoved(Vector2 localPosition, float angle, bool isInLane)
        {
            if (!isInLane)
                return;

            if (!hasLastPointerAngle)
            {
                lastPointerAngle = angle;
                hasLastPointerAngle = true;
                return;
            }

            HitObject.ArcAngle += SticksHitObject.DeltaAngle(lastPointerAngle, angle);
            lastPointerAngle = angle;

            double step = minimumDuration();
            arcDuration = Math.Max(step, Math.Ceiling(Math.Abs(HitObject.ArcAngle) / 90) * step);
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

        protected override bool OnScroll(ScrollEvent e)
        {
            if (PlacementActive != PlacementState.Active || e.ScrollDelta.Y == 0)
                return base.OnScroll(e);

            if (e.ControlPressed)
            {
                arcDuration = Math.Max(minimumDuration(), arcDuration + Math.Sign(e.ScrollDelta.Y) * minimumDuration());
                updateDuration();
            }
            else if (e.ShiftPressed)
            {
                HitObject.RepeatCount = Math.Clamp(HitObject.RepeatCount + Math.Sign(e.ScrollDelta.Y), 0, 16);
            }
            else
            {
                return base.OnScroll(e);
            }

            return true;
        }

        private void updateDuration() => HitObject.Duration = Math.Max(arcDuration, clockDuration);

        private double minimumDuration() => beatSnapProvider?.GetBeatLengthAtTime(HitObject.StartTime) ?? 100;
    }
}
