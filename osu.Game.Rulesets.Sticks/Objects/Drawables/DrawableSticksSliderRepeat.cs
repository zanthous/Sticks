// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksSliderRepeat : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable
    {
        private const float tracking_magnitude = 0.56f;

        private SticksPlayfield playfield = null!;
        private readonly SticksSliderHeadMarker marker;

        public new SticksSliderRepeat HitObject => (SticksSliderRepeat)base.HitObject;

        public override bool HandlePositionalInput => false;

        public DrawableSticksSliderRepeat(SticksSliderRepeat hitObject)
            : base(hitObject)
        {
            Size = new Vector2(SticksPlayfield.SIZE);

            AddInternal(marker = new SticksSliderHeadMarker(
                hitObject.Side,
                hitObject.DirectionAfter,
                colourFor(hitObject.Side),
                reversalStyle: true)
            {
                Angle = hitObject.Angle,
                Span = hitObject.PrimaryHitAngle,
            });
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield sticksPlayfield) => playfield = sticksPlayfield;

        protected override void Update()
        {
            base.Update();

            if (marker.Side != HitObject.Side || marker.Direction != HitObject.DirectionAfter)
                marker.SetLaneAndDirection(HitObject.Side, HitObject.DirectionAfter, colourFor(HitObject.Side));
            marker.Angle = HitObject.Angle;
            marker.Span = HitObject.PrimaryHitAngle;
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

            if (stick.Length >= tracking_magnitude
                && SticksSliderRepeat.IsAngleInRange(angleError, HitObject.PrimaryHitAngle, HitObject.SecondaryHitAngle))
                ApplyMaxResult();
            else
                ApplyMinResult();
        }

        protected override void UpdateInitialTransforms()
        {
            this.FadeOut();
            using (BeginDelayedSequence(Math.Max(0, InitialLifetimeOffset - HitObject.DisplayPreempt)))
                this.FadeIn(120);
        }

        protected override void UpdateHitStateTransforms(ArmedState state) => this.FadeOut(180).Expire();

        private static osuTK.Graphics.Color4 colourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;
    }
}
