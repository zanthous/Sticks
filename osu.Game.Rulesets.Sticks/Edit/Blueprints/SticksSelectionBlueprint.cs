// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
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
    public partial class SticksSelectionBlueprint : HitObjectSelectionBlueprint<SticksHitObject>
    {
        private readonly SticksBlueprintPiece piece;
        private readonly SliderTailHandle? sliderTailHandle;
        private readonly SegmentButton? removeSegmentButton;
        private readonly SegmentButton? appendSegmentButton;
        private readonly CircularProgress? continuationPreview;
        private readonly EndpointPreviewMarker? continuationEndpointPreview;
        private float dragArcAngle;
        private float lastDragPointerAngle;
        private float originalDragArcAngle;
        private bool placingContinuation;
        private float pendingContinuationArc;

        private const double endpoint_time_tolerance = 0.5;

        [Resolved]
        private EditorBeatmap? editorBeatmap { get; set; }

        [Resolved]
        private EditorClock editorClock { get; set; } = null!;

        [Resolved(CanBeNull = true)]
        private IEditorChangeHandler? changeHandler { get; set; }

        public SticksSelectionBlueprint(SticksHitObject hitObject)
            : base(hitObject)
        {
            InternalChild = piece = new SticksBlueprintPiece();

            if (hitObject is SticksSlider)
            {
                AddInternal(sliderTailHandle = new SliderTailHandle
                {
                    DragStarted = beginTailDrag,
                    Dragged = dragTail,
                    DragEnded = endTailDrag,
                });
                AddInternal(removeSegmentButton = new SegmentButton(false, removeFinalSegment));
                AddInternal(appendSegmentButton = new SegmentButton(true, beginContinuationPlacement));
                AddInternal(continuationPreview = new CircularProgress
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = new Vector2(SticksPlayfield.SIZE / 2),
                    RoundedCaps = true,
                    Alpha = 0,
                    Depth = -19,
                });
                AddInternal(continuationEndpointPreview = new EndpointPreviewMarker());
            }
        }

        protected override bool AlwaysShowWhenSelected => placingContinuation;

        protected override void Update()
        {
            base.Update();
            piece.UpdateFrom(HitObject);

            if (sliderTailHandle != null && HitObject is SticksSlider slider)
            {
                bool atSliderEnd = isAtSliderEnd(slider);

                if (placingContinuation)
                    pendingContinuationArc = slider.ContinuationArcAt(editorClock.CurrentTimeAccurate);

                float terminalAngle = slider.SegmentStartAngleAt(slider.SegmentCount);
                sliderTailHandle.Position = SticksPlayfield.PointAt(terminalAngle, SticksPlayfield.RadiusFor(slider.Side));
                sliderTailHandle.FillColour = slider.Side == StickSide.Left
                    ? SticksPlayfield.LEFT_COLOUR
                    : SticksPlayfield.RIGHT_COLOUR;

                float radians = (terminalAngle + 90) * MathF.PI / 180;
                Vector2 tangent = new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * 27;
                removeSegmentButton!.Position = sliderTailHandle.Position - tangent;
                appendSegmentButton!.Position = sliderTailHandle.Position + tangent;
                appendSegmentButton.IconRotation = terminalAngle - Math.Sign(slider.SegmentArcAngleAt(slider.SegmentCount - 1)) * 90;
                sliderTailHandle.Available = !placingContinuation;
                removeSegmentButton.Available = atSliderEnd && !placingContinuation;
                appendSegmentButton.Available = atSliderEnd && !placingContinuation;
                removeSegmentButton.Enabled = slider.SegmentCount > 1 && !placingContinuation;
                appendSegmentButton.Enabled = slider.SegmentCount < 16 && !placingContinuation;

                updateContinuationPreview(slider, terminalAngle);
            }
        }

        protected override void OnDeselected()
        {
            placingContinuation = false;
            base.OnDeselected();
        }

        protected override void OnSelected()
        {
            base.OnSelected();
            piece.Alpha = 1;
        }

        public static float AdjustDraggedArcAngle(float rawArcAngle, bool snap)
        {
            float adjusted = snap ? SticksEditorCoordinates.SnapAngleOffset(rawArcAngle) : rawArcAngle;
            float minimum = snap ? 15 : 1;

            if (Math.Abs(adjusted) < minimum)
            {
                float signSource = Math.Abs(rawArcAngle) > 0.001f ? rawArcAngle : 1;
                adjusted = MathF.CopySign(minimum, signSource);
            }

            return adjusted;
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

        private bool isAtSliderEnd(SticksSlider slider) => IsAtSliderEndTime(editorClock.CurrentTimeAccurate, slider.EndTime);

        private bool beginTailDrag(DragStartEvent e)
        {
            if (HitObject is not SticksSlider slider || !tryGetPointerAngle(e.ScreenSpaceMousePosition, out lastDragPointerAngle))
                return false;

            originalDragArcAngle = dragArcAngle = slider.SegmentArcAngleAt(slider.SegmentCount - 1);
            changeHandler?.BeginChange();
            return true;
        }

        private void dragTail(DragEvent e)
        {
            if (HitObject is not SticksSlider slider || !tryGetPointerAngle(e.ScreenSpaceMousePosition, out float pointerAngle))
                return;

            dragArcAngle += SticksHitObject.DeltaAngle(lastDragPointerAngle, pointerAngle);
            lastDragPointerAngle = pointerAngle;

            float adjusted = AdjustDraggedArcAngle(dragArcAngle, e.ShiftPressed);
            if (Math.Abs(slider.SegmentArcAngleAt(slider.SegmentCount - 1) - adjusted) < 0.001f)
                return;

            slider.ReplaceFinalSegment(adjusted);
            editorBeatmap?.Update(slider);
        }

        private void endTailDrag(DragEndEvent e)
        {
            if (HitObject is SticksSlider slider && Math.Abs(slider.SegmentArcAngleAt(slider.SegmentCount - 1)) < 1)
            {
                slider.ReplaceFinalSegment(MathF.CopySign(1, originalDragArcAngle));
                editorBeatmap?.Update(slider);
            }

            changeHandler?.EndChange();
        }

        private bool tryGetPointerAngle(Vector2 screenSpacePosition, out float angle) =>
            SticksEditorCoordinates.TryGetPlacement(piece.ToLocalSpace(screenSpacePosition), out _, out angle);

        private void beginContinuationPlacement()
        {
            if (HitObject is not SticksSlider slider || slider.SegmentCount >= 16 || !isAtSliderEnd(slider))
                return;

            placingContinuation = true;
            pendingContinuationArc = 0;
        }

        private void removeFinalSegment()
        {
            if (HitObject is SticksSlider slider && slider.SegmentCount > 1)
                performUndoableChange(slider, () => slider.RemoveFinalSegmentAtConstantSpeed());
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (!placingContinuation || HitObject is not SticksSlider slider)
                return base.OnMouseDown(e);

            if (e.Button == MouseButton.Right)
            {
                placingContinuation = false;
                return true;
            }

            if (e.Button != MouseButton.Left)
                return true;

            double newEndTime = editorClock.CurrentTimeAccurate;
            if (Math.Abs(slider.ContinuationArcAt(newEndTime)) < 1)
                return true;

            placingContinuation = false;
            performUndoableChange(slider, () => slider.AppendTimedSegmentAtConstantSpeed(newEndTime));
            return true;
        }

        private void updateContinuationPreview(SticksSlider slider, float terminalAngle)
        {
            if (continuationPreview == null || continuationEndpointPreview == null)
                return;

            if (!placingContinuation || Math.Abs(pendingContinuationArc) < 1)
            {
                continuationPreview.Alpha = 0;
                continuationEndpointPreview.Alpha = 0;
                return;
            }

            float radius = SticksPlayfield.RadiusFor(slider.Side);
            const float halfThickness = 4;
            float outerRadius = radius + halfThickness;
            continuationPreview.Size = new Vector2(outerRadius * 2);
            continuationPreview.InnerRadius = 2 * halfThickness / outerRadius;
            continuationPreview.Colour = slider.Side == StickSide.Left ? SticksPlayfield.LEFT_COLOUR : SticksPlayfield.RIGHT_COLOUR;
            continuationPreview.Rotation = 90 + (pendingContinuationArc >= 0 ? terminalAngle : terminalAngle + pendingContinuationArc);
            continuationPreview.Progress = Math.Abs(pendingContinuationArc) / 360;
            continuationPreview.Alpha = 0.55f;

            continuationEndpointPreview.Position = SticksPlayfield.PointAt(
                terminalAngle + pendingContinuationArc,
                radius);
            continuationEndpointPreview.FillColour = slider.Side == StickSide.Left
                ? SticksPlayfield.LEFT_COLOUR
                : SticksPlayfield.RIGHT_COLOUR;
            continuationEndpointPreview.Alpha = 1;
        }

        private void performUndoableChange(HitObject hitObject, Action mutation)
        {
            if (editorBeatmap == null)
            {
                mutation();
                return;
            }

            changeHandler?.BeginChange();
            try
            {
                mutation();
                editorBeatmap.Update(hitObject);
            }
            finally
            {
                changeHandler?.EndChange();
            }
        }

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
        {
            if (placingContinuation)
            {
                Vector2 localPosition = piece.ToLocalSpace(screenSpacePos);
                if (localPosition.X >= 0 && localPosition.X <= SticksPlayfield.SIZE
                                         && localPosition.Y >= 0 && localPosition.Y <= SticksPlayfield.SIZE)
                    return true;
            }

            return piece.ReceiveAt(screenSpacePos)
                   || sliderTailHandle?.ReceivePositionalInputAt(screenSpacePos) == true
                   || removeSegmentButton?.ReceivePositionalInputAt(screenSpacePos) == true
                   || appendSegmentButton?.ReceivePositionalInputAt(screenSpacePos) == true;
        }

        public override Vector2 ScreenSpaceSelectionPoint => piece.Marker.ScreenSpaceDrawQuad.Centre;

        public override Quad SelectionQuad => piece.Marker.ScreenSpaceDrawQuad;

        private partial class SliderTailHandle : CircularContainer
        {
            private readonly Box fill;
            private bool available = true;

            public bool Available
            {
                get => available;
                set
                {
                    available = value;
                    Alpha = value ? 1 : 0;
                }
            }

            public Func<DragStartEvent, bool>? DragStarted { get; init; }

            public Action<DragEvent>? Dragged { get; init; }

            public Action<DragEndEvent>? DragEnded { get; init; }

            public Color4 FillColour
            {
                set => fill.Colour = value;
            }

            public SliderTailHandle()
            {
                Anchor = Anchor.TopLeft;
                Origin = Anchor.Centre;
                Size = new Vector2(22);
                Masking = true;
                BorderThickness = 3;
                BorderColour = Color4.White;
                Depth = -20;
                Child = fill = new Box { RelativeSizeAxes = Axes.Both };
            }

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
                Available && base.ReceivePositionalInputAt(screenSpacePos);

            protected override bool OnMouseDown(MouseDownEvent e) => Available && e.Button == MouseButton.Left;

            protected override bool OnDragStart(DragStartEvent e) =>
                Available && e.Button == MouseButton.Left && (DragStarted?.Invoke(e) ?? false);

            protected override void OnDrag(DragEvent e)
            {
                Dragged?.Invoke(e);
                base.OnDrag(e);
            }

            protected override void OnDragEnd(DragEndEvent e)
            {
                DragEnded?.Invoke(e);
                base.OnDragEnd(e);
            }
        }

        private partial class EndpointPreviewMarker : CircularContainer
        {
            private readonly Box fill;

            public Color4 FillColour
            {
                set => fill.Colour = value;
            }

            public override bool HandlePositionalInput => false;

            public EndpointPreviewMarker()
            {
                Anchor = Anchor.TopLeft;
                Origin = Anchor.Centre;
                Size = new Vector2(22);
                Masking = true;
                BorderThickness = 3;
                BorderColour = Color4.White;
                Depth = -22;
                Alpha = 0;
                Child = fill = new Box { RelativeSizeAxes = Axes.Both };
            }
        }

        private partial class SegmentButton : CircularContainer
        {
            private readonly Action action;
            private readonly SpriteIcon icon;
            private bool enabled = true;
            private bool available;

            public float IconRotation
            {
                set => icon.Rotation = value;
            }

            public bool Available
            {
                get => available;
                set
                {
                    available = value;
                    updateState();
                }
            }

            public bool Enabled
            {
                get => enabled;
                set
                {
                    enabled = value;
                    updateState();
                }
            }

            public SegmentButton(bool increase, Action action)
            {
                this.action = action;

                Anchor = Anchor.TopLeft;
                Origin = Anchor.Centre;
                Size = new Vector2(20);
                Masking = true;
                BorderThickness = 2;
                BorderColour = Color4.White;
                Depth = -21;
                Alpha = 0;
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                        Alpha = 0.7f,
                    },
                    icon = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(increase ? 14 : 11),
                        Icon = increase ? FontAwesome.Solid.AngleDoubleRight : FontAwesome.Solid.Minus,
                        Colour = Color4.White,
                        Shadow = increase,
                    },
                };
            }

            private void updateState() => Alpha = Available ? (Enabled ? 1 : 0.3f) : 0;

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
                Available && base.ReceivePositionalInputAt(screenSpacePos);

            protected override bool OnMouseDown(MouseDownEvent e) => Available && Enabled && e.Button == MouseButton.Left;

            protected override bool OnClick(ClickEvent e)
            {
                if (e.Button != MouseButton.Left || !Available || !Enabled)
                    return false;

                action();
                return true;
            }
        }
    }
}
