// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Diagnostics;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
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
        public static readonly Color4 LEFT_COLOUR = new Color4(0.2f, 0.62f, 1f, 1f);
        public static readonly Color4 RIGHT_COLOUR = new Color4(1f, 0.25f, 0.3f, 1f);

        private readonly CircularContainer leftCursor;
        private readonly CircularContainer rightCursor;
        private readonly Container leftTrail;
        private readonly Container rightTrail;
        private readonly SticksJudgementDisplay judgementDisplay;
        private readonly Circle[] leftTrailDots;
        private readonly Circle[] rightTrailDots;
        private readonly SticksInputTracker input = new SticksInputTracker();
        private readonly SticksReplayInputProvider replayInputProvider;
        private readonly Process currentProcess = Process.GetCurrentProcess();
        private double lastTrailSampleTime = double.NegativeInfinity;
        private double lastMemoryReportTime = double.NegativeInfinity;
        private bool trailsWereVisible;
        private float leftX;
        private float leftY;
        private float rightX;
        private float rightY;
        private HitResult? pendingTimingResult;

        public bool ShowCursorTrails { get; set; }

        public CircularContainer LeftStickCursor => leftCursor;

        public CircularContainer RightStickCursor => rightCursor;

        public SticksPlayfield(SticksReplayInputProvider replayInputProvider = null)
        {
            this.replayInputProvider = replayInputProvider ?? new SticksReplayInputProvider();
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;

            AddRangeInternal(new Drawable[]
            {
                ring(GUIDE_RADIUS, Color4.White.Opacity(0.55f)),
                HitObjectContainer,
                judgementDisplay = new SticksJudgementDisplay(),
                leftTrail = trail(LEFT_COLOUR, out leftTrailDots),
                rightTrail = trail(RIGHT_COLOUR, out rightTrailDots),
                leftCursor = cursor(LEFT_COLOUR),
                rightCursor = cursor(RIGHT_COLOUR),
            });
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
            if (!DisplayJudgements.Value || result.HitObject is not ISticksAccuracyComponent component)
                return;

            if (component.AccuracyComponent == SticksAccuracyComponent.Timing)
            {
                pendingTimingResult = result.Type;
                return;
            }

            if (pendingTimingResult is not HitResult timingResult)
                return;

            judgementDisplay.Display(timingResult, result.Type);
            pendingTimingResult = null;
        }

        private void onRevertResult(JudgementResult result)
        {
            pendingTimingResult = null;
            judgementDisplay.ResetDisplay();
        }

        public Vector2 StickVector(StickSide side) => input.VectorFor(side);

        public long FlickSequence(StickSide side) => input.SequenceFor(side);

        public SticksInputTracker.FlickEvent LastFlick(StickSide side) => input.LastFlickFor(side);

        public bool TryConsumeFlick(StickSide side, long sequence) => input.TryConsumeFlick(side, sequence);

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

            if (replayInputProvider.Active)
            {
                (Vector2 left, Vector2 right) = replayInputProvider.Snapshot();
                leftX = left.X;
                leftY = left.Y;
                rightX = right.X;
                rightY = right.Y;
            }

            input.Update(StickSide.Left, new Vector2(leftX, leftY), Time.Current);
            input.Update(StickSide.Right, new Vector2(rightX, rightY), Time.Current);
            updateCursor(leftCursor, StickSide.Left);
            updateCursor(rightCursor, StickSide.Right);
            updateTrails();
            reportMemoryUsage();
        }

        private void reportMemoryUsage()
        {
            if (Time.Current - lastMemoryReportTime < 2000)
                return;

            lastMemoryReportTime = Time.Current;
            currentProcess.Refresh();
            GCMemoryInfo gc = GC.GetGCMemoryInfo();
            Logger.Log($"Sticks memory: managed={toMiB(GC.GetTotalMemory(false))} MiB, heap={toMiB(gc.HeapSizeBytes)} MiB, committed={toMiB(gc.TotalCommittedBytes)} MiB, working={toMiB(currentProcess.WorkingSet64)} MiB, private={toMiB(currentProcess.PrivateMemorySize64)} MiB");
        }

        private static long toMiB(long bytes) => bytes / (1024 * 1024);

        private void updateCursor(CircularContainer drawable, StickSide side)
        {
            Vector2 value = StickVector(side);
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
                    resetTrail(leftTrailDots);
                    resetTrail(rightTrailDots);
                    trailsWereVisible = false;
                }

                return;
            }

            trailsWereVisible = true;
            leftTrail.Alpha = 0.26f;
            rightTrail.Alpha = 0.26f;

            if (Time.Current - lastTrailSampleTime < 16)
                return;

            lastTrailSampleTime = Time.Current;
            appendTrailPoint(leftTrailDots, leftCursor.Position);
            appendTrailPoint(rightTrailDots, rightCursor.Position);
        }

        private static void appendTrailPoint(Circle[] dots, Vector2 point)
        {
            for (int i = dots.Length - 1; i > 0; i--)
                dots[i].Position = dots[i - 1].Position;

            dots[0].Position = point;
        }

        private static void resetTrail(Circle[] dots)
        {
            foreach (Circle dot in dots)
                dot.Position = new Vector2(SIZE / 2);
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

        private static Container trail(Color4 colour, out Circle[] dots)
        {
            dots = new Circle[18];

            for (int i = 0; i < dots.Length; i++)
            {
                dots[i] = new Circle
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = new Vector2(SIZE / 2),
                    Size = new Vector2(6 - i * 0.2f),
                    Colour = colour,
                    Alpha = 1 - i / (float)dots.Length,
                };
            }

            return new Container
            {
                Size = new Vector2(SIZE),
                Alpha = 0,
                Depth = -15,
                Children = dots,
            };
        }

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
    }
}
