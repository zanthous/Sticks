// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.UI
{
    [Cached]
    public partial class SticksPlayfield : Playfield
    {
        public const float SIZE = 640;
        public const float GUIDE_RADIUS = 230;
        public const float LANE_OFFSET = 16;
        public const float OUTER_RADIUS = GUIDE_RADIUS + LANE_OFFSET;
        public const float INNER_RADIUS = GUIDE_RADIUS - LANE_OFFSET;
        public const float DEFAULT_RADIAL_APPROACH_DISTANCE = 30;
        public const float DEFAULT_RADIAL_APPROACH_SPEED = 1;
        public const float DEFAULT_NOTE_CIRCLE_SCALE = 1;
        public const float MIN_NOTE_CIRCLE_SCALE = 1;
        public const float MAX_NOTE_CIRCLE_SCALE = 2;
        public const float RELAX_DIRECTION_LENGTH_FRACTION = 0.25f;
        public const float CENTER_OUT_CURSOR_HELD_THRESHOLD = 0.9f;
        public const float CENTER_OUT_CURSOR_MOVING_THRESHOLD = 0.2f;
        private const double center_out_cursor_motion_grace = 40;
        public static readonly Color4 LEFT_COLOUR = new Color4(0.2f, 0.62f, 1f, 1f);
        public static readonly Color4 RIGHT_COLOUR = new Color4(1f, 0.25f, 0.3f, 1f);

        private readonly CircularContainer leftCursor;
        private readonly CircularContainer rightCursor;
        private readonly SticksCursorTrail leftTrail;
        private readonly SticksCursorTrail rightTrail;
        private readonly SmoothPath leftRelaxDirectionLine;
        private readonly SmoothPath rightRelaxDirectionLine;
        private readonly Container radialPathLayer;
        private readonly SticksRibbonBuffer radialPathBuffer;
        private readonly SticksContactBurstLayer contactBurstLayer;
        private readonly SticksJudgementDisplay judgementDisplay;
        private readonly SticksInputTracker input = new SticksInputTracker();
        private readonly SticksReplayInputProvider replayInputProvider;
        private SticksStackedNotePresentation stackedNotePresentation = SticksStackedNotePresentation.RadialSpacing;
        private float noteCircleScale = DEFAULT_NOTE_CIRCLE_SCALE;
        private bool leftTrailWasVisible;
        private bool rightTrailWasVisible;
        private SticksNotePresentation notePresentation = SticksNotePresentation.BracketMarkers;
        private float leftX;
        private float leftY;
        private float rightX;
        private float rightY;
        private Vector2 lastReportedPhysicalLeft;
        private Vector2 lastReportedPhysicalRight;
        private Vector2 relaxedLeftDirection;
        private Vector2 relaxedRightDirection;
        private float previousDisplayedLeftMagnitude;
        private float previousDisplayedRightMagnitude;
        private double leftCursorOutwardUntil = double.NegativeInfinity;
        private double rightCursorOutwardUntil = double.NegativeInfinity;

        public event Action<bool> PhysicalStickInputChanged;

        public bool ShowCursorTrails { get; set; }

        public SticksChordLinkPresentation ChordLinkPresentation { get; set; } = SticksChordLinkPresentation.FullToCentre;

        public SticksNotePresentation NotePresentation
        {
            get => notePresentation;
            set
            {
                if (notePresentation == value)
                    return;

                notePresentation = value;
                updateRadialPresentationMode();
            }
        }

        public bool CenterOutPresentation => notePresentation == SticksNotePresentation.CenterOut;

        /// <summary>
        /// In center-out presentation, hides cursors except while held near the judgement ring
        /// or actively moving outward beyond the inner dead zone.
        /// </summary>
        public bool HideInactiveCursors { get; set; }

        /// <summary>
        /// Shows restrained contact feedback while center-out objects are successfully played.
        /// </summary>
        public bool SliderTrackingSparks { get; set; }

        public float NoteCircleScale
        {
            get => noteCircleScale;
            set => noteCircleScale = Math.Clamp(value, MIN_NOTE_CIRCLE_SCALE, MAX_NOTE_CIRCLE_SCALE);
        }

        /// <summary>
        /// When active, each stick retains its latest direction supplied beyond the normal slider
        /// tracking cutoff, and gestures are generated when that direction can play an object.
        /// </summary>
        public bool RelaxMode { get; set; }

        /// <summary>
        /// The physical stick magnitude represented as full distance for gameplay, gesture detection,
        /// and the cursor.
        /// </summary>
        public float PhysicalStickDistanceAtGameEdge { get; set; } = 1;

        /// <summary>
        /// Mapped gameplay radius which an armed stick must cross to create a flick.
        /// Recharge is derived thirty percentage points below this value in physical space.
        /// </summary>
        public float FlickActivationThreshold
        {
            get => input.ActivationThreshold;
            set => input.ActivationThreshold = value;
        }

        public float RechargeThreshold => input.RechargeThreshold;

        public SticksStackedNotePresentation StackedNotePresentation
        {
            get => stackedNotePresentation;
            set
            {
                stackedNotePresentation = value;
                updateRadialPresentationMode();
            }
        }

        public bool RadialNoteApproach => stackedNotePresentation == SticksStackedNotePresentation.RadialApproach;

        public float RadialApproachDistance { get; set; } = DEFAULT_RADIAL_APPROACH_DISTANCE;

        public float RadialApproachSpeed { get; set; } = DEFAULT_RADIAL_APPROACH_SPEED;

        public CircularContainer LeftStickCursor => leftCursor;

        public CircularContainer RightStickCursor => rightCursor;

        /// <summary>
        /// Returns the unmodified physical controller position. Replays must store this rather than
        /// <see cref="StickVector"/>, because gameplay distance mapping is reapplied during playback.
        /// </summary>
        public Vector2 PhysicalStickVector(StickSide side) => side == StickSide.Left
            ? new Vector2(leftX, leftY)
            : new Vector2(rightX, rightY);

        /// <summary>
        /// Duration objects remain trackable only while the physical stick is strictly outside
        /// the recharge boundary. Entering the boundary both drops tracking and rearms a flick.
        /// </summary>
        public bool IsStickBeyondRechargeBoundary(StickSide side) =>
            input.IsBeyondRechargeBoundary(side);

        public SticksPlayfield(SticksReplayInputProvider replayInputProvider = null)
        {
            this.replayInputProvider = replayInputProvider ?? new SticksReplayInputProvider();
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;

            AddRangeInternal(new Drawable[]
            {
                ring(GUIDE_RADIUS, Color4.White.Opacity(0.55f)),
                leftRelaxDirectionLine = relaxDirectionLine(LEFT_COLOUR),
                rightRelaxDirectionLine = relaxDirectionLine(RIGHT_COLOUR),
                radialPathBuffer = new SticksRibbonBuffer()
                {
                    RelativeSizeAxes = Axes.Both,
                    Depth = 5,
                    Alpha = 0,
                    // Max-blended ribbon colours require zero RGB in transparent pixels.
                    // Color4.Transparent is transparent white, which wins a component-wise
                    // maximum and washes every isolated ribbon fill to white.
                    BackgroundColour = new Color4(0, 0, 0, 0),
                    Child = radialPathLayer = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                },
                // Note markers deliberately live outside the max-blended ribbon buffer. They
                // therefore replace slider colour normally and remain fully opaque when the two
                // overlap, while ribbon/ribbon intersections can still mix to purple below.
                HitObjectContainer,
                contactBurstLayer = new SticksContactBurstLayer(),
                judgementDisplay = new SticksJudgementDisplay(),
                leftTrail = new SticksCursorTrail("Cursors/blue"),
                rightTrail = new SticksCursorTrail("Cursors/red"),
                leftCursor = cursor(LEFT_COLOUR),
                rightCursor = cursor(RIGHT_COLOUR),
            });
        }

        internal void AddRadialPath(SticksRadialTimelinePath path)
        {
            if (path.Parent == null)
                radialPathLayer.Add(path);
        }

        internal void DetachRadialPath(SticksRadialTimelinePath path)
        {
            if (path.Parent == radialPathLayer)
                radialPathLayer.Remove(path, false);
        }

        internal void RemoveRadialPath(SticksRadialTimelinePath path)
        {
            // Hit object disposal may be initiated by the host's shutdown thread. Defer the
            // child mutation; if the playfield is also shutting down, its normal disposal owns it.
            Scheduler.Add(() =>
            {
                if (path.Parent == radialPathLayer)
                    radialPathLayer.Remove(path, true);
                else
                    path.Dispose();
            });
        }

        internal void TriggerContactBurst(StickSide side, float angle, float hitSpan, bool completion = false)
        {
            if (!CenterOutPresentation || !SliderTrackingSparks)
                return;

            contactBurstLayer.Trigger(
                angle,
                hitSpan,
                side == StickSide.Left ? LEFT_COLOUR : RIGHT_COLOUR,
                completion);
        }

        [BackgroundDependencyLoader]
        private void load(IBeatmap beatmap)
        {
            int maxSliderTicks = beatmap.HitObjects.OfType<SticksSlider>()
                                        .Select(slider => slider.NestedHitObjects.OfType<SticksSliderTick>().Count())
                                        .DefaultIfEmpty(0)
                                        .Max();
            int maxHoldTicks = beatmap.HitObjects.OfType<SticksHold>()
                                      .Select(hold => hold.NestedHitObjects.OfType<SticksHoldTick>().Count())
                                      .DefaultIfEmpty(0)
                                      .Max();
            int maxRepeats = beatmap.HitObjects.OfType<SticksSlider>()
                                    .Select(slider => slider.NestedHitObjects.OfType<SticksSliderRepeat>().Count())
                                    .DefaultIfEmpty(0)
                                    .Max();
            int maxExtensions = beatmap.HitObjects.OfType<SticksSlider>()
                                       .Select(slider => slider.NestedHitObjects.OfType<SticksSliderExtension>().Count())
                                       .DefaultIfEmpty(0)
                                       .Max();

            RegisterPool<SticksSliderHead, DrawableSticksSliderHead>(10, 100);
            RegisterPool<SticksHoldHead, DrawableSticksHoldHead>(10, 100);
            RegisterPool<SticksAngleComponent, DrawableSticksAngleComponent>(20, 200);
            RegisterPool<SticksSliderTick, DrawableSticksSliderTick>(Math.Clamp(maxSliderTicks, 10, 100), Math.Max(maxSliderTicks, 200));
            RegisterPool<SticksHoldTick, DrawableSticksHoldTick>(Math.Clamp(maxHoldTicks, 10, 100), Math.Max(maxHoldTicks, 200));
            RegisterPool<SticksSliderRepeat, DrawableSticksSliderRepeat>(Math.Max(maxRepeats, 10), Math.Max(maxRepeats, 100));
            RegisterPool<SticksSliderExtension, DrawableSticksSliderExtension>(Math.Clamp(maxExtensions, 10, 100), Math.Max(maxExtensions, 100));
            RegisterPool<SticksSliderTail, DrawableSticksSliderTail>(10, 100);
            RegisterPool<SticksHoldTail, DrawableSticksHoldTail>(10, 100);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            NewResult += onNewResult;
            RevertResult += onRevertResult;
        }

        protected override void Dispose(bool isDisposing)
        {
            NewResult -= onNewResult;
            RevertResult -= onRevertResult;
            base.Dispose(isDisposing);
        }

        private void onNewResult(DrawableHitObject judgedObject, JudgementResult result)
        {
            if (judgedObject is DrawableSticksFlick flick && result.Type.IsHit())
                TriggerContactBurst(flick.HitObject.Side, flick.HitObject.Angle, flick.HitObject.PrimaryHitAngle);
            else if (judgedObject is DrawableSticksSliderTail tail && result.Type == HitResult.SliderTailHit)
                TriggerContactBurst(tail.HitObject.Side, tail.HitObject.Angle, tail.HitObject.PrimaryHitAngle, completion: true);
            else if (judgedObject is DrawableSticksHoldTail holdTail && result.Type == HitResult.SliderTailHit)
                TriggerContactBurst(holdTail.HitObject.Side, holdTail.HitObject.Angle, holdTail.HitObject.PrimaryHitAngle, completion: true);

            if (!DisplayJudgements.Value)
                return;

            judgementDisplay.Process(result);
        }

        private void onRevertResult(JudgementResult result)
        {
            contactBurstLayer.ClearBursts();
            judgementDisplay.Revert(result);
        }

        public Vector2 StickVector(StickSide side) => input.VectorFor(side);

        public static Vector2 MapStickDistance(Vector2 value, float physicalDistanceAtGameEdge)
        {
            float length = value.Length;

            if (length == 0)
                return Vector2.Zero;

            physicalDistanceAtGameEdge = Math.Clamp(physicalDistanceAtGameEdge, 0.01f, 1);
            float gameDistance = Math.Min(length / physicalDistanceAtGameEdge, 1);
            return value / length * gameDistance;
        }

        public long FlickSequence(StickSide side) => input.SequenceFor(side);

        public SticksInputTracker.FlickEvent LastFlick(StickSide side) => input.LastFlickFor(side);

        /// <summary>
        /// Claims a flick for the best matching unjudged note head in this stick's overlapping hit windows.
        /// This prevents whichever drawable happens to update first from stealing the gesture.
        /// </summary>
        public bool TryConsumeHeadFlick(DrawableHitObject requester, StickSide side, long sequence)
        {
            SticksInputTracker.FlickEvent flick = input.LastFlickFor(side);
            DrawableHitObject target = findPreferredHeadTarget(side, flick);

            if (flick.Sequence != sequence
                || target != requester
                || isBlockedByEarlierHead(side, headHitObjectFor(target), flick.Time))
                return false;

            if (!input.TryConsumeFlick(side, sequence))
                return false;

            // Match lazer's modern note lock result ordering: skipped notes resolve before the
            // selected target. Duration parents remain alive because only their heads are missed.
            missSkippedHeads(side, headHitObjectFor(target).StartTime);
            return true;
        }

        private bool isBlockedByEarlierHead(StickSide side, SticksHitObject target, double flickTime)
        {
            DrawableHitObject latestPrecedingHead = null;

            foreach (DrawableHitObject drawable in HitObjectContainer.AliveObjects)
            {
                SticksHitObject earlierHead = headHitObjectFor(drawable);

                if (earlierHead != null
                    && earlierHead.Side == side
                    && earlierHead.StartTime < target.StartTime
                    && (latestPrecedingHead == null
                        || earlierHead.StartTime >= headHitObjectFor(latestPrecedingHead).StartTime))
                    latestPrecedingHead = drawable;
            }

            return latestPrecedingHead != null
                   && !headIsJudgedFor(latestPrecedingHead)
                   && IsEarlierHeadBlocking(flickTime, target.StartTime, headHitObjectFor(latestPrecedingHead).StartTime);
        }

        /// <summary>
        /// Modern osu!-style note lock: a skipped head blocks a later head only until the
        /// skipped head's own start time. Once that time is reached the later head may be hit,
        /// but the skipped head is immediately missed by <see cref="missSkippedHeads"/>.
        /// </summary>
        public static bool IsEarlierHeadBlocking(double flickTime, double targetStartTime, double earlierStartTime) =>
            earlierStartTime < targetStartTime && flickTime < earlierStartTime;

        private void missSkippedHeads(StickSide side, double targetStartTime)
        {
            var skipped = new List<DrawableHitObject>();

            foreach (DrawableHitObject drawable in HitObjectContainer.AliveObjects)
            {
                SticksHitObject skippedHead = unjudgedHeadHitObjectFor(drawable);

                if (skippedHead != null && skippedHead.Side == side && skippedHead.StartTime < targetStartTime)
                    skipped.Add(drawable);
            }

            // Applying a miss emits results and may update the live-object collection. Iterate a
            // stable snapshot rather than mutating the collection being enumerated.
            foreach (DrawableHitObject drawable in skipped)
            {
                switch (drawable)
                {
                    case DrawableSticksFlick flick:
                        flick.MarkHeadMiss();
                        break;

                    case DrawableSticksSlider slider:
                        slider.MarkHeadMiss();
                        break;

                    case DrawableSticksHold hold:
                        hold.MarkHeadMiss();
                        break;
                }
            }
        }

        /// <summary>
        /// Claims a flick for duration-object tracking only when it is not a valid hit for a note head.
        /// </summary>
        public bool TryConsumeTrackingFlick(StickSide side, long sequence)
        {
            SticksInputTracker.FlickEvent flick = input.LastFlickFor(side);

            if (flick.Sequence != sequence || findPreferredHeadTarget(side, flick) != null)
                return false;

            return input.TryConsumeFlick(side, sequence);
        }

        private DrawableHitObject findPreferredHeadTarget(StickSide side, SticksInputTracker.FlickEvent flick)
        {
            DrawableHitObject bestDrawable = null;
            FlickTarget bestTarget = default;

            foreach (DrawableHitObject drawable in HitObjectContainer.AliveObjects)
            {
                if (!tryGetFlickTarget(drawable, side, flick, out FlickTarget target))
                    continue;

                if (bestDrawable == null || IsBetterFlickTarget(target, bestTarget, flick.Time, flick.Angle))
                {
                    bestDrawable = drawable;
                    bestTarget = target;
                }
            }

            return bestDrawable;
        }

        private static bool tryGetFlickTarget(DrawableHitObject drawable, StickSide side, SticksInputTracker.FlickEvent flickEvent, out FlickTarget target)
        {
            SticksHitObject hitObject = drawable switch
            {
                DrawableSticksFlick flick when !flick.Judged => flick.HitObject,
                DrawableSticksSlider slider when !slider.HeadJudged => slider.HitObject,
                DrawableSticksHold hold when !hold.HeadJudged => hold.HitObject,
                _ => null,
            };

            if (hitObject == null || hitObject.Side != side)
            {
                target = default;
                return false;
            }

            target = new FlickTarget(hitObject.StartTime, hitObject.Angle, hitObject.LenientHalfAngle);
            HitResult timingResult = HeadTimingResultFor(hitObject, flickEvent.Time - hitObject.StartTime);
            float angleError = Math.Abs(SticksHitObject.DeltaAngle(flickEvent.Angle, hitObject.Angle));
            return IsEligibleFlickTarget(timingResult, angleError, hitObject.LenientHalfAngle);
        }

        public static HitResult HeadTimingResultFor(SticksHitObject hitObject, double timeOffset)
        {
            SticksHitObject scoredHead = hitObject switch
            {
                SticksSlider slider => slider.NestedHitObjects.OfType<SticksSliderHead>().FirstOrDefault(),
                SticksHold hold => hold.NestedHitObjects.OfType<SticksHoldHead>().FirstOrDefault(),
                _ => hitObject,
            };

            return scoredHead?.HitWindows?.ResultFor(timeOffset) ?? HitResult.Miss;
        }

        public static bool IsEligibleFlickTarget(HitResult timingResult, float angleError, float lenientHalfAngle) =>
            timingResult.IsHit() && angleError <= lenientHalfAngle;

        public static bool IsBetterFlickTarget(FlickTarget candidate, FlickTarget current, double flickTime, float flickAngle)
        {
            if (candidate.StartTime != current.StartTime)
                return candidate.StartTime < current.StartTime;

            float candidateAngleError = Math.Abs(SticksHitObject.DeltaAngle(flickAngle, candidate.Angle));
            float currentAngleError = Math.Abs(SticksHitObject.DeltaAngle(flickAngle, current.Angle));

            if (candidateAngleError != currentAngleError)
                return candidateAngleError < currentAngleError;

            return Math.Abs(flickTime - candidate.StartTime) < Math.Abs(flickTime - current.StartTime);
        }

        public readonly record struct FlickTarget(double StartTime, float Angle, float LenientHalfAngle);

        /// <summary>
        /// Returns a clock-derived radial approach position. Blue objects begin outside their
        /// lane and red objects begin inside it, then linearly reach the normal lane exactly at
        /// the hit time. This is deliberately not frame-rate-dependent damping.
        /// </summary>
        public float RadialApproachOffsetFor(SticksHitObject hitObject) => RadialNoteApproach
            ? RadialApproachOffsetAt(
                hitObject.Side,
                Time.Current,
                hitObject.StartTime,
                hitObject.ApproachDuration,
                RadialApproachDistance,
                RadialApproachSpeed)
            : 0;

        public float VisualRadialOffsetFor(DrawableHitObject drawable, SticksHitObject hitObject) => CenterOutPresentation
            ? 0
            : RadialNoteApproach ? RadialApproachOffsetFor(hitObject) : HeadStackOffsetFor(drawable);

        internal static float RadialApproachOffsetAt(
            StickSide side,
            double time,
            double hitTime,
            double approachDuration,
            float distance = DEFAULT_RADIAL_APPROACH_DISTANCE,
            float speed = DEFAULT_RADIAL_APPROACH_SPEED)
        {
            double progress = Math.Clamp((time - (hitTime - approachDuration)) / Math.Max(1, approachDuration), 0, 1);
            double speedAdjustedProgress = 1 - Math.Pow(1 - progress, Math.Max(0.01f, speed));
            float direction = side == StickSide.Left ? 1 : -1;
            return direction * distance * (1 - (float)speedAdjustedProgress);
        }

        /// <summary>
        /// Returns the visual radial separation assigned to a later head which would otherwise
        /// be occluded by an earlier head on the same stick and angular lane.
        /// </summary>
        public float HeadStackOffsetFor(DrawableHitObject drawable) =>
            ((SticksHitObjectContainer)HitObjectContainer).HeadStackOffsetFor(drawable);

        private void updateRadialPresentationMode()
        {
            radialPathBuffer.Alpha = CenterOutPresentation ? 1 : 0;
            ((SticksHitObjectContainer)HitObjectContainer).RadialStackedNoteSpacing =
                !CenterOutPresentation && stackedNotePresentation == SticksStackedNotePresentation.RadialSpacing;
        }

        /// <summary>
        /// Maps an object's hit time to the shared center-out judgement circle.
        /// </summary>
        internal static float CenterOutProgressAt(double time, double hitTime, double approachDuration) =>
            (float)Math.Clamp((time - (hitTime - approachDuration)) / Math.Max(1, approachDuration), 0, 1);

        internal static bool CenterOutCursorVisible(float mappedMagnitude, bool movingOutward) =>
            mappedMagnitude >= CENTER_OUT_CURSOR_HELD_THRESHOLD
            || mappedMagnitude > CENTER_OUT_CURSOR_MOVING_THRESHOLD && movingOutward;

        public static float RadiusFor(StickSide side) => side == StickSide.Left ? OUTER_RADIUS : INNER_RADIUS;

        public static Vector2 PointAt(float angle, float radius)
        {
            float radians = angle * MathF.PI / 180;
            return new Vector2(SIZE / 2 + MathF.Cos(radians) * radius, SIZE / 2 + MathF.Sin(radians) * radius);
        }

        protected override HitObjectContainer CreateHitObjectContainer() => new SticksHitObjectContainer();

        protected override GameplayCursorContainer CreateCursor() => new SticksCursorContainer();

        protected override bool OnJoystickAxisMove(JoystickAxisMoveEvent e)
        {
            switch (e.Axis.Source)
            {
                case JoystickAxisSource.GamePadLeftStickX:
                    leftX = e.Axis.Value;
                    break;

                case JoystickAxisSource.GamePadLeftStickY:
                    leftY = e.Axis.Value;
                    break;

                case JoystickAxisSource.GamePadRightStickX:
                    rightX = e.Axis.Value;
                    break;

                case JoystickAxisSource.GamePadRightStickY:
                    rightY = e.Axis.Value;
                    break;

                default:
                    return base.OnJoystickAxisMove(e);
            }

            return true;
        }

        protected override void Update()
        {
            base.Update();

            var left = new Vector2(leftX, leftY);
            var right = new Vector2(rightX, rightY);

            if (replayInputProvider.Active)
                (left, right) = replayInputProvider.Snapshot();

            Vector2 displayedLeft = MapStickDistance(left, PhysicalStickDistanceAtGameEdge);
            Vector2 displayedRight = MapStickDistance(right, PhysicalStickDistanceAtGameEdge);

            if (RelaxMode)
                updateRelaxInput(left, right);
            else
            {
                // Replay positions are deliberately kept separate from the physical axis fields.
                // In editor test play autoplay can be detached at runtime; physical input must take
                // over immediately rather than inheriting the replay's final held position.
                input.Update(StickSide.Left, left, MapStickDistance(left, PhysicalStickDistanceAtGameEdge), Time.Current);
                input.Update(StickSide.Right, right, MapStickDistance(right, PhysicalStickDistanceAtGameEdge), Time.Current);
            }

            reportPhysicalStickInput(left, right);
            bool leftCursorVisible = updateCursor(
                leftCursor,
                displayedLeft,
                StickSide.Left,
                ref previousDisplayedLeftMagnitude,
                ref leftCursorOutwardUntil);
            bool rightCursorVisible = updateCursor(
                rightCursor,
                displayedRight,
                StickSide.Right,
                ref previousDisplayedRightMagnitude,
                ref rightCursorOutwardUntil);
            updateTrails(leftCursorVisible, rightCursorVisible);
        }

        private void updateRelaxInput(Vector2 left, Vector2 right)
        {
            Vector2 previousLeftDirection = relaxedLeftDirection;
            Vector2 previousRightDirection = relaxedRightDirection;

            relaxedLeftDirection = RememberRelaxDirection(relaxedLeftDirection, left, RechargeThreshold);
            relaxedRightDirection = RememberRelaxDirection(relaxedRightDirection, right, RechargeThreshold);

            if (relaxedLeftDirection != previousLeftDirection || relaxedRightDirection != previousRightDirection)
                updateRelaxDirectionLines();

            input.UpdateRelaxDirection(StickSide.Left, relaxedLeftDirection);
            input.UpdateRelaxDirection(StickSide.Right, relaxedRightDirection);

            tryTriggerRelaxGesture(StickSide.Left, relaxedLeftDirection);
            tryTriggerRelaxGesture(StickSide.Right, relaxedRightDirection);
        }

        internal static Vector2 RememberRelaxDirection(Vector2 previous, Vector2 current, float minimumMagnitude) =>
            current.Length > minimumMagnitude ? current.Normalized() : previous;

        private void updateRelaxDirectionLines()
        {
            updateRelaxDirectionLine(leftRelaxDirectionLine, relaxedLeftDirection, StickSide.Left);
            updateRelaxDirectionLine(rightRelaxDirectionLine, relaxedRightDirection, StickSide.Right);
        }

        private static void updateRelaxDirectionLine(SmoothPath line, Vector2 direction, StickSide side)
        {
            if (direction.LengthSquared == 0)
            {
                line.Alpha = 0;
                return;
            }

            Vector2 centre = new Vector2(SIZE / 2);
            line.Vertices = new[]
            {
                centre,
                RelaxDirectionEndpoint(direction, side),
            };
            line.Alpha = 0.85f;
        }

        internal static Vector2 RelaxDirectionEndpoint(Vector2 direction, StickSide side) =>
            new Vector2(SIZE / 2) + direction * (RadiusFor(side) * RELAX_DIRECTION_LENGTH_FRACTION);

        private void tryTriggerRelaxGesture(StickSide side, Vector2 direction)
        {
            if (direction.LengthSquared == 0)
                return;

            double time = Time.Current;
            float angle = SticksHitObject.NormaliseAngle(MathF.Atan2(direction.Y, direction.X) * 180 / MathF.PI);
            var candidate = new SticksInputTracker.FlickEvent(0, time, angle);
            DrawableHitObject headTarget = findPreferredHeadTarget(side, candidate);

            if (headTarget != null)
            {
                if (time >= headHitObjectFor(headTarget).StartTime)
                    input.TriggerRelaxFlick(side, time);

                return;
            }

            // A missed duration head must not lock Relax out of the normal partial-credit path.
            // Generate a new tracking gesture once the remembered direction reaches the active
            // path, while leaving all tick and tail checks to the ordinary drawable logic.
            if (hasEligibleRelaxTrackingTarget(side, time, angle))
                input.TriggerRelaxFlick(side, time);
        }

        private bool hasEligibleRelaxTrackingTarget(StickSide side, double time, float angle)
        {
            foreach (DrawableHitObject drawable in HitObjectContainer.AliveObjects)
            {
                SticksHitObject hitObject;
                float targetAngle;
                bool headCanNoLongerBeHit;

                switch (drawable)
                {
                    case DrawableSticksSlider slider when !slider.TrackingAuthorised
                                                                && time >= slider.HitObject.StartTime
                                                                && time <= slider.HitObject.EndTime:
                        hitObject = slider.HitObject;
                        targetAngle = slider.HitObject.AngleAt(time);
                        headCanNoLongerBeHit = slider.HeadJudged
                                               || !HeadTimingResultFor(hitObject, time - hitObject.StartTime).IsHit();
                        break;

                    case DrawableSticksHold hold when !hold.TrackingAuthorised
                                                            && time >= hold.HitObject.StartTime
                                                            && time <= hold.HitObject.EndTime:
                        hitObject = hold.HitObject;
                        targetAngle = hold.HitObject.Angle;
                        headCanNoLongerBeHit = hold.HeadJudged
                                               || !HeadTimingResultFor(hitObject, time - hitObject.StartTime).IsHit();
                        break;

                    default:
                        continue;
                }

                if (hitObject.Side == side
                    && headCanNoLongerBeHit
                    && Math.Abs(SticksHitObject.DeltaAngle(angle, targetAngle)) <= hitObject.LenientHalfAngle)
                    return true;
            }

            return false;
        }

        private void reportPhysicalStickInput(Vector2 left, Vector2 right)
        {
            if (left == lastReportedPhysicalLeft && right == lastReportedPhysicalRight)
                return;

            bool important = RelaxMode
                             || crossesGestureBoundary(lastReportedPhysicalLeft, left)
                             || crossesGestureBoundary(lastReportedPhysicalRight, right);
            lastReportedPhysicalLeft = left;
            lastReportedPhysicalRight = right;

            // Joystick X and Y arrive as separate framework events. Publishing here, after the
            // input event batch has completed, prevents recording a new X with the previous Y.
            PhysicalStickInputChanged?.Invoke(important);
        }

        private bool crossesGestureBoundary(Vector2 previous, Vector2 current)
        {
            bool returnedToNeutral = previous.Length > RechargeThreshold
                                     && current.Length <= RechargeThreshold;
            bool crossedFlickThreshold = MapStickDistance(previous, PhysicalStickDistanceAtGameEdge).Length < FlickActivationThreshold
                                         && MapStickDistance(current, PhysicalStickDistanceAtGameEdge).Length >= FlickActivationThreshold;
            return returnedToNeutral || crossedFlickThreshold;
        }

        private bool updateCursor(
            CircularContainer drawable,
            Vector2 value,
            StickSide side,
            ref float previousMagnitude,
            ref double outwardUntil)
        {
            float magnitude = Math.Clamp(value.Length, 0, 1);

            if (!CenterOutPresentation || !HideInactiveCursors)
            {
                float radius = CenterOutPresentation ? GUIDE_RADIUS : RadiusFor(side);
                drawable.Position = new Vector2(SIZE / 2) + value * radius;
                drawable.Alpha = 0.35f + magnitude * 0.65f;
                previousMagnitude = magnitude;
                outwardUntil = double.NegativeInfinity;
                return true;
            }

            const float movement_epsilon = 0.0001f;
            float movement = magnitude - previousMagnitude;

            if (movement > movement_epsilon)
                outwardUntil = Time.Current + center_out_cursor_motion_grace;
            else if (movement < -movement_epsilon)
                outwardUntil = double.NegativeInfinity;

            previousMagnitude = magnitude;
            bool visible = CenterOutCursorVisible(magnitude, Time.Current <= outwardUntil);
            drawable.Position = new Vector2(SIZE / 2) + value * GUIDE_RADIUS;
            drawable.Alpha = visible ? 1 : 0;
            return visible;
        }

        private void updateTrails(bool leftCursorVisible, bool rightCursorVisible)
        {
            updateTrail(leftTrail, leftCursor, ShowCursorTrails && leftCursorVisible, ref leftTrailWasVisible);
            updateTrail(rightTrail, rightCursor, ShowCursorTrails && rightCursorVisible, ref rightTrailWasVisible);
        }

        private static void updateTrail(SticksCursorTrail trail, Drawable cursorDrawable, bool visible, ref bool wasVisible)
        {
            if (!visible)
            {
                trail.Alpha = 0;

                if (wasVisible)
                {
                    trail.Reset();
                    wasVisible = false;
                }

                return;
            }

            if (!wasVisible)
            {
                trail.Reset();
                wasVisible = true;
            }

            trail.Alpha = 0.65f;
            trail.AddPosition(cursorDrawable.ScreenSpaceDrawQuad.Centre);
        }

        private static CircularContainer ring(float radius, Color4 colour) => new CircularContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Position = new Vector2(SIZE / 2),
            Size = new Vector2(radius * 2),
            Masking = true,
            BorderThickness = 2,
            BorderColour = colour,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
                AlwaysPresent = true,
            },
        };

        private static SmoothPath relaxDirectionLine(Color4 colour) => new SmoothPath
        {
            AutoSizeAxes = Axes.None,
            Size = new Vector2(SIZE),
            PathRadius = 3.5f,
            Colour = colour,
            Alpha = 0,
            Depth = 10,
        };

        private static CircularContainer cursor(Color4 colour) => new CircularContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Size = new Vector2(24),
            Masking = true,
            BorderThickness = 3,
            BorderColour = Color4.White,
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = Color4.Black.Opacity(0.7f),
                Offset = new Vector2(0, 3),
                Radius = 5,
                Hollow = true,
            },
            Depth = -20,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colour,
            },
        };

        private static SticksHitObject headHitObjectFor(DrawableHitObject drawable) => drawable switch
        {
            DrawableSticksFlick flick => flick.HitObject,
            DrawableSticksSlider slider => slider.HitObject,
            DrawableSticksHold hold => hold.HitObject,
            _ => null,
        };

        private static SticksHitObject unjudgedHeadHitObjectFor(DrawableHitObject drawable) => drawable switch
        {
            DrawableSticksFlick flick when !flick.Judged => flick.HitObject,
            DrawableSticksSlider slider when !slider.HeadJudged => slider.HitObject,
            DrawableSticksHold hold when !hold.HeadJudged => hold.HitObject,
            _ => null,
        };

        private static bool headIsJudgedFor(DrawableHitObject drawable) => drawable switch
        {
            DrawableSticksFlick flick => flick.Judged,
            DrawableSticksSlider slider => slider.HeadJudged,
            DrawableSticksHold hold => hold.HeadJudged,
            _ => true,
        };

        private partial class SticksRibbonBuffer : BufferedContainer, ITexturedShaderDrawable
        {
            private IShader compositeShader = null!;

            IShader ITexturedShaderDrawable.TextureShader => compositeShader;

            public SticksRibbonBuffer()
                : base(cachedFrameBuffer: false)
            {
            }

            [BackgroundDependencyLoader]
            private void load(ShaderManager shaders)
            {
                compositeShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, "SticksRibbonComposite");
            }
        }
    }
}
