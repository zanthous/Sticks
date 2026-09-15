using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksSlice : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable
    {
        private readonly SticksSliceMarker marker;
        private readonly Container nestedContainer;
        private DrawableSticksTimingWeight weight = null!;
        private SticksPlayfield playfield = null!;
        private double previousTime = double.NaN;
        private Vector2 previousLeft;
        private Vector2 previousRight;

        public new SticksSlice HitObject => (SticksSlice)base.HitObject;
        public override bool HandlePositionalInput => false;
        public override IEnumerable<HitSampleInfo> GetSamples() => HitObject.CreatePlayableSamples();

        public DrawableSticksSlice(SticksSlice note) : base(note)
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            AddInternal(nestedContainer = new Container { AlwaysPresent = true });
            AddInternal(marker = new SticksSliceMarker());
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield field) => playfield = field;

        protected override void Update()
        {
            base.Update();
            double now = Time.Current;
            float radius = SticksPlayfield.GUIDE_RADIUS * SticksPlayfield.CenterOutProgressAt(now, HitObject.StartTime, HitObject.ApproachDuration);
            marker.SetState(radius, HitObject.Angle, HitObject.Direction, playfield.SliceColourFor(HitObject));
            Vector2 left = playfield.StickVector(StickSide.Left);
            Vector2 right = playfield.StickVector(StickSide.Right);
            if (!Judged && !playfield.IsPausedEditorPreview && double.IsFinite(previousTime) && now > previousTime)
            {
                if (HitObject.Side == StickSide.Left || playfield.EitherStick)
                    tryCross(StickSide.Left, previousLeft, left, previousTime, now);
                if (!Judged && (HitObject.Side == StickSide.Right || playfield.EitherStick))
                    tryCross(StickSide.Right, previousRight, right, previousTime, now);
            }
            if (!Judged && HitObject.HitWindows != null && !HitObject.HitWindows.CanBeHit(now - HitObject.StartTime))
                apply(HitResult.Miss);
            previousTime = playfield.IsPausedEditorPreview ? double.NaN : now;
            previousLeft = left;
            previousRight = right;
        }

        private void tryCross(StickSide side, Vector2 from, Vector2 to, double start, double end)
        {
            if (!SticksSliceInput.TryCross(HitObject, from, to, out double progress))
                return;
            double offset = start + (end - start) * progress - HitObject.StartTime;
            HitResult grade = HitObject.HitWindows.ResultFor(offset);
            if (grade.IsHit() && playfield.TryClaimAimContact(side, HitObject.StartTime + offset))
                apply(grade);
        }

        private void apply(HitResult grade)
        {
            ApplyResult(grade);
            weight.ApplyTimingResult(grade);
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            // The input sweep must be checked before declaring a late miss.
        }

        protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject) =>
            hitObject is SticksClick.TimingWeight timing ? new DrawableSticksTimingWeight(timing) : base.CreateNestedHitObject(hitObject);

        protected override void AddNestedHitObject(DrawableHitObject hitObject)
        {
            base.AddNestedHitObject(hitObject);
            nestedContainer.Add(hitObject);
            weight = (DrawableSticksTimingWeight)hitObject;
        }

        protected override void ClearNestedHitObjects()
        {
            base.ClearNestedHitObjects();
            nestedContainer.Clear(false);
            weight = null!;
            previousTime = double.NaN;
        }

        protected override double InitialLifetimeOffset => HitObject.ApproachDuration;
        void ISticksApproachRateAdjustable.RefreshApproachTransforms()
        {
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
