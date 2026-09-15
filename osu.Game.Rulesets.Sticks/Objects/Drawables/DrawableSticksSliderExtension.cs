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
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield sticksPlayfield) => playfield = sticksPlayfield;

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
    }
}
