// Copyright (c) Zanthous. Licensed under the MIT Licence.

#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    public partial class SticksSelectionBlueprint : HitObjectSelectionBlueprint<SticksHitObject>
    {
        private readonly SticksBlueprintPiece piece;

        [Resolved]
        private EditorBeatmap? editorBeatmap { get; set; }

        [Resolved]
        private IBeatSnapProvider? beatSnapProvider { get; set; }

        protected override bool AlwaysShowWhenSelected => true;

        public SticksSelectionBlueprint(SticksHitObject hitObject)
            : base(hitObject)
        {
            InternalChild = piece = new SticksBlueprintPiece();
        }

        protected override void Update()
        {
            base.Update();
            piece.UpdateFrom(HitObject);
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

        protected override bool OnScroll(ScrollEvent e)
        {
            int direction = Math.Sign(e.ScrollDelta.Y);
            if (!IsSelected || direction == 0)
                return base.OnScroll(e);

            if (e.ShiftPressed && HitObject is SticksSlider slider)
            {
                int repeatCount = AdjustRepeatCount(slider.RepeatCount, direction);
                if (repeatCount != slider.RepeatCount)
                    performUndoableChange(slider, () => slider.RepeatCount = repeatCount);
                return true;
            }

            if (e.ControlPressed && HitObject is IHasDuration duration)
            {
                double step = beatSnapProvider?.GetBeatLengthAtTime(HitObject.StartTime) ?? 100;
                double adjustedDuration = AdjustDuration(duration.Duration, step, direction);
                if (adjustedDuration != duration.Duration)
                    performUndoableChange(HitObject, () => duration.Duration = adjustedDuration);
                return true;
            }

            return base.OnScroll(e);
        }

        public static int AdjustRepeatCount(int repeatCount, int direction) =>
            Math.Clamp(repeatCount + Math.Sign(direction), 0, 16);

        public static double AdjustDuration(double duration, double step, int direction)
        {
            step = Math.Max(1, step);
            return Math.Max(step, duration + Math.Sign(direction) * step);
        }

        private void performUndoableChange(HitObject hitObject, Action mutation)
        {
            if (editorBeatmap == null)
            {
                mutation();
                return;
            }

            editorBeatmap.BeginChange();
            try
            {
                mutation();
                editorBeatmap.Update(hitObject);
            }
            finally
            {
                editorBeatmap.EndChange();
            }
        }

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => piece.ReceiveAt(screenSpacePos);

        public override Vector2 ScreenSpaceSelectionPoint => piece.Marker.ScreenSpaceDrawQuad.Centre;

        public override Quad SelectionQuad => piece.Marker.ScreenSpaceDrawQuad;
    }
}
