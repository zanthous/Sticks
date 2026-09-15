using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
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
        private readonly SticksRadialTimelinePath radialPath;
        private readonly SticksSliderContactEffect holdContactEffect;
        private readonly PausableSkinnableSound holdingSample;
        private readonly Container nestedHitObjectContainer;
        private readonly SticksTrackingEligibility trackingEligibility = new SticksTrackingEligibility();
        private readonly SticksTrackingEligibility otherTrackingEligibility = new SticksTrackingEligibility();
        public StickSide TrackingSide { get; private set; }
        private StickSide displayedSide;
        private float displayedAngle = float.NaN;
        private SticksPlayfield playfield = null!;
        private DrawableSticksHoldHead drawableHead = null!;
        private bool headJudged;
        private bool headHit;
        private bool headSamplePlayed;
        private double previousEditorTime = double.NaN;
        private bool radialPathRegistered;

        [Resolved(CanBeNull = true)]
        private Editor editor { get; set; }

        [Resolved(CanBeNull = true)]
        private Player player { get; set; }

        public new SticksHold HitObject => (SticksHold)base.HitObject;

        public override bool HandlePositionalInput => false;

        public override bool DisplayResult => false;

        public override IEnumerable<HitSampleInfo> GetSamples() => HitObject.CreatePlayableSamples();

        internal bool HeadJudged => headJudged;

        public bool TrackingAuthorised => (trackingEligibility.IsAuthorised || otherTrackingEligibility.IsAuthorised)
            && (playfield?.EitherStick != true || playfield.IsTrackingOwner(this, TrackingSide));

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

            AddInternal(headMarker = createHeadMarker());

            AddInternal(radialPath = new SticksRadialTimelinePath(hitObject.Side)
            {
                Alpha = 0,
                Depth = 4,
            });

            AddInternal(holdContactEffect = new SticksSliderContactEffect(
                colourFor(hitObject.Side),
                hitObject.StartTime * 0.00031 + hitObject.Angle * 0.017)
            {
                Alpha = 0,
                Depth = -25,
            });

            AddInternal(holdingSample = new PausableSkinnableSound
            {
                Looping = true,
                MinimumSampleVolume = MINIMUM_SAMPLE_VOLUME,
            });

            refreshGeometry();
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield sticksPlayfield)
        {
            playfield = sticksPlayfield;
            trackingEligibility.Reset(playfield.FlickSequence(HitObject.Side));
            otherTrackingEligibility.Reset(playfield.FlickSequence(SticksPlayfield.OppositeSide(HitObject.Side)));
            TrackingSide = HitObject.Side;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            RemoveInternal(radialPath, false);

            // Native judgement results rewind automatically in the editor, but the hold's
            // parallel gesture/audio state is custom and must follow the head result explicitly.
            OnRevertResult += (drawable, _) =>
            {
                if (drawable is DrawableSticksHoldHead)
                    ResetEditorPreviewState(playfield.FlickSequence(HitObject.Side));
            };
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                // Loading can be cancelled before the playfield is resolved. In that
                // case the path is still our child and base disposal will clean it up.
                playfield?.RemoveRadialPath(radialPath);
                radialPathRegistered = false;
            }

            if (isDisposing)
                playfield?.ReleaseTracking(this);
            base.Dispose(isDisposing);
        }

        protected override void Update()
        {
            base.Update();

            refreshGeometry();
            headMarker.SetLane(HitObject.Side, colourFor(HitObject.Side));

            double now = Time.Current;
            updateEditorState(now);
            bool active = now >= HitObject.StartTime && now <= HitObject.EndTime;
            headMarker.Span = HitObject.PrimaryHitAngle;
            float radius = SticksPlayfield.GUIDE_RADIUS * SticksPlayfield.CenterOutProgressAt(now, HitObject.StartTime, HitObject.ApproachDuration);
            headMarker.SetRadialOffset(radius - SticksPlayfield.RadiusFor(HitObject.Side), true);

            setRadialPathRegistered(now < HitObject.EndTime);
            if (now < HitObject.EndTime)
                radialPath.SetHoldGeometry(HitObject, now);

            updateHeadJudgement(now);
            headMarker.Alpha = HeadMarkerAlphaAt(now, HitObject.EndTime);

            // Like a standard slider, tracking can resume after the head or any intermediate
            // checkpoint was missed. Only checkpoints crossed while away are lost.
            bool currentlyTracking = active && TrackingAuthorised && isStickInRange();

            radialPath.SetTrackingState(currentlyTracking, currentlyTracking ? HitObject.BeatPulseAt(now) : 0);

            holdContactEffect.SetState(
                playfield.SliderTrackingSparks && currentlyTracking,
                now,
                HitObject.Angle,
                HitObject.PrimaryHitAngle,
                colourFor(HitObject.Side));

            updateHoldingSample(currentlyTracking && !Judged);
        }

        private void setRadialPathRegistered(bool registered)
        {
            updateRadialPathLifetime();
            if (radialPathRegistered == registered)
                return;

            radialPathRegistered = registered;
            radialPath.Alpha = registered ? 1 : 0;

            if (registered)
                playfield.AddRadialPath(radialPath);
            else
                playfield.DetachRadialPath(radialPath);
        }

        private void updateRadialPathLifetime()
        {
            if (radialPath == null)
                return;

            // Buffer ownership is independent of the parent's visible lifetime.
            radialPath.LifetimeStart = HitObject.StartTime - HitObject.ApproachDuration;
            radialPath.LifetimeEnd = HitObject.EndTime;
        }

        private void refreshGeometry()
        {
            bool sideChanged = !float.IsNaN(displayedAngle) && displayedSide != HitObject.Side;
            if (!sideChanged
                && Math.Abs(displayedAngle - HitObject.Angle) < 0.001f)
                return;

            if (sideChanged)
            {
                headMarker.SetLane(HitObject.Side, colourFor(HitObject.Side));
                trackingEligibility.Reset(playfield.FlickSequence(HitObject.Side));
                otherTrackingEligibility.Reset(playfield.FlickSequence(SticksPlayfield.OppositeSide(HitObject.Side)));
                TrackingSide = HitObject.Side;
            }

            headMarker.Angle = HitObject.Angle;

            displayedSide = HitObject.Side;
            displayedAngle = HitObject.Angle;
        }

        private SticksArcMarker createHeadMarker() => new SticksArcMarker(HitObject.Side, colourFor(HitObject.Side), true)
        {
            Angle = HitObject.Angle,
            Span = HitObject.PrimaryHitAngle,
        };

        private void updateHeadJudgement(double now)
        {
            if (Judged)
                return;

            bool consumed = observeHeadInput(HitObject.Side, trackingEligibility);
            if (playfield.EitherStick && !consumed)
                observeHeadInput(SticksPlayfield.OppositeSide(HitObject.Side), otherTrackingEligibility);

            double timeOffset = now - HitObject.StartTime;
            if (!headJudged
                && drawableHead.HitObject.HitWindows is not null
                && !drawableHead.HitObject.HitWindows.CanBeHit(timeOffset))
                MarkHeadMiss();
        }

        private bool observeHeadInput(StickSide inputSide, SticksTrackingEligibility eligibility)
        {
            long sequence = playfield.FlickSequence(inputSide);
            SticksInputTracker.FlickEvent flick = playfield.LastFlick(inputSide);
            double offset = flick.Time - HitObject.StartTime;
            HitResult headTimingResult = drawableHead.HitObject.HitWindows?.ResultFor(offset) ?? HitResult.Great;
            bool canAttemptHead = !headJudged
                                  && headTimingResult.IsHit();

            bool sawNewGesture = eligibility.Observe(
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
                    ? playfield.TryConsumeHeadFlick(this, inputSide, flick.Sequence)
                    : playfield.TryConsumeTrackingFlick(inputSide, flick.Sequence)))
            {
                if (canStartTracking)
                {
                    eligibility.Authorise();
                    TrackingSide = inputSide;
                    playfield.ClaimTracking(this, inputSide);
                }

                if (canAttemptHead)
                {
                    float angleError = Math.Abs(SticksHitObject.DeltaAngle(flick.Angle, HitObject.Angle));
                    headJudged = true;
                    drawableHead.ApplyHead(offset, angleError);
                    headHit = drawableHead.BothComponentsHit;

                    if (headHit)
                        playHeadSample();
                }
                return true;
            }

            return false;
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
            otherTrackingEligibility.Reset(playfield?.FlickSequence(SticksPlayfield.OppositeSide(HitObject.Side)) ?? 0);
            TrackingSide = HitObject.Side;
            playfield?.ReleaseTracking(this);
            updateHoldingSample(false);
        }

        protected override double InitialLifetimeOffset => HitObject.ApproachDuration;

        protected override void OnApply()
        {
            base.OnApply();
            updateRadialPathLifetime();

            // Editing a hold re-applies it with newly-created nested drawables. Do not attach
            // that fresh head to state retained by the previous preview pass.
            if (editor != null)
                ResetEditorPreviewState(playfield != null ? playfield.FlickSequence(HitObject.Side) : 0);
        }

        void ISticksApproachRateAdjustable.RefreshApproachTransforms()
        {
            updateRadialPathLifetime();
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
            detachRadialPath();
            // Clear RequestedPlaying before discarding the custom loop's samples. Otherwise an
            // editor re-apply can reload them while the sound still claims to be playing and it
            // will never start again.
            if (editor != null)
                ResetEditorPreviewState(playfield != null ? playfield.FlickSequence(HitObject.Side) : 0);
            else
                updateHoldingSample(false);

            playfield?.ReleaseTracking(this);
            base.OnFree();
            holdingSample?.ClearSamples();
        }

        public override void OnKilled()
        {
            playfield?.ReleaseTracking(this);
            // Non-pooled editor objects can be removed without OnFree or Dispose.
            detachRadialPath();
            base.OnKilled();
        }

        private void detachRadialPath()
        {
            if (!radialPathRegistered)
                return;
            radialPathRegistered = false;
            radialPath.Alpha = 0;
            playfield.DetachRadialPath(radialPath);
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
            Vector2 stick = playfield.StickVector(TrackingSide);
            float actualAngle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Y, stick.X) * 180 / MathF.PI);
            float angleError = Math.Abs(SticksHitObject.DeltaAngle(actualAngle, HitObject.Angle));
            return playfield.IsStickBeyondRechargeBoundary(TrackingSide) && angleError <= HitObject.LenientHalfAngle;
        }

        protected override void UpdateInitialTransforms() => this.Show();

        protected override void UpdateHitStateTransforms(ArmedState state)
        {
            if (state == ArmedState.Hit)
                this.FadeOut(180).Expire();
            else
                this.FadeColour(Color4.Gray, 100).FadeOut(240).Expire();
        }

        private Color4 colourFor(StickSide side) => playfield?.ColourFor(side) ?? (side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR);
    }
}
