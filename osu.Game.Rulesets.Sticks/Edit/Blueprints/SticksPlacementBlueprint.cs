// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using osu.Framework.Input;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    public abstract partial class SticksPlacementBlueprint<T> : HitObjectPlacementBlueprint
        where T : SticksHitObject
    {
        public new T HitObject => (T)base.HitObject;

        protected readonly SticksBlueprintPiece Piece;

        protected bool HasValidPosition { get; private set; }

        protected float CurrentPointerAngle { get; private set; }

        private InputManager inputManager;

        protected bool ShiftPressed => inputManager?.CurrentState.Keyboard.ShiftPressed == true;

        protected override bool IsValidForPlacement => base.IsValidForPlacement && HasValidPosition;

        protected SticksPlacementBlueprint(T hitObject)
            : base(hitObject)
        {
            InternalChild = Piece = new SticksBlueprintPiece();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            inputManager = GetContainingInputManager();
            BeginPlacement();
        }

        protected override void Update()
        {
            base.Update();
            Piece.UpdateFrom(HitObject);
        }

        public override SnapResult UpdateTimeAndPosition(Vector2 screenSpacePosition, double fallbackTime)
        {
            Vector2 localPosition = ToLocalSpace(screenSpacePosition);
            bool validPointer = SticksEditorCoordinates.TryGetPlacement(localPosition, out StickSide side, out float angle);

            if (PlacementActive == PlacementState.Waiting)
            {
                if (validPointer && ShiftPressed)
                    angle = SticksEditorCoordinates.SnapAngle(angle);

                CurrentPointerAngle = angle;
                HasValidPosition = validPointer;
                if (validPointer)
                {
                    HitObject.Side = side;
                    HitObject.Angle = angle;
                    screenSpacePosition = ToScreenSpace(SticksEditorCoordinates.PositionFor(side, angle));
                }
            }
            else
            {
                CurrentPointerAngle = angle;
                ActivePointerMoved(localPosition, angle, validPointer);
            }

            SnapResult result = base.UpdateTimeAndPosition(screenSpacePosition, fallbackTime);
            TimeUpdated(result.Time ?? fallbackTime);
            return result;
        }

        protected virtual void ActivePointerMoved(Vector2 localPosition, float angle, bool isInLane)
        {
        }

        protected virtual void TimeUpdated(double time)
        {
        }

        public override bool ReplacesExistingObject(HitObject existing) =>
            existing is SticksHitObject sticks
            && sticks.Side == HitObject.Side
            && base.ReplacesExistingObject(existing);
    }
}
