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
        private SticksPlayfield playfield = null!;
        private readonly SticksSliderHeadMarker marker;

        public new SticksSliderRepeat HitObject => (SticksSliderRepeat)base.HitObject;

        public override bool HandlePositionalInput => false;

        public DrawableSticksSliderRepeat()
            : this(null!)
        {
        }

        public DrawableSticksSliderRepeat(SticksSliderRepeat hitObject)
            : base(hitObject)
        {
            Size = new Vector2(SticksPlayfield.SIZE);

            StickSide side = hitObject?.Side ?? StickSide.Left;
            int direction = hitObject?.DirectionAfter ?? 1;
            AddInternal(marker = new SticksSliderHeadMarker(
                side,
                direction,
                colourFor(side),
                reversalStyle: true)
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

            marker.SetLaneAndDirection(HitObject.Side, HitObject.DirectionAfter, colourFor(HitObject.Side));
            marker.Angle = HitObject.Angle;
            marker.Span = HitObject.PrimaryHitAngle;
            marker.SkinCentreOnly = true;
            // A hidden optional marker still needs skin-change callbacks, otherwise adding
            // a reversal image while this pooled marker is alive cannot make it visible.
            marker.AlwaysPresent = true;

            float radius = SticksPlayfield.GUIDE_RADIUS * SticksPlayfield.CenterOutProgressAt(
                Time.Current, HitObject.StartTime, HitObject.ApproachDuration);
            marker.SetRadialOffset(radius - SticksPlayfield.RadiusFor(HitObject.Side), true);
            bool sliderEnded = ParentHitObject is DrawableSticksSlider slider && Time.Current > slider.HitObject.EndTime;
            marker.Alpha = marker.HasSkinCentre && radius > 0 && !sliderEnded ? 1 : 0;
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

            if (playfield.IsStickBeyondRechargeBoundary(HitObject.Side)
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

        private osuTK.Graphics.Color4 colourFor(StickSide side) => playfield?.ColourFor(side) ?? (side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR);
    }
}
