// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

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
        private SticksSyncedNoteLink syncedNoteLink;
        private readonly Container nestedContainer;
        private DrawableSticksAngleComponent angleComponent = null!;
        private SticksPlayfield playfield = null!;
        private long observedSequence;
        private bool visualRadialOffsetInitialised;

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

            ensureSyncedNoteLink();

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
            marker.Presentation = playfield.NotePresentation;
            marker.TargetCircleScale = playfield.NoteCircleScale;
            marker.Angle = HitObject.Angle;

            double approachStart = HitObject.StartTime - HitObject.ApproachDuration;
            bool useCenterOut = marker.Presentation == Configuration.SticksNotePresentation.CenterOut;

            if (useCenterOut)
            {
                float radius = SticksPlayfield.GUIDE_RADIUS * SticksPlayfield.CenterOutProgressAt(Time.Current, HitObject.StartTime, HitObject.ApproachDuration);
                marker.SetRadialOffset(radius - SticksPlayfield.RadiusFor(HitObject.Side), true);
            }
            else if (playfield.RadialNoteApproach)
            {
                marker.SetRadialOffset(playfield.VisualRadialOffsetFor(this, HitObject), true);
            }
            else if (Time.Current < approachStart)
            {
                visualRadialOffsetInitialised = false;
            }
            else
            {
                marker.SetRadialOffset(playfield.VisualRadialOffsetFor(this, HitObject), !visualRadialOffsetInitialised);
                visualRadialOffsetInitialised = true;
            }

            double approach = Math.Clamp((Time.Current - approachStart) / HitObject.ApproachDuration, 0, 1);
            double growth = SticksHitObject.ApproachGrowthProgress(approach);
            bool useApproachCircles = marker.Presentation == Configuration.SticksNotePresentation.ApproachCircles;

            // Presentations with an independent timing cue keep the target at its final angular
            // width: approach circles contract externally, while filling arcs fill internally.
            marker.Span = SticksArcMarker.SpanForApproach(HitObject.PrimaryHitAngle, marker.Presentation, growth);
            marker.ApproachCircleEnabled = useApproachCircles;
            marker.ApproachProgress = (float)approach;
            marker.ApproachAlpha = useApproachCircles && !Judged
                ? 0.9f * (float)(1 - Math.Clamp((Time.Current - HitObject.StartTime) / 50, 0, 1))
                : 0;

            ensureSyncedNoteLink();
            if (syncedNoteLink != null && HitObject.SyncedNoteSide.HasValue)
                syncedNoteLink.Alpha = useCenterOut ? 0 : SticksSyncedNoteLink.AlphaAtGrowth(growth);

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
                    && playfield.TryConsumeHeadFlick(this, HitObject.Side, flick.Sequence))
                {
                    HitResult timingResult = HitObject.HitWindows?.ResultFor(offset) ?? HitResult.Great;
                    HitResult angleResult = HitObject.ResultForCurrentAngleError(angleError);
                    (timingResult, angleResult) = SticksHitObject.ResolveComponentResults(timingResult, angleResult);

                    // Apply in reading order. Both are native basic judgements and therefore each
                    // contributes exactly half of this note's accuracy.
                    ApplyResult(timingResult);
                    angleComponent.ApplyAngleResult(angleResult, angleError);
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

        private void ensureSyncedNoteLink()
        {
            if (HitObject.SyncedNoteSide is not StickSide linkedSide)
            {
                if (syncedNoteLink != null)
                    syncedNoteLink.Alpha = 0;
                return;
            }

            if (syncedNoteLink == null)
            {
                AddInternal(syncedNoteLink = new SticksSyncedNoteLink(
                    HitObject.Side,
                    HitObject.Angle,
                    linkedSide,
                    HitObject.SyncedNoteAngle));
            }
            else
            {
                syncedNoteLink.SetGeometry(
                    HitObject.Side,
                    HitObject.Angle,
                    linkedSide,
                    HitObject.SyncedNoteAngle);
            }

            if (playfield != null)
                syncedNoteLink.Presentation = playfield.ChordLinkPresentation;
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

        protected override void UpdateInitialTransforms() => this.Show();

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
