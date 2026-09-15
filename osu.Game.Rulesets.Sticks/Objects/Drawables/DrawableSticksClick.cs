using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Objects;
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
        private readonly SticksClickHalo halo;
        private SticksPlayfield playfield = null!;
        private HitResult? pendingResult;
        private readonly Container nestedContainer;
        private DrawableSticksTimingWeight timingWeight = null!;

        public new SticksClick HitObject => (SticksClick)base.HitObject;

        public override bool HandlePositionalInput => false;

        public override IEnumerable<HitSampleInfo> GetSamples() => HitObject.CreatePlayableSamples();

        public DrawableSticksClick(SticksClick hitObject)
            : base(hitObject)
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            AddInternal(nestedContainer = new Container());
            AddInternal(halo = new SticksClickHalo
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
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
                    applyClickResult(pending);
            }
            float radius = SticksPlayfield.GUIDE_RADIUS * SticksPlayfield.CenterOutProgressAt(
                Time.Current, HitObject.StartTime, HitObject.ApproachDuration);
            halo.SetGeometry(radius, playfield.ClickColourFor(HitObject));
            if (!Judged && playfield.RelaxMode && Time.Current >= HitObject.StartTime)
                applyClickResult(HitResult.Great);
        }

        internal bool HasPendingResult => pendingResult.HasValue;

        internal bool TryHit(double time)
        {
            if (Judged || pendingResult.HasValue || HitObject.HitWindows == null)
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
                applyClickResult(HitResult.Miss);
        }

        private void applyClickResult(HitResult result)
        {
            ApplyResult(result);
            timingWeight.ApplyTimingResult(result);
        }

        protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject) => hitObject switch
        {
            SticksClick.TimingWeight weight => new DrawableSticksTimingWeight(weight),
            _ => base.CreateNestedHitObject(hitObject),
        };

        protected override void AddNestedHitObject(DrawableHitObject hitObject)
        {
            base.AddNestedHitObject(hitObject);
            nestedContainer.Add(hitObject);
            timingWeight = (DrawableSticksTimingWeight)hitObject;
        }

        protected override void ClearNestedHitObjects()
        {
            base.ClearNestedHitObjects();
            nestedContainer.Clear(false);
            timingWeight = null!;
            pendingResult = null;
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
