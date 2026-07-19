// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
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
        public static readonly Color4 LEFT_COLOUR = new Color4(0.2f, 0.62f, 1f, 1f);
        public static readonly Color4 RIGHT_COLOUR = new Color4(1f, 0.25f, 0.3f, 1f);

        private readonly CircularContainer leftCursor;
        private readonly CircularContainer rightCursor;
        private readonly SticksCursorTrail leftTrail;
        private readonly SticksCursorTrail rightTrail;
        private readonly SmoothPath leftRelaxDirectionLine;
        private readonly SmoothPath rightRelaxDirectionLine;
        private readonly SticksJudgementDisplay judgementDisplay;
        private readonly SticksInputTracker input = new SticksInputTracker();
        private readonly SticksReplayInputProvider replayInputProvider;
        private SticksStackedNotePresentation stackedNotePresentation = SticksStackedNotePresentation.RadialSpacing;
        private float noteCircleScale = DEFAULT_NOTE_CIRCLE_SCALE;
        private bool trailsWereVisible;
        private float leftX;
        private float leftY;
        private float rightX;
        private float rightY;
        private Vector2 lastReportedPhysicalLeft;
        private Vector2 lastReportedPhysicalRight;
        private Vector2 relaxedLeftDirection;
        private Vector2 relaxedRightDirection;

        public event Action<bool> PhysicalStickInputChanged;

        public bool ShowCursorTrails { get; set; }

        public SticksChordLinkPresentation ChordLinkPresentation { get; set; } = SticksChordLinkPresentation.FullToCentre;

        public SticksNotePresentation NotePresentation { get; set; } = SticksNotePresentation.BracketMarkers;

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
                HitObjectContainer,
                judgementDisplay = new SticksJudgementDisplay(),
                leftTrail = new SticksCursorTrail("Cursors/blue"),
                rightTrail = new SticksCursorTrail("Cursors/red"),
                leftCursor = cursor(LEFT_COLOUR),
                rightCursor = cursor(RIGHT_COLOUR),
            });
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
            if (!DisplayJudgements.Value)
                return;

            judgementDisplay.Process(result);
        }

        private void onRevertResult(JudgementResult result) => judgementDisplay.Revert(result);

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

            if (flick.Sequence != sequence || findPreferredHeadTarget(side, flick) != requester)
                return false;

            return input.TryConsumeFlick(side, sequence);
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
            float candidateAngleError = Math.Abs(SticksHitObject.DeltaAngle(flickAngle, candidate.Angle));
            float currentAngleError = Math.Abs(SticksHitObject.DeltaAngle(flickAngle, current.Angle));
            bool candidateAngleMatches = candidateAngleError <= candidate.LenientHalfAngle;
            bool currentAngleMatches = currentAngleError <= current.LenientHalfAngle;

            if (candidateAngleMatches != currentAngleMatches)
                return candidateAngleMatches;

            double candidateTimeError = Math.Abs(flickTime - candidate.StartTime);
            double currentTimeError = Math.Abs(flickTime - current.StartTime);

            if (candidateTimeError != currentTimeError)
                return candidateTimeError < currentTimeError;

            if (candidateAngleError != currentAngleError)
                return candidateAngleError < currentAngleError;

            return candidate.StartTime < current.StartTime;
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

        public float VisualRadialOffsetFor(DrawableHitObject drawable, SticksHitObject hitObject) => RadialNoteApproach
            ? RadialApproachOffsetFor(hitObject)
            : HeadStackOffsetFor(drawable);

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
            ((SticksHitObjectContainer)HitObjectContainer).RadialStackedNoteSpacing =
                stackedNotePresentation == SticksStackedNotePresentation.RadialSpacing;
        }

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
            updateCursor(leftCursor, displayedLeft, StickSide.Left);
            updateCursor(rightCursor, displayedRight, StickSide.Right);
            updateTrails();
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

        private void updateCursor(CircularContainer drawable, Vector2 value, StickSide side)
        {
            drawable.Position = new Vector2(SIZE / 2) + value * RadiusFor(side);
            drawable.Alpha = 0.35f + Math.Clamp(value.Length, 0, 1) * 0.65f;
        }

        private void updateTrails()
        {
            if (!ShowCursorTrails)
            {
                leftTrail.Alpha = 0;
                rightTrail.Alpha = 0;

                if (trailsWereVisible)
                {
                    leftTrail.Reset();
                    rightTrail.Reset();
                    trailsWereVisible = false;
                }

                return;
            }

            if (!trailsWereVisible)
            {
                leftTrail.Reset();
                rightTrail.Reset();
                trailsWereVisible = true;
            }

            leftTrail.Alpha = 0.65f;
            rightTrail.Alpha = 0.65f;
            leftTrail.AddPosition(leftCursor.ScreenSpaceDrawQuad.Centre);
            rightTrail.AddPosition(rightCursor.ScreenSpaceDrawQuad.Centre);
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
    }
}
