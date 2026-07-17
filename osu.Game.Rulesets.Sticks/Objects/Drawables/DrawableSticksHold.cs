// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Utils;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksHold : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable, ISticksTrackingSource
    {
        private readonly SticksArcMarker headMarker;
        private readonly SmoothPath durationRail;
        private readonly Circle durationCursor;
        private readonly PausableSkinnableSound holdingSample;
        private readonly Container nestedHitObjectContainer;
        private readonly Container approachVisuals;
        private readonly SticksTrackingEligibility trackingEligibility = new SticksTrackingEligibility();
        private SticksSyncedNoteLink syncedNoteLink;
        private Vector2 railStart;
        private Vector2 railEnd;
        private StickSide displayedSide;
        private float displayedAngle = float.NaN;
        private double displayedDuration = double.NaN;
        private SticksPlayfield playfield = null!;
        private DrawableSticksHoldHead drawableHead = null!;
        private bool headJudged;
        private bool headHit;
        private bool headSamplePlayed;
        private double previousEditorTime = double.NaN;
        private float visualRadialOffset;
        private bool visualRadialOffsetInitialised;

        [Resolved(CanBeNull = true)]
        private Editor editor { get; set; }

        [Resolved(CanBeNull = true)]
        private Player player { get; set; }

        public new SticksHold HitObject => (SticksHold)base.HitObject;

        public override bool HandlePositionalInput => false;

        public override bool DisplayResult => false;

        public override IEnumerable<HitSampleInfo> GetSamples() => HitObject.CreatePlayableSamples();

        internal bool HeadJudged => headJudged;

        public bool TrackingAuthorised => trackingEligibility.IsAuthorised;

        public Vector2 RailStart
        {
            get
            {
                refreshGeometry();
                return railStart;
            }
        }

        public Vector2 RailEnd
        {
            get
            {
                refreshGeometry();
                return railEnd;
            }
        }

        public DrawableSticksHold(SticksHold hitObject)
            : base(hitObject)
        {
            Size = new Vector2(SticksPlayfield.SIZE);

            AddInternal(nestedHitObjectContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                AlwaysPresent = true,
                Depth = -20,
            });

            AddInternal(approachVisuals = new Container
            {
                RelativeSizeAxes = Axes.Both,
                AlwaysPresent = true,
                Children = new Drawable[]
                {
                    durationRail = new SmoothPath
                    {
                        AutoSizeAxes = Axes.None,
                        Size = new Vector2(SticksPlayfield.SIZE),
                        PathRadius = 4,
                        Colour = colourFor(hitObject.Side),
                        Alpha = 0,
                        Depth = 10,
                        Vertices = new[] { railStart, railEnd },
                    },
                    durationCursor = new Circle
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.Centre,
                        Position = railEnd,
                        Size = new Vector2(11),
                        Colour = Color4.White,
                        Alpha = 0,
                        Depth = 5,
                    },
                    headMarker = createHeadMarker(),
                },
            });

            AddInternal(holdingSample = new PausableSkinnableSound
            {
                Looping = true,
                MinimumSampleVolume = MINIMUM_SAMPLE_VOLUME,
            });

            ensureSyncedNoteLink();
            refreshGeometry();
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield sticksPlayfield)
        {
            playfield = sticksPlayfield;
            trackingEligibility.Reset(playfield.FlickSequence(HitObject.Side));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Native judgement results rewind automatically in the editor, but the hold's
            // parallel gesture/audio state is custom and must follow the head result explicitly.
            OnRevertResult += (drawable, _) =>
            {
                if (drawable is DrawableSticksHoldHead)
                    ResetEditorPreviewState(playfield.FlickSequence(HitObject.Side));
            };
        }

        protected override void Update()
        {
            base.Update();

            updateVisualRadialOffset();
            float radians = HitObject.Angle * MathF.PI / 180;
            approachVisuals.Position = new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * visualRadialOffset;
            refreshGeometry();

            double now = Time.Current;
            updateEditorState(now);
            bool active = now >= HitObject.StartTime && now <= HitObject.EndTime;
            double approachProgress = Math.Clamp((now - (HitObject.StartTime - HitObject.ApproachDuration)) / Math.Max(1, HitObject.ApproachDuration), 0, 1);
            double headGrowth = SticksHitObject.ApproachGrowthProgress(approachProgress);
            updateSyncedNoteLink(now, headGrowth);
            headMarker.Span = HitObject.PrimaryHitAngle * (float)(0.2 + 0.8 * headGrowth);

            double progress = Math.Clamp((now - HitObject.StartTime) / Math.Max(1, HitObject.Duration), 0, 1);
            durationRail.Alpha = now < HitObject.EndTime ? 0.38f : 0;
            durationCursor.Alpha = active ? 0.9f : 0;
            durationCursor.Position = Vector2.Lerp(railEnd, railStart, (float)progress);

            updateHeadJudgement(now);
            headMarker.Alpha = HeadMarkerAlphaAt(now, HitObject.EndTime);

            // Like a standard slider, tracking can resume after the head or any intermediate
            // checkpoint was missed. Only checkpoints crossed while away are lost.
            bool currentlyTracking = active && TrackingAuthorised && isStickInRange();
            updateHoldingSample(currentlyTracking && !Judged);
        }

        private void updateVisualRadialOffset()
        {
            if (Time.Current < HitObject.StartTime - HitObject.ApproachDuration)
            {
                visualRadialOffsetInitialised = false;
                return;
            }

            float targetOffset = playfield.VisualRadialOffsetFor(this, HitObject);

            if (playfield.RadialNoteApproach || !visualRadialOffsetInitialised)
            {
                visualRadialOffsetInitialised = true;
                visualRadialOffset = targetOffset;
                return;
            }

            visualRadialOffset = (float)Interpolation.DampContinuously(visualRadialOffset, targetOffset, 45, Math.Abs(Time.Elapsed));
        }

        private void refreshGeometry()
        {
            bool sideChanged = !float.IsNaN(displayedAngle) && displayedSide != HitObject.Side;
            if (!sideChanged
                && Math.Abs(displayedAngle - HitObject.Angle) < 0.001f
                && Math.Abs(displayedDuration - HitObject.Duration) < 0.001)
                return;

            float laneRadius = SticksPlayfield.RadiusFor(HitObject.Side);
            float railLength = (float)Math.Clamp(HitObject.Duration * 0.06, 40, 130);
            float farRadius = laneRadius + (HitObject.Side == StickSide.Left ? railLength : -railLength);
            railStart = SticksPlayfield.PointAt(HitObject.Angle, laneRadius);
            railEnd = SticksPlayfield.PointAt(HitObject.Angle, farRadius);

            durationRail.Colour = colourFor(HitObject.Side);
            durationRail.Vertices = new[] { railStart, railEnd };
            durationCursor.Position = railEnd;

            if (sideChanged)
            {
                headMarker.SetLane(HitObject.Side, colourFor(HitObject.Side));
                trackingEligibility.Reset(playfield.FlickSequence(HitObject.Side));
            }

            headMarker.Angle = HitObject.Angle;

            displayedSide = HitObject.Side;
            displayedAngle = HitObject.Angle;
            displayedDuration = HitObject.Duration;
        }

        private SticksArcMarker createHeadMarker() => new SticksArcMarker(HitObject.Side, colourFor(HitObject.Side), true)
        {
            Angle = HitObject.Angle,
            Span = HitObject.PrimaryHitAngle * 0.2f,
        };

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
        }

        private void updateSyncedNoteLink(double now, double headGrowth)
        {
            ensureSyncedNoteLink();

            if (syncedNoteLink != null && HitObject.SyncedNoteSide.HasValue)
                syncedNoteLink.Alpha = SticksSyncedNoteLink.AlphaAtHeadCue(now, HitObject.StartTime, headGrowth);
        }

        private void updateHeadJudgement(double now)
        {
            if (Judged)
                return;

            long sequence = playfield.FlickSequence(HitObject.Side);
            SticksInputTracker.FlickEvent flick = playfield.LastFlick(HitObject.Side);
            double offset = flick.Time - HitObject.StartTime;
            HitResult headTimingResult = drawableHead.HitObject.HitWindows?.ResultFor(offset) ?? HitResult.Great;
            bool canAttemptHead = !headJudged
                                  && headTimingResult.IsHit();

            bool sawNewGesture = trackingEligibility.Observe(
                    sequence,
                    flick,
                    HitObject.StartTime - SticksFlick.EARLY_HIT_WINDOW,
                    HitObject.EndTime,
                    HitObject.Angle,
                    HitObject.LenientHalfAngle,
                    out bool canAuthoriseTracking);
            bool canStartTracking = canAuthoriseTracking
                                    && (canAttemptHead || flick.Time >= HitObject.StartTime);

            if (sawNewGesture
                && (canAttemptHead || canStartTracking)
                && (canAttemptHead
                    ? playfield.TryConsumeHeadFlick(this, HitObject.Side, flick.Sequence)
                    : playfield.TryConsumeTrackingFlick(HitObject.Side, flick.Sequence)))
            {
                if (canStartTracking)
                    trackingEligibility.Authorise();

                if (canAttemptHead)
                {
                    float angleError = Math.Abs(SticksHitObject.DeltaAngle(flick.Angle, HitObject.Angle));
                    headJudged = true;
                    drawableHead.ApplyHead(offset, angleError);
                    headHit = drawableHead.BothComponentsHit;

                    if (headHit)
                        playHeadSample();
                }
            }

            double timeOffset = now - HitObject.StartTime;
            if (!headJudged
                && drawableHead.HitObject.HitWindows is not null
                && !drawableHead.HitObject.HitWindows.CanBeHit(timeOffset))
                MarkHeadMiss();
        }

        internal void MarkHeadMiss()
        {
            if (headJudged)
                return;

            headJudged = true;
            drawableHead?.ApplyMiss();
        }

        public static float HeadMarkerAlphaAt(double time, double endTime) => time <= endTime ? 1 : 0;

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (Judged || Time.Current < HitObject.EndTime)
                return;

            updateHoldingSample(false);

            // Short converted holds can end while their head is still inside its late miss
            // window. Resolve that head before the parent so its two accuracy components cannot
            // remain invisibly outstanding after the hold has disappeared.
            MarkHeadMiss();

            // The independently-scored head, ticks and tail own all hold gameplay results.
            ApplyMaxResult();
        }

        private void playHeadSample()
        {
            if (headSamplePlayed)
                return;

            headSamplePlayed = true;
            Samples.Volume.Value = 1;
            Samples.Frequency.Value = 1;
            base.PlaySamples();
        }

        private void updateEditorState(double now)
        {
            if (editor == null)
                return;

            bool rewoundBeforeHead = !double.IsNaN(previousEditorTime)
                                      && now < previousEditorTime
                                      && now < HitObject.StartTime;
            bool nativeHeadResultWasReverted = headJudged
                                               && drawableHead != null
                                               && !drawableHead.Judged;

            if (rewoundBeforeHead || nativeHeadResultWasReverted)
                ResetEditorPreviewState(playfield.FlickSequence(HitObject.Side));

            // Compose preview has no Player and uses an autoplay replay only to animate gameplay.
            // Play the authored head sample deterministically when crossing the object. F5 test
            // play does have a Player, so misses there must remain silent like normal gameplay.
            if (player == null
                && !headSamplePlayed
                && DrawableSticksSlider.CrossedStartTime(previousEditorTime, now, HitObject.StartTime))
                playHeadSample();

            previousEditorTime = now;
        }

        internal void ResetEditorPreviewState(long currentSequence)
        {
            headSamplePlayed = false;
            headJudged = false;
            headHit = false;
            trackingEligibility.Reset(currentSequence);
            updateHoldingSample(false);
        }

        protected override double InitialLifetimeOffset => HitObject.ApproachDuration;

        protected override void OnApply()
        {
            base.OnApply();

            // Editing a hold re-applies it with newly-created nested drawables. Do not attach
            // that fresh head to state retained by the previous preview pass.
            if (editor != null)
                ResetEditorPreviewState(playfield != null ? playfield.FlickSequence(HitObject.Side) : 0);
        }

        void ISticksApproachRateAdjustable.RefreshApproachTransforms()
        {
            if (Judged)
                return;

            LifetimeStart = HitObject.StartTime - InitialLifetimeOffset;
            UpdateState(State.Value, true);
        }

        protected override void AddNestedHitObject(DrawableHitObject hitObject)
        {
            base.AddNestedHitObject(hitObject);
            nestedHitObjectContainer.Add(hitObject);

            if (hitObject is DrawableSticksHoldHead head)
                drawableHead = head;
        }

        protected override void ClearNestedHitObjects()
        {
            base.ClearNestedHitObjects();
            nestedHitObjectContainer.Clear(false);
            drawableHead = null!;
        }

        protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject) => hitObject switch
        {
            SticksHoldHead head => new DrawableSticksHoldHead(head),
            SticksHoldTick tick => new DrawableSticksHoldTick(tick),
            SticksHoldTail tail => new DrawableSticksHoldTail(tail),
            _ => base.CreateNestedHitObject(hitObject),
        };

        public override void PlaySamples()
        {
            // The head plays manually, while ticks and the tail own their feedback samples.
        }

        protected override void LoadSamples()
        {
            base.LoadSamples();

            var slidingSamples = HitObject.CreatePlayableSlidingSamples();
            if (slidingSamples.Count == 0)
                slidingSamples.Add(HitObject.CreateHitSampleInfo("sliderslide"));

            holdingSample.Samples = slidingSamples.Cast<ISampleInfo>().ToArray();
        }

        public override void StopAllSamples()
        {
            base.StopAllSamples();
            holdingSample?.Stop();
        }

        protected override void OnFree()
        {
            // Clear RequestedPlaying before discarding the custom loop's samples. Otherwise an
            // editor re-apply can reload them while the sound still claims to be playing and it
            // will never start again.
            if (editor != null)
                ResetEditorPreviewState(playfield != null ? playfield.FlickSequence(HitObject.Side) : 0);
            else
                updateHoldingSample(false);

            base.OnFree();
            holdingSample?.ClearSamples();
        }

        private void updateHoldingSample(bool shouldPlay)
        {
            if (shouldPlay)
            {
                if (!holdingSample.RequestedPlaying)
                    holdingSample.Play();
            }
            else if (holdingSample.IsPlaying || holdingSample.RequestedPlaying)
            {
                holdingSample.Stop();
            }
        }

        private bool isStickInRange()
        {
            Vector2 stick = playfield.StickVector(HitObject.Side);
            float actualAngle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Y, stick.X) * 180 / MathF.PI);
            float angleError = Math.Abs(SticksHitObject.DeltaAngle(actualAngle, HitObject.Angle));
            return playfield.IsStickBeyondRechargeBoundary(HitObject.Side) && angleError <= HitObject.LenientHalfAngle;
        }

        protected override void UpdateInitialTransforms() => this.Show();

        protected override void UpdateHitStateTransforms(ArmedState state)
        {
            if (state == ArmedState.Hit)
                this.FadeOut(180).Expire();
            else
                this.FadeColour(Color4.Gray, 100).FadeOut(240).Expire();
        }

        private static Color4 colourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;
    }
}
