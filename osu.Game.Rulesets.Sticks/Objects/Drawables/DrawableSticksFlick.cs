// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Utils;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksFlick : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable
    {
        private readonly SticksArcMarker marker;
        private readonly SticksSyncedNoteLink syncedNoteLink;
        private readonly Container nestedContainer;
        private DrawableSticksAngleComponent angleComponent = null!;
        private SticksPlayfield playfield = null!;
        private long observedSequence;

        public new SticksFlick HitObject => (SticksFlick)base.HitObject;

        public override bool HandlePositionalInput => false;

        public override IEnumerable<HitSampleInfo> GetSamples() => HitObject.CreatePlayableSamples();

        public DrawableSticksFlick(SticksFlick hitObject)
            : base(hitObject)
        {
            Size = new Vector2(SticksPlayfield.SIZE);

            AddInternal(nestedContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                AlwaysPresent = true,
            });

            if (hitObject.SyncedNoteSide is StickSide linkedSide)
            {
                AddInternal(syncedNoteLink = new SticksSyncedNoteLink(
                    hitObject.Side,
                    hitObject.Angle,
                    linkedSide,
                    hitObject.SyncedNoteAngle));
            }

            AddInternal(marker = new SticksArcMarker(hitObject.Side, colourFor(hitObject.Side), true)
            {
                Angle = hitObject.Angle,
                Span = hitObject.PrimaryHitAngle * 0.2f,
            });
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield sticksPlayfield)
        {
            playfield = sticksPlayfield;
            observedSequence = playfield.FlickSequence(HitObject.Side);
        }

        protected override void Update()
        {
            base.Update();

            if (marker.Side != HitObject.Side)
            {
                marker.SetLane(HitObject.Side, colourFor(HitObject.Side));
                observedSequence = playfield.FlickSequence(HitObject.Side);
            }
            marker.Angle = HitObject.Angle;

            double approach = Math.Clamp((Time.Current - (HitObject.StartTime - HitObject.ApproachDuration)) / HitObject.ApproachDuration, 0, 1);
            double growth = SticksHitObject.ApproachGrowthProgress(approach);
            marker.Span = HitObject.PrimaryHitAngle * (float)(0.2 + growth * 0.8);

            if (syncedNoteLink != null)
            {
                if (HitObject.SyncedNoteSide is StickSide linkedSide)
                {
                    syncedNoteLink.SetGeometry(
                        HitObject.Side,
                        HitObject.Angle,
                        linkedSide,
                        HitObject.SyncedNoteAngle);
                }
                syncedNoteLink.Alpha = SticksSyncedNoteLink.AlphaAtGrowth(growth);
            }

            long sequence = playfield.FlickSequence(HitObject.Side);
            if (Judged)
                return;

            if (sequence != observedSequence)
            {
                observedSequence = sequence;
                SticksInputTracker.FlickEvent flick = playfield.LastFlick(HitObject.Side);
                double offset = flick.Time - HitObject.StartTime;
                float angleError = Math.Abs(SticksHitObject.DeltaAngle(flick.Angle, HitObject.Angle));

                if (offset >= -SticksFlick.EARLY_HIT_WINDOW
                    && offset <= SticksFlick.LATE_HIT_WINDOW
                    && playfield.TryConsumeFlick(HitObject.Side, flick.Sequence))
                {
                    HitResult timingResult = HitObject.HitWindows?.ResultFor(offset) ?? HitResult.Great;
                    HitResult angleResult = HitObject.ResultForCurrentAngleError(angleError);
                    (timingResult, angleResult) = SticksHitObject.ResolveComponentResults(timingResult, angleResult);

                    // Apply in reading order. Both are native basic judgements and therefore each
                    // contributes exactly half of this note's accuracy.
                    ApplyResult(timingResult);
                    angleComponent.ApplyAngleResult(angleResult);
                    return;
                }
            }

            double currentOffset = Time.Current - HitObject.StartTime;
            if (HitObject.HitWindows is not null && !HitObject.HitWindows.CanBeHit(currentOffset))
                applyMisses();
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (!Judged && HitObject.HitWindows is not null && !HitObject.HitWindows.CanBeHit(timeOffset))
                applyMisses();
        }

        private void applyMisses()
        {
            ApplyMinResult();
            angleComponent.ApplyMiss();
        }

        protected override void AddNestedHitObject(DrawableHitObject hitObject)
        {
            base.AddNestedHitObject(hitObject);
            nestedContainer.Add(hitObject);
            angleComponent = (DrawableSticksAngleComponent)hitObject;
        }

        protected override void ClearNestedHitObjects()
        {
            base.ClearNestedHitObjects();
            nestedContainer.Clear(false);
            angleComponent = null!;
        }

        protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject) => hitObject switch
        {
            SticksAngleComponent angle => new DrawableSticksAngleComponent(angle),
            _ => base.CreateNestedHitObject(hitObject),
        };

        protected override double InitialLifetimeOffset => HitObject.ApproachDuration;

        void ISticksApproachRateAdjustable.RefreshApproachTransforms()
        {
            if (Judged)
                return;

            LifetimeStart = HitObject.StartTime - InitialLifetimeOffset;
            UpdateState(State.Value, true);
        }

        protected override void UpdateInitialTransforms() => this.FadeInFromZero(Math.Min(120, HitObject.ApproachDuration / 3));

        protected override void UpdateHitStateTransforms(ArmedState state)
        {
            if (state == ArmedState.Hit)
                this.FadeColour(Color4.White, 70).FadeOut(140).Expire();
            else if (state == ArmedState.Miss)
                this.FadeColour(Color4.Gray, 100).FadeOut(240).Expire();
        }

        private static Color4 colourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;
    }
}
