#nullable enable

using osu.Framework.Allocation;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    public partial class SticksSliderPlacementBlueprint : SticksPlacementBlueprint<SticksSlider>
    {
        [Resolved]
        private SticksHitObjectComposer composer { get; set; } = null!;

        [Resolved]
        private EditorClock editorClock { get; set; } = null!;

        private SticksSliderPlacementGesture? gesture;

        public SticksSliderPlacementBlueprint()
            : base(new SticksSlider())
        {
        }

        protected override bool IsValidForPlacement => base.IsValidForPlacement
                                                       && (PlacementActive == PlacementState.Waiting
                                                           || gesture?.CanCommit == true);

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (PlacementActive == PlacementState.Waiting && e.Button == MouseButton.Left && HasValidPosition)
            {
                BeginPlacement(true);
                gesture = new SticksSliderPlacementGesture(HitObject.Angle, HitObject.StartTime, editorClock.CurrentTimeAccurate);
                HitObject.Duration = 0;
                HitObject.ArcAngle = 0;
                return true;
            }

            if (PlacementActive == PlacementState.Active && (e.Button == MouseButton.Left || e.Button == MouseButton.Right))
            {
                updateDuration();
                if (gesture?.CanCommit != true)
                    return true;

                EndPlacement(true);
                if (e.Button == MouseButton.Right)
                    composer.ContinueSliderPlacement(PlacedPartner == null ? new[] { HitObject } : new[] { HitObject, PlacedPartner });

                return true;
            }

            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (PlacementActive == PlacementState.Active && e.Button == MouseButton.Left)
            {
                updateDuration();
                if (gesture?.CanCommit == true)
                    EndPlacement(true);
            }

            base.OnMouseUp(e);
        }

        protected override void ActivePointerMoved(Vector2 localPosition, float angle, bool isInLane)
        {
            if (gesture == null)
                return;

            gesture.UpdatePointer(angle, isInLane, ShiftPressed);
            HitObject.ArcAngle = gesture.Arc;
        }

        protected override void TimeUpdated(double time)
        {
            if (PlacementActive == PlacementState.Active)
                updateDuration();
        }

        private void updateDuration()
        {
            if (gesture == null)
                return;

            gesture.UpdateTime(editorClock.CurrentTimeAccurate);
            HitObject.Duration = gesture.Duration;
        }
    }
}
