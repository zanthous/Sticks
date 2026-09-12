using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksClick : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable
    {
        private const float halo_thickness = 5;
        private readonly CircularContainer halo;
        private SticksPlayfield playfield = null!;
        private HitResult? pendingResult;

        public new SticksClick HitObject => (SticksClick)base.HitObject;

        public override bool HandlePositionalInput => false;

        public override IEnumerable<HitSampleInfo> GetSamples() => HitObject.CreatePlayableSamples();

        public DrawableSticksClick(SticksClick hitObject)
            : base(hitObject)
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            AddInternal(halo = new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                BorderThickness = halo_thickness,
                Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                // An unfilled border is the entire note, including its timing cue.
            });
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield sticksPlayfield) => playfield = sticksPlayfield;

        protected override void Update()
        {
            base.Update();
            // Input is collected by the parent before our frame clock updates. Judge here so
            // timing offsets, result transforms and replay seeks use this frame's timestamp.
            if (pendingResult is HitResult pending)
            {
                pendingResult = null;
                if (!Judged)
                    ApplyResult(pending);
            }
            float radius = SticksPlayfield.GUIDE_RADIUS * SticksPlayfield.CenterOutProgressAt(
                Time.Current, HitObject.StartTime, HitObject.ApproachDuration);
            float thickness = System.Math.Min(halo_thickness, radius);
            // Masked borders draw inward from their bounds. Centre this stroke on the same
            // approach radius as regular note arcs instead of placing its outer edge there.
            halo.Size = new Vector2(radius * 2 + thickness);
            halo.BorderThickness = thickness;
            halo.BorderColour = playfield.ClickColourFor(HitObject);
            if (!Judged && playfield.RelaxMode && Time.Current >= HitObject.StartTime)
                ApplyResult(HitResult.Perfect);
        }

        internal bool TryHit(double time)
        {
            if (Judged || HitObject.HitWindows == null)
                return false;

            HitResult result = HitObject.HitWindows.ResultFor(time - HitObject.StartTime);
            if (!result.IsHit())
                return false;

            pendingResult = result;
            return true;
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (!Judged && HitObject.HitWindows != null && !HitObject.HitWindows.CanBeHit(timeOffset))
                ApplyMinResult();
        }

        protected override double InitialLifetimeOffset => HitObject.ApproachDuration;

        void ISticksApproachRateAdjustable.RefreshApproachTransforms()
        {
            if (Judged)
                return;
            LifetimeStart = HitObject.StartTime - InitialLifetimeOffset;
            UpdateState(State.Value, true);
        }

        protected override void UpdateInitialTransforms() => this.Show();

        protected override void UpdateHitStateTransforms(ArmedState state)
        {
            if (state == ArmedState.Hit)
                this.FadeOut(140).Expire();
            else if (state == ArmedState.Miss)
                this.FadeColour(Color4.Gray, 100).FadeOut(240).Expire();
        }
    }
}
