using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
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
    public partial class DrawableSticksSlider : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable, ISticksTrackingSource
    {
        private readonly SticksSliderHeadMarker headMarker;
        private readonly SticksRadialTimelinePath radialPath;
        private readonly SticksSliderContactEffect sliderContactEffect;
        private readonly PausableSkinnableSound holdingSample;
        private readonly Container nestedHitObjectContainer;
        private readonly SticksTrackingEligibility trackingEligibility = new SticksTrackingEligibility();
        private SticksPlayfield playfield = null!;
        private DrawableSticksSliderHead drawableHead = null!;
        private bool headJudged;
        private bool headHit;
        private StickSide? displayedSide;
        private bool headSamplePlayed;
        private double previousEditorTime = double.NaN;
        private bool radialPathRegistered;

        [Resolved(CanBeNull = true)]
        private Editor editor { get; set; }

        [Resolved(CanBeNull = true)]
        private Player player { get; set; }

        public new SticksSlider HitObject => (SticksSlider)base.HitObject;

        public override bool HandlePositionalInput => false;

        public override bool DisplayResult => false;

        public override IEnumerable<HitSampleInfo> GetSamples() => HitObject.NodeSamples.Count > 0 && HitObject.NodeSamples[0].Count > 0
            ? SticksHitObject.CreatePlayableSamples(HitObject.NodeSamples[0])
            : HitObject.CreatePlayableSamples();

        internal bool HeadHit => headHit;

        internal bool HeadJudged => headJudged;

        internal bool HasResult => Judged;

        public bool TrackingAuthorised => trackingEligibility.IsAuthorised;

        public DrawableSticksSlider(SticksSlider hitObject)
            : base(hitObject)
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            Position = Vector2.Zero;

            AddInternal(nestedHitObjectContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                AlwaysPresent = true,
                Depth = -20,
            });

            AddInternal(radialPath = new SticksRadialTimelinePath(hitObject.Side)
            {
                Alpha = 0,
                Depth = 4,
            });

            AddInternal(sliderContactEffect = new SticksSliderContactEffect(
                colourFor(hitObject.Side),
                hitObject.StartTime * 0.00031 + hitObject.Angle * 0.017)
            {
                Alpha = 0,
                Depth = -25,
            });

            AddInternal(headMarker = new SticksSliderHeadMarker(hitObject.Side, hitObject.InitialDirection, colourFor(hitObject.Side), true)
            {
                Angle = hitObject.Angle,
                Span = hitObject.PrimaryHitAngle,
                Alpha = 0,
                Depth = -11,
            });

            AddInternal(holdingSample = new PausableSkinnableSound
            {
                Looping = true,
                MinimumSampleVolume = MINIMUM_SAMPLE_VOLUME,
            });
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

            RemoveInternal(radialPath, false);

            // The editor rewinds nested results independently of this parent's
            // custom head and tracking state, just as it does for holds.
            OnRevertResult += (drawable, _) =>
            {
                if (drawable is DrawableSticksSliderHead)
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

            base.Dispose(isDisposing);
        }

        protected override void Update()
        {
            base.Update();

            refreshEditorGeometry();

            double now = Time.Current;
            bool active = now >= HitObject.StartTime && now <= HitObject.EndTime;
            updateEditorHeadSample(now);
            double cueDuration = HitObject.ApproachDuration;
            bool cueActive = now >= HitObject.StartTime - cueDuration && now < HitObject.StartTime;
            headMarker.Span = HitObject.PrimaryHitAngle;
            float radius = SticksPlayfield.GUIDE_RADIUS * SticksPlayfield.CenterOutProgressAt(now, HitObject.StartTime, HitObject.ApproachDuration);
            headMarker.SetRadialOffset(radius - SticksPlayfield.RadiusFor(HitObject.Side), true);
            setRadialPathRegistered(now < HitObject.EndTime);
            if (now < HitObject.EndTime)
                radialPath.SetSliderGeometry(HitObject, now);

            updateHeadCue(now, cueActive);

            updateHeadJudgement(now);

            bool tracking = active && TrackingAuthorised && isStickInRange(now);
            updateHoldingSample(HitObject.IsStationary && tracking && !Judged);

            radialPath.SetTrackingState(tracking, tracking ? HitObject.BeatPulseAt(now) : 0);

            bool showSparks = playfield.SliderTrackingSparks && tracking;
            float trackingAngle = showSparks
                ? HitObject.AngleAt(Math.Clamp(now, HitObject.StartTime, HitObject.EndTime))
                : 0;

            sliderContactEffect.SetState(
                showSparks,
                now,
                trackingAngle,
                HitObject.PrimaryHitAngle,
                colourFor(HitObject.Side));
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

            // This path lives in the shared buffer rather than under the hit object.
            // Its own lifetime must hide stale geometry when a seek skips the parent.
            radialPath.LifetimeStart = HitObject.StartTime - HitObject.ApproachDuration;
            radialPath.LifetimeEnd = HitObject.EndTime;
        }

        private bool isStickInRange(double now)
        {
            Vector2 stick = playfield.StickVector(HitObject.Side);

            if (stick.LengthSquared <= 0 || !playfield.IsStickBeyondRechargeBoundary(HitObject.Side))
                return false;

            float actualAngle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Y, stick.X) * 180 / MathF.PI);
            float targetAngle = HitObject.AngleAt(Math.Clamp(now, HitObject.StartTime, HitObject.EndTime));
            return Math.Abs(SticksHitObject.DeltaAngle(actualAngle, targetAngle)) <= HitObject.LenientHalfAngle;
        }

        private void refreshEditorGeometry()
        {
            Color4 colour = colourFor(HitObject.Side);

            headMarker.SetLaneAndDirection(HitObject.Side, HitObject.InitialDirection, colour);
            if (displayedSide != HitObject.Side)
            {
                displayedSide = HitObject.Side;
                trackingEligibility.Reset(playfield.FlickSequence(HitObject.Side));
            }

            if (headMarker.Direction != HitObject.InitialDirection)
                headMarker.SetLaneAndDirection(HitObject.Side, HitObject.InitialDirection, colour);
            headMarker.Angle = HitObject.Angle;
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
            float trackingAngle = canAttemptHead
                ? HitObject.Angle
                : HitObject.AngleAt(Math.Clamp(flick.Time, HitObject.StartTime, HitObject.EndTime));

            bool sawNewGesture = trackingEligibility.Observe(
                    sequence,
                    flick,
                    HitObject.StartTime - SticksFlick.EARLY_HIT_WINDOW,
                    HitObject.EndTime,
                    trackingAngle,
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

        private void updateHeadCue(double now, bool cueActive)
        {
            if (HitObject.IsStationary)
            {
                headMarker.Alpha = now >= HitObject.StartTime - HitObject.ApproachDuration && now <= HitObject.EndTime ? 1 : 0;
                return;
            }

            if (cueActive)
            {
                headMarker.Alpha = 1;
                return;
            }

            double timeSinceStart = now - HitObject.StartTime;
            if (timeSinceStart >= 0 && timeSinceStart <= 120)
            {
                float flashProgress = (float)(timeSinceStart / 120);
                headMarker.Alpha = 1 - flashProgress;
                return;
            }

            headMarker.Alpha = 0;
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (Judged || Time.Current < HitObject.EndTime)
                return;

            updateHoldingSample(false);

            // A converted slider may be shorter than the head's late miss window. Once the
            // parent resolves it no longer checks head input, so close any still-open head first
            // rather than leaving its timing and angle components permanently unjudged.
            MarkHeadMiss();

            // Modern standard-style slider scoring lives entirely on the independent head,
            // ticks, reversals and tail. The parent only resolves its visual lifetime.
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

        private void updateEditorHeadSample(double now)
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

            // Only compose preview receives an automatic authored sample. In F5 editor test
            // play, actual head acquisition must remain authoritative just like normal play.
            if (player == null
                && !headSamplePlayed
                && CrossedStartTime(previousEditorTime, now, HitObject.StartTime))
                playHeadSample();

            previousEditorTime = now;
        }

        public static bool CrossedStartTime(double previousTime, double currentTime, double startTime) =>
            !double.IsNaN(previousTime)
            && currentTime >= previousTime
            && previousTime < startTime
            && currentTime >= startTime;

        internal void ResetEditorPreviewState(long currentSequence)
        {
            updateHoldingSample(false);
            headSamplePlayed = false;
            headJudged = false;
            headHit = false;
            trackingEligibility.Reset(currentSequence);
        }

        protected override void OnApply()
        {
            base.OnApply();
            updateRadialPathLifetime();
            if (editor != null)
                ResetEditorPreviewState(playfield != null ? playfield.FlickSequence(HitObject.Side) : 0);
        }

        protected override void OnFree()
        {
            detachRadialPath();
            if (editor != null)
                ResetEditorPreviewState(playfield != null ? playfield.FlickSequence(HitObject.Side) : 0);
            else
                updateHoldingSample(false);
            base.OnFree();
            holdingSample?.ClearSamples();
        }

        public override void OnKilled()
        {
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

        protected override double InitialLifetimeOffset => HitObject.ApproachDuration;

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

            if (hitObject is DrawableSticksSliderHead head)
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
            SticksSliderHead head => new DrawableSticksSliderHead(head),
            SticksSliderTick tick => new DrawableSticksSliderTick(tick),
            SticksSliderRepeat repeat => new DrawableSticksSliderRepeat(repeat),
            SticksSliderExtension extension => new DrawableSticksSliderExtension(extension),
            SticksSliderTail tail => new DrawableSticksSliderTail(tail),
            _ => base.CreateNestedHitObject(hitObject),
        };

        public override void PlaySamples()
        {
            // The head plays manually when acquired and the nested tail owns completion feedback.
        }

        protected override void LoadSamples()
        {
            base.LoadSamples();

            if (!HitObject.IsStationary)
            {
                updateHoldingSample(false);
                holdingSample.ClearSamples();
                return;
            }

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

        private void updateHoldingSample(bool shouldPlay)
        {
            if (holdingSample == null)
                return;

            if (shouldPlay)
            {
                if (!holdingSample.RequestedPlaying)
                    holdingSample.Play();
            }
            else if (holdingSample.IsPlaying || holdingSample.RequestedPlaying)
                holdingSample.Stop();
        }

        protected override void UpdateInitialTransforms() => this.Show();

        protected override void UpdateHitStateTransforms(ArmedState state)
        {
            if (state == ArmedState.Hit)
                this.FadeOut(180).Expire();
            else if (state == ArmedState.Miss)
                this.FadeColour(Color4.Gray, 100).FadeOut(260).Expire();
        }

        private Color4 colourFor(StickSide side) => playfield?.ColourFor(side) ?? defaultColourFor(side);

        private static Color4 defaultColourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;
    }
}
