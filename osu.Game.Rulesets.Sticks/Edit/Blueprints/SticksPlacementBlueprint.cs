using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Screens.Edit;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    public abstract partial class SticksPlacementBlueprint<T> : HitObjectPlacementBlueprint, IKeyBindingHandler<PlatformAction>
        where T : SticksHitObject, new()
    {
        public new T HitObject => (T)base.HitObject;

        protected readonly SticksBlueprintPiece Piece;

        protected bool HasValidPosition { get; private set; }

        protected float CurrentPointerAngle { get; private set; }

        public bool BothSticks { get; private set; }

        protected T PlacedPartner { get; private set; }

        [Resolved]
        private EditorBeatmap editorBeatmap { get; set; }

        private InputManager inputManager;

        protected bool ShiftPressed => inputManager?.CurrentState.Keyboard.ShiftPressed == true;

        protected override bool IsValidForPlacement => base.IsValidForPlacement && HasValidPosition;

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePosition) =>
            base.ReceivePositionalInputAt(screenSpacePosition)
            || SticksEditorCoordinates.TryGetPlacement(ToLocalSpace(screenSpacePosition), out _, out _);

        protected SticksPlacementBlueprint(T hitObject)
            : base(hitObject)
        {
            AddInternal(Piece = new SticksBlueprintPiece());
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
            // The base blueprint tints invalid placement red, which turns blue heads black.
            // Hide invalid waiting previews instead; an active duration gesture remains visible.
            Colour = Colour4.White;
            Piece.Alpha = PlacementActive == PlacementState.Finished
                          || PlacementActive == PlacementState.Waiting && (!IsValidForPlacement || hasIdenticalPlacedHead()) ? 0 : 1;
            Piece.UpdateFrom(HitObject, bothSticks: BothSticks);
        }

        private bool hasIdenticalPlacedHead()
        {
            // HitObjects are sorted by time. Restrict the lookup to this chord rather than
            // scanning a large beatmap every frame or maintaining a stale placement cache.
            var objects = editorBeatmap.HitObjects;
            int low = 0;
            int high = objects.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (objects[middle].StartTime < HitObject.StartTime - 0.5)
                    low = middle + 1;
                else
                    high = middle;
            }

            bool ownSide = false;
            bool otherSide = false;
            for (int i = low; i < objects.Count && objects[i].StartTime <= HitObject.StartTime + 0.5; i++)
            {
                if (objects[i] is not SticksHitObject existing || existing.GetType() != HitObject.GetType()
                    || existing is not SticksClick && Math.Abs(SticksHitObject.DeltaAngle(existing.Angle, HitObject.Angle)) > 0.01)
                    continue;

                if (existing.Side == HitObject.Side)
                    ownSide = true;
                else
                    otherSide = true;
            }
            return ownSide && (!BothSticks || otherSide);
        }

        public override SnapResult UpdateTimeAndPosition(Vector2 screenSpacePosition, double fallbackTime)
        {
            Vector2 localPosition = ToLocalSpace(screenSpacePosition);
            bool validPointer = SticksEditorCoordinates.TryGetPlacement(localPosition, out StickSide side, out float angle, out bool bothSticks);

            if (PlacementActive == PlacementState.Waiting)
            {
                if (validPointer && ShiftPressed)
                    angle = SticksEditorCoordinates.SnapAngle(angle);

                CurrentPointerAngle = angle;
                HasValidPosition = validPointer;
                if (validPointer)
                {
                    BothSticks = bothSticks;
                    HitObject.Side = side;
                    HitObject.Angle = angle;
                    screenSpacePosition = ToScreenSpace(SticksEditorCoordinates.PositionFor(side, angle));
                }
            }
            else
            {
                validPointer = SticksEditorCoordinates.TryGetAngle(localPosition, out angle);
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

        public bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
        {
            if (e.Action != PlatformAction.Undo || PlacementActive != PlacementState.Active)
                return false;

            EndPlacement(false);
            return true;
        }

        public void OnReleased(KeyBindingReleaseEvent<PlatformAction> e)
        {
        }

        public override void EndPlacement(bool commit)
        {
            if (PlacementActive == PlacementState.Finished)
                return;

            if (!commit || !BothSticks || !IsValidForPlacement)
            {
                base.EndPlacement(commit);
                return;
            }

            // Keep the normal placement replacement/selection behaviour, but commit the
            // opposite hand inside the same transaction so one undo removes the pair.
            var partner = new T
            {
                StartTime = HitObject.StartTime,
                Angle = HitObject.Angle,
                Side = HitObject.Side == StickSide.Left ? StickSide.Right : StickSide.Left,
                Samples = HitObject.CreatePlayableSamples().Select(sample => sample.With()).ToList(),
            };
            if (HitObject is SticksSlider slider && partner is SticksSlider partnerSlider)
            {
                partnerSlider.Duration = slider.Duration;
                if (slider.HasTimedSegments)
                    partnerSlider.SetTimedSegments(slider.SegmentArcAngles,
                        Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentDurationAt).ToList());
                else
                    partnerSlider.SetCustomSegments(slider.SegmentArcAngles);
            }

            partner.ApplyDefaults(editorBeatmap.ControlPointInfo, editorBeatmap.Difficulty);

            editorBeatmap.BeginChange();
            try
            {
                base.EndPlacement(true);
                editorBeatmap.Add(partner);
                PlacedPartner = partner;
            }
            finally
            {
                editorBeatmap.EndChange();
            }
        }

        public override bool ReplacesExistingObject(HitObject existing) =>
            existing is SticksHitObject sticks
            && (BothSticks || sticks.Side == HitObject.Side)
            && (sticks is SticksClick) == (HitObject is SticksClick)
            && base.ReplacesExistingObject(existing);
    }
}
