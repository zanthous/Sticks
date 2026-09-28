using osu.Framework.Allocation;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksSliderTick : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable
    {
        private SticksPlayfield playfield = null!;

        public new SticksSliderTick HitObject => (SticksSliderTick)base.HitObject;

        public override bool HandlePositionalInput => false;

        public DrawableSticksSliderTick()
            : this(null!)
        {
        }

        public DrawableSticksSliderTick(SticksSliderTick hitObject)
            : base(hitObject)
        {
            Alpha = 0;
            AlwaysPresent = true;
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

            if (ParentHitObject is DrawableSticksSlider slider
                && slider.TryGetTickTrackingSide(HitObject.Angle, HitObject.LenientHalfAngle, out StickSide inputSide)
                && playfield.TryClaimSliderTick(inputSide, HitObject.StartTime))
                ApplyMaxResult();
            else
                ApplyMinResult();
        }
    }
}
