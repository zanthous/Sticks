#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Screens.Edit;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    public partial class SticksSelectionBlueprint : HitObjectSelectionBlueprint<SticksHitObject>, IKeyBindingHandler<GlobalAction>, IKeyBindingHandler<PlatformAction>
    {
        private readonly SticksBlueprintPiece piece;
        private readonly List<SliderPointHandle> sliderHandles = new List<SliderPointHandle>();
        private SticksBlueprintPiece? continuationPreview;
        private SticksSlider? continuationObject;
        private float dragArcAngle;
        private float lastDragPointerAngle;
        private bool hasDragPointer;
        private int draggedSegment = -1;
        private float[] dragSegments = Array.Empty<float>();
        private double[] dragWeights = Array.Empty<double>();
        private double dragDuration;
        private double dragStartTime;
        private bool placingContinuation;
        private SticksSliderPlacementGesture? continuationGesture;
        private SticksSlider[] continuationTargets = Array.Empty<SticksSlider>();

        internal bool IsPlacingContinuation => placingContinuation;

        // Preserve the selected editor time, including authored endpoints off the beat grid.
        private double currentPointTime => editorClock.CurrentTimeAccurate;

        private const double endpoint_time_tolerance = 0.5;

        [Resolved]
        private EditorBeatmap? editorBeatmap { get; set; }

        [Resolved]
        private EditorClock editorClock { get; set; } = null!;

        [Resolved(CanBeNull = true)]
        private IEditorChangeHandler? changeHandler { get; set; }

        [Resolved(CanBeNull = true)]
        private SticksHitObjectComposer? composer { get; set; }

        public SticksSelectionBlueprint(SticksHitObject hitObject)
            : base(hitObject)
        {
            InternalChild = piece = new SticksBlueprintPiece(false);
        }

        protected override bool AlwaysShowWhenSelected => placingContinuation || draggedSegment >= 0;

        protected override void Update()
        {
            base.Update();
            double now = editorClock.CurrentTimeAccurate;
            piece.UpdateFrom(HitObject, now, IsSelected);

            if (HitObject is SticksSlider slider)
            {
                if (draggedSegment >= 0)
                    applyPointDrag(slider, GetContainingInputManager().CurrentState.Keyboard.ShiftPressed);
                updateSliderHandles(slider, now);
                updateContinuationPreview(slider, slider.SegmentStartAngleAt(slider.SegmentCount));
            }
        }

        private void updateSliderHandles(SticksSlider slider, double now)
        {
            if (IsSelected)
            {
                while (sliderHandles.Count < slider.SegmentCount)
                {
                    int index = sliderHandles.Count;
                    var handle = new SliderPointHandle
                    {
                        TurnName = $"Slider turn {index + 1}",
                        DragStarted = e => beginPointDrag(index, e),
                        Dragged = dragPoint,
                        DragEnded = _ => endPointDrag(),
                    };
                    sliderHandles.Add(handle);
                    AddInternal(handle);
                }
            }

            double time = slider.StartTime;
            float angle = slider.Angle;
            double approach = composer?.PlayerApproachDuration ?? slider.ApproachDuration;
            Color4 colour = colourFor(slider.Side);
            for (int i = 0; i < sliderHandles.Count; i++)
            {
                SliderPointHandle handle = sliderHandles[i];
                if (i >= slider.SegmentCount)
                {
                    handle.Available = false;
                    continue;
                }

                time += slider.SegmentDurationAt(i);
                angle += slider.SegmentArcAngleAt(i);
                float radius = SticksEditorCoordinates.RadiusAt(now, time, approach);
                bool isTail = i == slider.SegmentCount - 1;
                handle.Name = isTail ? "Slider tail" : handle.TurnName;
                handle.Position = SticksPlayfield.PointAt(angle, radius);
                handle.FillColour = colour;
                handle.Available = IsSelected && !placingContinuation
                                   && (i == draggedSegment || (isTail || slider.SegmentEndsWithReversal(i))
                                       && radius >= 12 && now <= time + endpoint_time_tolerance);
            }
        }

        protected override void OnDeselected()
        {
            cancelCurrentPoint();
            endPointDrag();
            base.OnDeselected();
        }

        protected override void OnSelected()
        {
            base.OnSelected();
            piece.Alpha = 1;
        }

        public static float AdjustDraggedArcAngle(float rawArcAngle, bool snap)
        {
            return snap ? SticksEditorCoordinates.SnapAngleOffset(rawArcAngle) : rawArcAngle;
        }

        public static float ReversalArcTo(float startAngle, float targetAngle, int previousDirection, bool snap)
        {
            int requiredDirection = Math.Sign(previousDirection) >= 0 ? -1 : 1;
            float arc = SticksHitObject.DeltaAngle(startAngle, targetAngle);

            if (requiredDirection > 0 && arc <= 0)
                arc += 360;
            else if (requiredDirection < 0 && arc >= 0)
                arc -= 360;

            return AdjustDraggedArcAngle(arc, snap);
        }

        public static bool IsAtSliderEndTime(double currentTime, double sliderEndTime) =>
            double.IsFinite(currentTime)
            && double.IsFinite(sliderEndTime)
            && Math.Abs(currentTime - sliderEndTime) <= endpoint_time_tolerance;

        private bool beginPointDrag(int segment, DragStartEvent e)
        {
            if (HitObject is not SticksSlider slider || segment >= slider.SegmentCount || !IsSelected || placingContinuation
                || !double.IsFinite(slider.Duration) || slider.Duration <= 0
                || !tryGetPointerAngle(e.ScreenSpaceMouseDownPosition, out lastDragPointerAngle))
                return false;

            endPointDrag();
            dragSegments = slider.SegmentArcAngles.ToArray();
            dragWeights = slider.HasTimedSegments ? slider.SegmentDurationWeights.ToArray()
                : Enumerable.Range(0, slider.SegmentCount).Select(i => slider.SegmentDurationAt(i) / slider.Duration).ToArray();
            draggedSegment = segment;
            dragArcAngle = dragSegments[segment];
            dragDuration = slider.Duration;
            dragStartTime = slider.StartTime;
            hasDragPointer = true;
            changeHandler?.BeginChange();
            return true;
        }

        private void dragPoint(DragEvent e)
        {
            if (draggedSegment < 0 || HitObject is not SticksSlider slider)
                return;
            if (!tryGetPointerAngle(e.ScreenSpaceMousePosition, out float pointerAngle))
            {
                hasDragPointer = false;
                return;
            }

            if (hasDragPointer)
                dragArcAngle += SticksHitObject.DeltaAngle(lastDragPointerAngle, pointerAngle);
            lastDragPointerAngle = pointerAngle;
            hasDragPointer = true;
            applyPointDrag(slider, e.ShiftPressed);
        }

        private void applyPointDrag(SticksSlider slider, bool snap)
        {
            if (slider.SegmentCount != dragSegments.Length || slider.Duration != dragDuration || slider.StartTime != dragStartTime)
            {
                endPointDrag();
                return;
            }

            float adjusted = AdjustDraggedArcAngle(dragArcAngle, snap);
            if (Math.Abs(slider.SegmentArcAngleAt(draggedSegment) - adjusted) < 0.001f)
                return;

            var angles = new Dictionary<int, double> { [draggedSegment] = adjusted };
            float delta = adjusted - dragSegments[draggedSegment];
            if (draggedSegment + 1 < dragSegments.Length)
                angles[draggedSegment + 1] = dragSegments[draggedSegment + 1] - delta;
            if (!SticksInspectorEdits.TryPrepare(new[] { slider }, new SticksInspectorEdit { SegmentAngles = angles }, null,
                    out var plans, out _)
                || editorBeatmap != null && !SticksInspectorEdits.ValidateCheckpointBudget(plans, editorBeatmap.ControlPointInfo,
                    editorBeatmap.Difficulty.SliderTickRate, out _))
                return;

            float[] segments = dragSegments.ToArray();
            foreach (var (index, arc) in angles)
                segments[index] = (float)arc;
            // Independent timing keeps this join on its authored beat. The next arc absorbs
            // the change so moving a turn leaves every later endpoint in place.
            slider.SetTimedSegments(segments, dragWeights);
            editorBeatmap?.Update(slider);
        }

        private void endPointDrag()
        {
            foreach (SliderPointHandle handle in sliderHandles)
                handle.IsGrabbed = false;

            if (draggedSegment < 0)
                return;
            draggedSegment = -1;
            hasDragPointer = false;
            dragSegments = Array.Empty<float>();
            dragWeights = Array.Empty<double>();
            changeHandler?.EndChange();
        }

        protected override void Dispose(bool isDisposing)
        {
            endPointDrag();
            base.Dispose(isDisposing);
        }

        private bool tryGetPointerAngle(Vector2 screenSpacePosition, out float angle) =>
            SticksEditorCoordinates.TryGetAngle(piece.ToLocalSpace(screenSpacePosition), out angle);

        public void BeginContinuationPlacement(SticksSlider[]? targets = null)
        {
            if (HitObject is not SticksSlider slider || slider.SegmentCount >= SticksSlider.MAX_SEGMENT_COUNT
                || currentPointTime < slider.EndTime - endpoint_time_tolerance)
                return;

            continuationTargets = targets ?? new[] { slider };
            placingContinuation = true;
            continuationGesture = createContinuationGesture(slider);
            endPointDrag();
            foreach (SliderPointHandle handle in sliderHandles)
                handle.Available = false;
            updateContinuationPreview(slider, slider.AngleAt(slider.EndTime));
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button == MouseButton.Right && IsSelected && HitObject is SticksSlider)
            {
                if (!placingContinuation)
                    BeginContinuationPlacement();
                else
                    confirmContinuation(true);

                // Invalid same-beat points must not fall through to quick-delete.
                return true;
            }

            if (!placingContinuation || HitObject is not SticksSlider)
                return base.OnMouseDown(e);

            if (e.Button == MouseButton.Left)
                confirmContinuation(false);

            return true;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (placingContinuation && e.Button == MouseButton.Left)
                confirmContinuation(false);

            base.OnMouseUp(e);
        }

        private void confirmContinuation(bool keepPlacing)
        {
            if (!placingContinuation || HitObject is not SticksSlider slider || continuationGesture == null)
                return;

            updateContinuationGesture();
            if (!continuationGesture.CanCommit)
                return;

            changeHandler?.BeginChange();
            try
            {
                foreach (SticksSlider target in continuationTargets)
                {
                    target.AppendTimedSegment(continuationGesture.Arc, currentPointTime);
                    editorBeatmap?.Update(target);
                }
            }
            finally
            {
                changeHandler?.EndChange();
            }

            releaseContinuationPreview();
            placingContinuation = keepPlacing && slider.SegmentCount < SticksSlider.MAX_SEGMENT_COUNT;
            continuationGesture = placingContinuation
                ? createContinuationGesture(slider)
                : null;
        }

        private SticksSliderPlacementGesture createContinuationGesture(SticksSlider slider)
        {
            float angle = tryGetPointerAngle(GetContainingInputManager().CurrentState.Mouse.Position, out float pointerAngle)
                ? pointerAngle : slider.AngleAt(slider.EndTime);
            return new SticksSliderPlacementGesture(angle, slider.EndTime, slider.EndTime);
        }

        private void updateContinuationGesture()
        {
            if (continuationGesture == null)
                return;

            var state = GetContainingInputManager().CurrentState;
            bool valid = tryGetPointerAngle(state.Mouse.Position, out float angle);
            continuationGesture.UpdatePointer(angle, valid, state.Keyboard.ShiftPressed);
            continuationGesture.UpdateTime(currentPointTime);
        }

        private bool cancelCurrentPoint()
        {
            if (!placingContinuation)
                return false;

            placingContinuation = false;
            releaseContinuationPreview();
            continuationTargets = Array.Empty<SticksSlider>();
            continuationGesture = null;
            return true;
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e) =>
            e.Action == GlobalAction.Back && cancelCurrentPoint();

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        public bool OnPressed(KeyBindingPressEvent<PlatformAction> e) =>
            e.Action == PlatformAction.Undo && cancelCurrentPoint();

        public void OnReleased(KeyBindingReleaseEvent<PlatformAction> e)
        {
        }

        private void releaseContinuationPreview()
        {
            if (continuationPreview != null)
                RemoveInternal(continuationPreview, true);

            continuationPreview = null;
            continuationObject = null;
        }

        private void updateContinuationPreview(SticksSlider slider, float terminalAngle)
        {
            if (!placingContinuation || continuationGesture == null)
            {
                continuationPreview?.Hide();
                return;
            }

            updateContinuationGesture();

            if (continuationPreview == null)
            {
                AddInternal(continuationPreview = new SticksBlueprintPiece { Depth = -19 });
                continuationObject = new SticksSlider();
            }

            continuationObject!.StartTime = slider.EndTime;
            continuationObject.Duration = continuationGesture.Duration;
            continuationObject.Angle = terminalAngle;
            continuationObject.Side = slider.Side;
            continuationObject.PrimaryHitAngle = slider.PrimaryHitAngle;
            continuationObject.ArcAngle = continuationGesture.Arc;
            continuationPreview.UpdateFrom(continuationObject, bothSticks: continuationTargets.Length > 1);
            continuationPreview.Show();
        }

        private Color4 colourFor(StickSide side) => (composer?.Playfield as SticksPlayfield)?.ColourFor(side)
                                                   ?? (side == StickSide.Left ? SticksPlayfield.LEFT_COLOUR : SticksPlayfield.RIGHT_COLOUR);

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
        {
            if (placingContinuation)
            {
                Vector2 localPosition = piece.ToLocalSpace(screenSpacePos);
                if (SticksEditorCoordinates.TryGetAngle(localPosition, out _))
                    return true;
            }

            return piece.ReceiveAt(screenSpacePos)
                   || sliderHandles.Any(handle => handle.ReceivePositionalInputAt(screenSpacePos));
        }

        public override Vector2 ScreenSpaceSelectionPoint => piece.Marker.ScreenSpaceDrawQuad.Centre;

        public override Quad SelectionQuad => piece.SelectionQuad;

        private partial class SliderPointHandle : CircularContainer
        {
            private readonly Box fill;
            private readonly Circle grabbedCentre;
            private bool available;
            private bool isGrabbed;

            public string TurnName { get; init; } = string.Empty;

            public bool IsGrabbed
            {
                get => isGrabbed;
                set
                {
                    if (isGrabbed == value)
                        return;
                    isGrabbed = value;
                    // Apply immediately: the editor clock may be paused or seeking backwards.
                    Size = new Vector2(value ? 26 : 22);
                    grabbedCentre.Alpha = value ? 1 : 0;
                }
            }

            public bool Available
            {
                get => available;
                set
                {
                    if (available == value)
                        return;
                    available = value;
                    Alpha = value ? 1 : 0;
                    if (!value)
                        IsGrabbed = false;
                }
            }

            public Func<DragStartEvent, bool>? DragStarted { get; init; }

            public Action<DragEvent>? Dragged { get; init; }

            public Action<DragEndEvent>? DragEnded { get; init; }

            public Color4 FillColour
            {
                set => fill.Colour = value;
            }

            public SliderPointHandle()
            {
                Anchor = Anchor.TopLeft;
                Origin = Anchor.Centre;
                Size = new Vector2(22);
                Alpha = 0;
                Masking = true;
                BorderThickness = 3;
                BorderColour = Color4.White;
                Depth = -20;
                Children = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both },
                    grabbedCentre = new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(6),
                        Colour = Color4.White,
                        Depth = -1,
                        Alpha = 0,
                    },
                };
            }

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
                Available && base.ReceivePositionalInputAt(screenSpacePos);

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (!Available || e.Button != MouseButton.Left)
                    return false;
                IsGrabbed = true;
                return true;
            }

            protected override void OnMouseUp(MouseUpEvent e)
            {
                if (e.Button == MouseButton.Left)
                    IsGrabbed = false;
                base.OnMouseUp(e);
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                IsGrabbed = Available && e.Button == MouseButton.Left && (DragStarted?.Invoke(e) ?? false);
                return IsGrabbed;
            }

            protected override void OnDrag(DragEvent e)
            {
                Dragged?.Invoke(e);
                base.OnDrag(e);
            }

            protected override void OnDragEnd(DragEndEvent e)
            {
                DragEnded?.Invoke(e);
                IsGrabbed = false;
                base.OnDragEnd(e);
            }
        }

    }
}
