// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksSliderExtension : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable
    {
        private SticksPlayfield playfield = null!;
        private readonly SticksSliderHeadMarker marker;

        public new SticksSliderExtension HitObject => (SticksSliderExtension)base.HitObject;

        public override bool HandlePositionalInput => false;

        public DrawableSticksSliderExtension()
            : this(null!)
        {
        }

        public DrawableSticksSliderExtension(SticksSliderExtension hitObject)
            : base(hitObject)
        {
            Size = new Vector2(SticksPlayfield.SIZE);

            StickSide side = hitObject?.Side ?? StickSide.Left;
            int direction = hitObject?.Direction ?? 1;
            AddInternal(marker = new SticksSliderHeadMarker(side, direction, colourFor(side))
            {
                Angle = hitObject?.Angle ?? 0,
                Span = hitObject?.PrimaryHitAngle ?? SticksHitObject.VISIBLE_ARC_SPAN,
            });
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield sticksPlayfield) => playfield = sticksPlayfield;

        protected override void Update()
        {
            base.Update();

            if (marker.Side != HitObject.Side || marker.Direction != HitObject.Direction)
                marker.SetLaneAndDirection(HitObject.Side, HitObject.Direction, colourFor(HitObject.Side));
            marker.Presentation = playfield.NotePresentation;
            marker.TargetCircleScale = playfield.NoteCircleScale;
            marker.Angle = HitObject.Angle;
            marker.Span = HitObject.PrimaryHitAngle;
            marker.ApproachCircleEnabled = false;
            marker.SetRadialOffset(ParentHitObject is ISticksVisualRadialOffsetSource source ? source.VisualRadialOffset : 0, true);
        }

        protected override double InitialLifetimeOffset => HitObject.PreemptDuration;

        void ISticksApproachRateAdjustable.RefreshApproachTransforms()
        {
            if (Judged)
                return;

            LifetimeStart = HitObject.StartTime - InitialLifetimeOffset;
            UpdateState(State.Value, true);
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (Judged || Time.Current < HitObject.StartTime)
                return;

            if (ParentHitObject is not ISticksTrackingSource { TrackingAuthorised: true })
            {
                ApplyMinResult();
                return;
            }

            Vector2 stick = playfield.StickVector(HitObject.Side);
            float actualAngle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Y, stick.X) * 180 / MathF.PI);
            float angleError = Math.Abs(SticksHitObject.DeltaAngle(actualAngle, HitObject.Angle));

            if (playfield.IsStickBeyondRechargeBoundary(HitObject.Side) && angleError <= HitObject.LenientHalfAngle)
                ApplyMaxResult();
            else
                ApplyMinResult();
        }

        protected override void UpdateInitialTransforms()
        {
            this.FadeOut();
            using (BeginDelayedSequence(Math.Max(0, InitialLifetimeOffset - HitObject.LoopDuration)))
                this.FadeIn(120);
        }

        protected override void UpdateHitStateTransforms(ArmedState state) => this.FadeOut(180).Expire();

        private static osuTK.Graphics.Color4 colourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;
    }
}
