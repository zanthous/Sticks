// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksSlider : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable
    {
        private const float tracking_magnitude = 0.56f;

        private readonly CircularProgress path;
        private readonly CircularProgress reversalOutline;
        private readonly CircularProgress reversalPreviewOutline;
        private readonly CircularProgress directionPreview;
        private readonly SticksArcMarker trackingMarker;
        private readonly SticksSliderHeadMarker headMarker;
        private readonly Container nestedHitObjectContainer;
        private SticksPlayfield playfield = null!;
        private double trackedTime;
        private double lastPlaybackTime = double.NaN;
        private bool headJudged;
        private bool headHit;
        private double headHitTime;

        public new SticksSlider HitObject => (SticksSlider)base.HitObject;

        public override bool HandlePositionalInput => false;

        public override IEnumerable<HitSampleInfo> GetSamples() => HitObject.CreatePlayableSamples();

        internal bool HeadHit => headHit;

        internal bool HeadJudged => headJudged;

        internal bool HasResult => Judged;

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

            AddInternal(reversalPreviewOutline = createArc(hitObject, 5, 0, 11, Color4.White));

            AddInternal(directionPreview = createArc(hitObject, 4, 0.28f, 10));

            AddInternal(reversalOutline = createArc(hitObject, 8, 0, 6, Color4.White));

            AddInternal(path = createArc(hitObject, 7, 1, 5));

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
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield sticksPlayfield) => playfield = sticksPlayfield;

        protected override void Update()
        {
            base.Update();

            double now = Time.Current;
            bool active = now >= HitObject.StartTime && now <= HitObject.EndTime;
            double cueDuration = HitObject.ApproachDuration;
            bool cueActive = now >= HitObject.StartTime - cueDuration && now < HitObject.StartTime;
            double cueProgress = Math.Clamp((now - (HitObject.StartTime - cueDuration)) / Math.Max(1, cueDuration), 0, 1);
            double headGrowth = SticksHitObject.ApproachGrowthProgress(cueProgress);
            headMarker.Span = HitObject.PrimaryHitAngle * (float)(0.2 + 0.8 * headGrowth);
            (double remainingStart, double remainingEnd) = HitObject.RemainingPathRangeAt(now);
            setVisibleRange(path, HitObject, remainingStart, remainingEnd);
            path.Alpha = active ? 1 : 0;
            updateDirectionPreview(now, cueActive, active);
            updateReversalOutline(now, cueActive, active, remainingStart, remainingEnd);

            updateHeadCue(now, cueActive);

            trackingMarker.Rotation = HitObject.AngleAt(now);
            trackingMarker.Alpha = active ? 1 : 0;

            updateHeadJudgement(now);

            if (active && headHit && !Judged)
            {
                float expectedAngle = HitObject.AngleAt(now);
                Vector2 stick = playfield.StickVector(HitObject.Side);
                float actualAngle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Y, stick.X) * 180 / MathF.PI);
                float angleError = Math.Abs(SticksHitObject.DeltaAngle(actualAngle, expectedAngle));

                if (stick.Length >= tracking_magnitude
                    && angleError <= HitObject.LenientHalfAngle
                    && !double.IsNaN(lastPlaybackTime)
                    && now > lastPlaybackTime)
                    trackedTime += Math.Min(50, now - lastPlaybackTime);
            }

            lastPlaybackTime = now;
        }

        private void updateDirectionPreview(double now, bool cueActive, bool active)
        {
            double rehearsalProgress = cueActive && now >= HitObject.RehearsalStartTime
                ? HitObject.RehearsalProgressAt(now)
                : 0;
            setVisibleRange(directionPreview, HitObject, 0, active ? 1 : rehearsalProgress);

            directionPreview.Alpha = active
                ? 0
                : rehearsalProgress > 0 ? (float)(0.18 + rehearsalProgress * 0.1) : 0;
        }

        private void updateReversalOutline(double now, bool cueActive, bool active, double remainingStart, double remainingEnd)
        {
            reversalOutline.Alpha = 0;
            reversalPreviewOutline.Alpha = 0;

            if ((!cueActive && !active) || !HitObject.CurrentSpanEndsWithReversal(now))
                return;

            if (active)
            {
                setVisibleRange(reversalOutline, HitObject, remainingStart, remainingEnd);
                reversalOutline.Alpha = 1;
                return;
            }

            double rehearsalProgress = now >= HitObject.RehearsalStartTime
                ? HitObject.RehearsalProgressAt(now)
                : 0;
            setVisibleRange(reversalPreviewOutline, HitObject, 0, rehearsalProgress);
            reversalPreviewOutline.Alpha = rehearsalProgress > 0 ? 1 : 0;
        }

        private void updateHeadJudgement(double now)
        {
            if (headJudged || Judged)
                return;

            double headWindow = HitObject.HitWindows?.WindowFor(HitResult.Miss) ?? 0;
            if (now < HitObject.StartTime - headWindow)
                return;

            Vector2 stick = playfield.StickVector(HitObject.Side);
            float actualAngle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Y, stick.X) * 180 / MathF.PI);
            float angleError = Math.Abs(SticksHitObject.DeltaAngle(actualAngle, HitObject.Angle));

            if (stick.Length >= tracking_magnitude && angleError <= HitObject.LenientHalfAngle)
            {
                headJudged = true;
                headHit = true;
                headHitTime = now;
                playHeadSample();
            }
            else if (now > HitObject.StartTime + headWindow)
            {
                MarkHeadMiss();
            }
        }

        internal void MarkHeadMiss() => headJudged = true;

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

            double trackedFraction = trackedTime / HitObject.AvailableTrackingDuration(headHitTime);
            if (headHit && trackedFraction >= SticksSlider.REQUIRED_TRACKING_FRACTION)
                ApplyMaxResult();
            else
                ApplyMinResult();
        }

        private void playHeadSample()
        {
            Samples.Volume.Value = 1;
            Samples.Frequency.Value = 1;
            base.PlaySamples();
        }

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
        }

        protected override void ClearNestedHitObjects()
        {
            base.ClearNestedHitObjects();
            nestedHitObjectContainer.Clear(false);
        }

        protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject) => hitObject switch
        {
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

        private static void setVisibleRange(CircularProgress drawable, SticksSlider slider, double start, double end)
        {
            start = Math.Clamp(start, 0, 1);
            end = Math.Clamp(end, start, 1);
            double clockwiseStart = slider.ArcAngle >= 0
                ? slider.Angle + slider.ArcAngle * start
                : slider.Angle + slider.ArcAngle * end;
            drawable.Rotation = (float)(90 + clockwiseStart);
            drawable.Progress = (float)(Math.Abs(slider.ArcAngle) * (end - start) / 360);
        }

        private static Color4 colourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;
    }
}
