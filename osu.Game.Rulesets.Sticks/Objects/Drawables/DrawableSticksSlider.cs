// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Utils;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksSlider : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable, ISticksTrackingSource, ISticksVisualRadialOffsetSource
    {
        public const float REVERSAL_PREVIEW_ALPHA = 0.28f;

        private const float path_half_thickness = 7;
        private const float reversal_outline_half_thickness = 9;
        private const float direction_preview_half_thickness = 4;
        private const float reversal_direction_outline_half_thickness = 6;

        private readonly CircularProgress path;
        private readonly CircularProgress reversalPathPreview;
        private readonly CircularProgress reversalPathPreviewOutline;
        private readonly CircularProgress reversalOutline;
        private readonly CircularProgress reversalPreviewOutline;
        private readonly CircularProgress directionPreview;
        private readonly SticksArcMarker trackingMarker;
        private readonly SticksSliderHeadMarker headMarker;
        private readonly Container nestedHitObjectContainer;
        private readonly SticksTrackingEligibility trackingEligibility = new SticksTrackingEligibility();
        private SticksSyncedNoteLink syncedNoteLink;
        private SticksPlayfield playfield = null!;
        private DrawableSticksSliderHead drawableHead = null!;
        private bool headJudged;
        private bool headHit;
        private StickSide? displayedSide;
        private bool headSamplePlayed;
        private double previousEditorTime = double.NaN;
        private float visualRadialOffset;
        private bool visualRadialOffsetInitialised;

        [Resolved(CanBeNull = true)]
        private Editor editor { get; set; }

        [Resolved(CanBeNull = true)]
        private Player player { get; set; }

        public new SticksSlider HitObject => (SticksSlider)base.HitObject;

        public override bool HandlePositionalInput => false;

        public override bool DisplayResult => false;

        public override IEnumerable<HitSampleInfo> GetSamples() => HitObject.CreatePlayableSamples();

        internal bool HeadHit => headHit;

        internal bool HeadJudged => headJudged;

        internal bool HasResult => Judged;

        /// <summary>
        /// Whether any not-yet-erased portion of this earlier slider will cross an approaching
        /// head's angular footprint. Future path is treated as fully extended rather than waiting
        /// for the snake animation to reach the head, preventing the head from spawning in a slot
        /// which is already scheduled to become occupied.
        /// </summary>
        internal bool FuturePathObstructsHeadAt(double time, float headAngle, float headHalfSpan)
        {
            if (HitObject.Side is not (StickSide.Left or StickSide.Right)
                || time < HitObject.StartTime - HitObject.ApproachDuration
                || time > HitObject.EndTime)
                return false;

            int firstFutureSegment;
            double firstFutureRangeStart;

            if (time < HitObject.StartTime)
            {
                firstFutureSegment = 0;
                firstFutureRangeStart = 0;
            }
            else
            {
                firstFutureSegment = HitObject.SegmentIndexAt(time);
                firstFutureRangeStart = HitObject.SegmentProgressAt(time);
            }

            for (int segment = firstFutureSegment; segment < HitObject.SegmentCount; segment++)
            {
                double rangeStart = segment == firstFutureSegment ? firstFutureRangeStart : 0;
                if (angleFallsWithinVisibleRange(
                    headAngle,
                    headHalfSpan,
                    HitObject.SegmentStartAngleAt(segment),
                    HitObject.SegmentArcAngleAt(segment),
                    rangeStart,
                    1))
                    return true;
            }

            return false;
        }

        public bool TrackingAuthorised => trackingEligibility.IsAuthorised;

        internal float VisualRadialOffset => visualRadialOffset;

        float ISticksVisualRadialOffsetSource.VisualRadialOffset => visualRadialOffset;

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

            // This is deliberately behind the opaque active path. As the played portion is
            // erased, the translucent upcoming reversal is revealed without covering it.
            AddInternal(reversalPathPreviewOutline = createArc(hitObject, reversal_outline_half_thickness, 0, 10, Color4.White));

            AddInternal(reversalPathPreview = createArc(hitObject, path_half_thickness, 0, 9));

            AddInternal(reversalPreviewOutline = createArc(hitObject, reversal_direction_outline_half_thickness, 0, 11, Color4.White));

            AddInternal(directionPreview = createArc(hitObject, direction_preview_half_thickness, 0.28f, 10));

            AddInternal(reversalOutline = createArc(hitObject, reversal_outline_half_thickness, 0, 6, Color4.White));

            AddInternal(path = createArc(hitObject, path_half_thickness, 1, 5));

            AddInternal(headMarker = new SticksSliderHeadMarker(hitObject.Side, hitObject.InitialDirection, colourFor(hitObject.Side), true)
            {
                Angle = hitObject.Angle,
                Span = hitObject.PrimaryHitAngle * 0.2f,
                Alpha = 0,
                Depth = -11,
            });

            AddInternal(trackingMarker = new SticksArcMarker(hitObject.Side, colourFor(hitObject.Side))
            {
                Angle = hitObject.Angle,
                Span = hitObject.PrimaryHitAngle * 0.35f,
                Alpha = 0,
                Depth = -12,
            });

            ensureSyncedNoteLink();
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield sticksPlayfield)
        {
            playfield = sticksPlayfield;
            trackingEligibility.Reset(playfield.FlickSequence(HitObject.Side));
        }

        protected override void Update()
        {
            base.Update();

            refreshEditorGeometry();
            updateVisualRadialOffset();

            double now = Time.Current;
            bool active = now >= HitObject.StartTime && now <= HitObject.EndTime;
            updateEditorHeadSample(now);
            double cueDuration = HitObject.ApproachDuration;
            bool cueActive = now >= HitObject.StartTime - cueDuration && now < HitObject.StartTime;
            double cueProgress = Math.Clamp((now - (HitObject.StartTime - cueDuration)) / Math.Max(1, cueDuration), 0, 1);
            double headGrowth = SticksHitObject.ApproachGrowthProgress(cueProgress);
            updateSyncedNoteLink(now, headGrowth);
            headMarker.Span = HitObject.PrimaryHitAngle * (float)(0.2 + 0.8 * headGrowth);
            (double remainingStart, double remainingEnd) = HitObject.RemainingPathRangeAt(now);
            int segmentIndex = active ? HitObject.SegmentIndexAt(now) : 0;
            setVisibleRange(path, HitObject.SegmentStartAngleAt(segmentIndex), HitObject.SegmentArcAngleAt(segmentIndex), remainingStart, remainingEnd);
            path.Alpha = active ? 1 : 0;
            updateReversalPathPreview(now, active);
            updateDirectionPreview(now, cueActive, active);
            updateReversalOutline(now, cueActive, active, segmentIndex, remainingStart, remainingEnd);

            updateHeadCue(now, cueActive);

            trackingMarker.Angle = HitObject.AngleAt(now);
            trackingMarker.Alpha = active ? 1 : 0;

            updateHeadJudgement(now);
        }

        private void refreshEditorGeometry()
        {
            Color4 colour = colourFor(HitObject.Side);

            if (displayedSide != HitObject.Side)
            {
                headMarker.SetLaneAndDirection(HitObject.Side, HitObject.InitialDirection, colour);
                trackingMarker.SetLane(HitObject.Side, colour);
                displayedSide = HitObject.Side;
                applyVisualRadialOffset(visualRadialOffset);
                trackingEligibility.Reset(playfield.FlickSequence(HitObject.Side));
            }

            if (headMarker.Direction != HitObject.InitialDirection)
                headMarker.SetLaneAndDirection(HitObject.Side, HitObject.InitialDirection, colour);
            headMarker.Angle = HitObject.Angle;
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
                applyVisualRadialOffset(targetOffset);
                return;
            }

            float nextOffset = (float)Interpolation.DampContinuously(visualRadialOffset, targetOffset, 45, Math.Abs(Time.Elapsed));
            if (Math.Abs(nextOffset - visualRadialOffset) < 0.001f)
                return;

            applyVisualRadialOffset(nextOffset);
        }

        internal void ApplyVisualRadialOffsetForTesting(float offset)
        {
            visualRadialOffsetInitialised = true;
            applyVisualRadialOffset(offset);
        }

        private void applyVisualRadialOffset(float offset)
        {
            visualRadialOffset = offset;
            Color4 colour = colourFor(HitObject.Side);

            updateArcLane(path, HitObject.Side, path_half_thickness, colour, offset);
            updateArcLane(reversalPathPreview, HitObject.Side, path_half_thickness, colour, offset);
            updateArcLane(reversalPathPreviewOutline, HitObject.Side, reversal_outline_half_thickness, Color4.White, offset);
            updateArcLane(directionPreview, HitObject.Side, direction_preview_half_thickness, colour, offset);
            updateArcLane(reversalOutline, HitObject.Side, reversal_outline_half_thickness, Color4.White, offset);
            updateArcLane(reversalPreviewOutline, HitObject.Side, reversal_direction_outline_half_thickness, Color4.White, offset);
            headMarker.SetRadialOffset(offset, true);
            trackingMarker.SetRadialOffset(offset, true);
        }

        private void updateReversalPathPreview(double now, bool active)
        {
            reversalPathPreview.Alpha = 0;
            reversalPathPreviewOutline.Alpha = 0;
            reversalPathPreviewOutline.Progress = 0;

            int upcomingSegment = active ? HitObject.UpcomingSegmentIndexAt(now) : -1;
            double previewProgress = upcomingSegment >= 0
                ? HitObject.UpcomingSegmentPreviewProgressAt(now)
                : 0;

            if (previewProgress <= 0)
                return;

            setVisibleRange(
                reversalPathPreview,
                HitObject.SegmentStartAngleAt(upcomingSegment),
                HitObject.SegmentArcAngleAt(upcomingSegment),
                0,
                previewProgress);
            reversalPathPreview.Alpha = REVERSAL_PREVIEW_ALPHA;

            // The transparent segment preview always snakes in, but the white treatment belongs
            // only to a segment which itself ends at another reversal. Keeping zero geometry for
            // an unoutlined segment prevents the previous segment's arc from lingering visually.
            if (HitObject.SegmentEndsWithReversal(upcomingSegment))
            {
                setVisibleRange(
                    reversalPathPreviewOutline,
                    HitObject.SegmentStartAngleAt(upcomingSegment),
                    HitObject.SegmentArcAngleAt(upcomingSegment),
                    0,
                    previewProgress);
                reversalPathPreviewOutline.Alpha = 1;
            }
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

        private void updateSyncedNoteLink(double now, double headGrowth)
        {
            ensureSyncedNoteLink();

            if (syncedNoteLink != null && HitObject.SyncedNoteSide.HasValue)
                syncedNoteLink.Alpha = SticksSyncedNoteLink.AlphaAtHeadCue(now, HitObject.StartTime, headGrowth);
        }

        private void updateDirectionPreview(double now, bool cueActive, bool active)
        {
            double rehearsalProgress = cueActive && now >= HitObject.RehearsalStartTime
                ? HitObject.RehearsalProgressAt(now)
                : 0;
            setVisibleRange(directionPreview, HitObject.Angle, HitObject.SegmentArcAngleAt(0), 0, active ? 1 : rehearsalProgress);

            directionPreview.Alpha = active
                ? 0
                : rehearsalProgress > 0 ? (float)(0.18 + rehearsalProgress * 0.1) : 0;
        }

        private void updateReversalOutline(double now, bool cueActive, bool active, int segmentIndex, double remainingStart, double remainingEnd)
        {
            reversalOutline.Alpha = 0;
            reversalOutline.Progress = 0;
            reversalPreviewOutline.Alpha = 0;
            reversalPreviewOutline.Progress = 0;

            if ((!cueActive && !active) || !HitObject.SegmentEndsWithReversal(segmentIndex))
                return;

            if (active)
            {
                setVisibleRange(reversalOutline, HitObject.SegmentStartAngleAt(segmentIndex), HitObject.SegmentArcAngleAt(segmentIndex), remainingStart, remainingEnd);
                reversalOutline.Alpha = 1;
                return;
            }

            double rehearsalProgress = now >= HitObject.RehearsalStartTime
                ? HitObject.RehearsalProgressAt(now)
                : 0;
            setVisibleRange(reversalPreviewOutline, HitObject.Angle, HitObject.SegmentArcAngleAt(0), 0, rehearsalProgress);
            reversalPreviewOutline.Alpha = rehearsalProgress > 0 ? 1 : 0;
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

            if (!double.IsNaN(previousEditorTime) && now < previousEditorTime && now < HitObject.StartTime)
            {
                headSamplePlayed = false;
                headJudged = false;
                headHit = false;
            }

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

        protected override double InitialLifetimeOffset => HitObject.ApproachDuration;

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

        protected override void UpdateInitialTransforms() => this.Show();

        protected override void UpdateHitStateTransforms(ArmedState state)
        {
            if (state == ArmedState.Hit)
                this.FadeOut(180).Expire();
            else if (state == ArmedState.Miss)
                this.FadeColour(Color4.Gray, 100).FadeOut(260).Expire();
        }

        private static CircularProgress createArc(SticksSlider slider, float halfThickness, float alpha, float depth, Color4? colour = null)
        {
            float radius = SticksPlayfield.RadiusFor(slider.Side);
            float outerRadius = radius + halfThickness;
            return new CircularProgress
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = new Vector2(SticksPlayfield.SIZE / 2),
                Size = new Vector2(outerRadius * 2),
                InnerRadius = 2 * halfThickness / outerRadius,
                RoundedCaps = true,
                Colour = colour ?? colourFor(slider.Side),
                Alpha = alpha,
                Depth = depth,
            };
        }

        private static void updateArcLane(CircularProgress drawable, StickSide side, float halfThickness, Color4 colour, float radialOffset = 0)
        {
            float radius = SticksPlayfield.RadiusFor(side) + radialOffset;
            float outerRadius = radius + halfThickness;
            Vector2 size = new Vector2(outerRadius * 2);

            if (drawable.Size != size)
            {
                drawable.Size = size;
                drawable.InnerRadius = 2 * halfThickness / outerRadius;
            }

            drawable.Colour = colour;
        }

        private static void setVisibleRange(CircularProgress drawable, float segmentStartAngle, float segmentArcAngle, double start, double end)
        {
            start = Math.Clamp(start, 0, 1);
            end = Math.Clamp(end, start, 1);
            double clockwiseStart = segmentArcAngle >= 0
                ? segmentStartAngle + segmentArcAngle * start
                : segmentStartAngle + segmentArcAngle * end;
            drawable.Rotation = (float)(90 + clockwiseStart);
            drawable.Progress = (float)(Math.Abs(segmentArcAngle) * (end - start) / 360);
        }

        private static Color4 colourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;

        private static bool angleFallsWithinVisibleRange(float angle, float halfSpan, float segmentStartAngle, float segmentArcAngle, double rangeStart, double rangeEnd)
        {
            rangeStart = Math.Clamp(rangeStart, 0, 1);
            rangeEnd = Math.Clamp(rangeEnd, 0, 1);
            if (rangeEnd <= rangeStart)
                return false;

            double visibleStart = segmentStartAngle + segmentArcAngle * rangeStart;
            double visibleEnd = segmentStartAngle + segmentArcAngle * rangeEnd;
            double lower = Math.Min(visibleStart, visibleEnd) - Math.Max(0, halfSpan);
            double upper = Math.Max(visibleStart, visibleEnd) + Math.Max(0, halfSpan);

            // Compare against every equivalent revolution without iterating through long,
            // multi-turn slider arcs.
            double firstEquivalent = Math.Ceiling((lower - angle) / 360);
            double lastEquivalent = Math.Floor((upper - angle) / 360);
            return firstEquivalent <= lastEquivalent;
        }
    }
}
