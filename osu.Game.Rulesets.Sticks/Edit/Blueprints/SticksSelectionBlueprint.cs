// Copyright (c) Zanthous. Licensed under the MIT Licence.

#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
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
        private readonly RepeatButton? decreaseRepeatsButton;
        private readonly RepeatButton? increaseRepeatsButton;
        private float dragArcAngle;
        private float lastDragPointerAngle;
        private float originalDragArcAngle;

        [Resolved]
        private EditorBeatmap? editorBeatmap { get; set; }

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
                AddInternal(decreaseRepeatsButton = new RepeatButton(false, () => adjustRepeats(-1)));
                AddInternal(increaseRepeatsButton = new RepeatButton(true, () => adjustRepeats(1)));
            }
        }

        protected override void Update()
        {
            base.Update();
            piece.UpdateFrom(HitObject);

            if (sliderTailHandle != null && HitObject is SticksSlider slider)
            {
                sliderTailHandle.Position = SticksPlayfield.PointAt(
                    slider.Angle + slider.ArcAngle,
                    SticksPlayfield.RadiusFor(slider.Side));
                sliderTailHandle.FillColour = slider.Side == StickSide.Left
                    ? SticksPlayfield.LEFT_COLOUR
                    : SticksPlayfield.RIGHT_COLOUR;

                float tailAngle = slider.Angle + slider.ArcAngle;
                float radians = (tailAngle + 90) * MathF.PI / 180;
                Vector2 tangent = new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * 27;
                decreaseRepeatsButton!.Position = sliderTailHandle.Position - tangent;
                increaseRepeatsButton!.Position = sliderTailHandle.Position + tangent;
                decreaseRepeatsButton.Enabled = slider.RepeatCount > 0;
                increaseRepeatsButton.Enabled = slider.RepeatCount < 16;
            }
        }

        protected override void OnDeselected()
        {
            base.OnDeselected();
        }

        protected override void OnSelected()
        {
            base.OnSelected();
            piece.Alpha = 1;
        }

        public static int AdjustRepeatCount(int repeatCount, int direction) =>
            Math.Clamp(repeatCount + Math.Sign(direction), 0, 16);

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

        private bool beginTailDrag(DragStartEvent e)
        {
            if (HitObject is not SticksSlider slider || !tryGetPointerAngle(e.ScreenSpaceMousePosition, out lastDragPointerAngle))
                return false;

            originalDragArcAngle = dragArcAngle = slider.ArcAngle;
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
            if (Math.Abs(slider.ArcAngle - adjusted) < 0.001f)
                return;

            slider.ArcAngle = adjusted;
            editorBeatmap?.Update(slider);
        }

        private void endTailDrag(DragEndEvent e)
        {
            if (HitObject is SticksSlider slider && Math.Abs(slider.ArcAngle) < 1)
            {
                slider.ArcAngle = MathF.CopySign(1, originalDragArcAngle);
                editorBeatmap?.Update(slider);
            }

            changeHandler?.EndChange();
        }

        private bool tryGetPointerAngle(Vector2 screenSpacePosition, out float angle) =>
            SticksEditorCoordinates.TryGetPlacement(piece.ToLocalSpace(screenSpacePosition), out _, out angle);

        private void adjustRepeats(int direction)
        {
            if (HitObject is not SticksSlider slider)
                return;

            int repeatCount = AdjustRepeatCount(slider.RepeatCount, direction);
            if (repeatCount != slider.RepeatCount)
                performUndoableChange(slider, () => slider.RepeatCount = repeatCount);
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

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
            piece.ReceiveAt(screenSpacePos)
            || sliderTailHandle?.ReceivePositionalInputAt(screenSpacePos) == true
            || decreaseRepeatsButton?.ReceivePositionalInputAt(screenSpacePos) == true
            || increaseRepeatsButton?.ReceivePositionalInputAt(screenSpacePos) == true;

        public override Vector2 ScreenSpaceSelectionPoint => piece.Marker.ScreenSpaceDrawQuad.Centre;

        public override Quad SelectionQuad => piece.Marker.ScreenSpaceDrawQuad;

        private partial class SliderTailHandle : CircularContainer
        {
            private readonly Box fill;

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

            protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left;

            protected override bool OnDragStart(DragStartEvent e) =>
                e.Button == MouseButton.Left && (DragStarted?.Invoke(e) ?? false);

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

        private partial class RepeatButton : CircularContainer
        {
            private readonly Action action;
            private bool enabled = true;

            public bool Enabled
            {
                get => enabled;
                set
                {
                    enabled = value;
                    Alpha = value ? 1 : 0.3f;
                }
            }

            public RepeatButton(bool increase, Action action)
            {
                this.action = action;

                Anchor = Anchor.TopLeft;
                Origin = Anchor.Centre;
                Size = new Vector2(20);
                Masking = true;
                BorderThickness = 2;
                BorderColour = Color4.White;
                Depth = -21;
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                        Alpha = 0.7f,
                    },
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(11),
                        Icon = increase ? FontAwesome.Solid.Plus : FontAwesome.Solid.Minus,
                        Colour = Color4.White,
                    },
                };
            }

            protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left;

            protected override bool OnClick(ClickEvent e)
            {
                if (e.Button != MouseButton.Left || !Enabled)
                    return false;

                action();
                return true;
            }
        }
    }
}
