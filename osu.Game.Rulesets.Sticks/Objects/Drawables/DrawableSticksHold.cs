// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksHold : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable
    {
        private const float tracking_magnitude = 0.56f;

        private SticksArcMarker headMarker;
        private readonly SmoothPath durationRail;
        private readonly Circle durationCursor;
        private readonly PausableSkinnableSound holdingSample;
        private Vector2 railStart;
        private Vector2 railEnd;
        private StickSide displayedSide;
        private float displayedAngle = float.NaN;
        private double displayedDuration = double.NaN;
        private SticksPlayfield playfield = null!;
        private bool headAcquired;
        private double trackedTime;
        private double lastPlaybackTime = double.NaN;
        private bool currentlyTracking;

        public new SticksHold HitObject => (SticksHold)base.HitObject;

        public override bool HandlePositionalInput => false;

        public override IEnumerable<HitSampleInfo> GetSamples() => HitObject.CreatePlayableSamples();

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

            AddInternal(durationRail = new SmoothPath
            {
                AutoSizeAxes = Axes.None,
                Size = new Vector2(SticksPlayfield.SIZE),
                PathRadius = 4,
                Colour = colourFor(hitObject.Side),
                Alpha = 0,
                Depth = 10,
                Vertices = new[] { railStart, railEnd },
            });

            AddInternal(durationCursor = new Circle
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = railEnd,
                Size = new Vector2(11),
                Colour = Color4.White,
                Alpha = 0,
                Depth = 5,
            });

            AddInternal(headMarker = createHeadMarker());

            AddInternal(holdingSample = new PausableSkinnableSound
            {
                Looping = true,
                MinimumSampleVolume = MINIMUM_SAMPLE_VOLUME,
            });

            refreshGeometry();
        }

        [BackgroundDependencyLoader]
        private void load(SticksPlayfield sticksPlayfield) => playfield = sticksPlayfield;

        protected override void Update()
        {
            base.Update();

            refreshGeometry();

            double now = Time.Current;
            bool active = now >= HitObject.StartTime && now <= HitObject.EndTime;
            double approachProgress = Math.Clamp((now - (HitObject.StartTime - HitObject.ApproachDuration)) / Math.Max(1, HitObject.ApproachDuration), 0, 1);
            double headGrowth = SticksHitObject.ApproachGrowthProgress(approachProgress);
            headMarker.Span = HitObject.PrimaryHitAngle * (float)(0.2 + 0.8 * headGrowth);
            double progress = Math.Clamp((now - HitObject.StartTime) / Math.Max(1, HitObject.Duration), 0, 1);
            durationRail.Alpha = now < HitObject.EndTime ? 0.38f : 0;
            durationCursor.Alpha = active ? 0.9f : 0;
            durationCursor.Position = Vector2.Lerp(railEnd, railStart, (float)progress);

            updateHeadAcquisition(now);
            currentlyTracking = false;

            if (active && headAcquired && !Judged)
            {
                currentlyTracking = isStickInRange();

                if (currentlyTracking
                    && !double.IsNaN(lastPlaybackTime)
                    && now > lastPlaybackTime)
                    trackedTime += Math.Min(50, now - lastPlaybackTime);
            }

            updateHoldingSample(active && headAcquired && currentlyTracking && !Judged);

            lastPlaybackTime = now;
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
                RemoveInternal(headMarker, true);
                AddInternal(headMarker = createHeadMarker());
            }
            else
            {
                headMarker.Angle = HitObject.Angle;
            }

            displayedSide = HitObject.Side;
            displayedAngle = HitObject.Angle;
            displayedDuration = HitObject.Duration;
        }

        private SticksArcMarker createHeadMarker() => new SticksArcMarker(HitObject.Side, colourFor(HitObject.Side), true)
        {
            Angle = HitObject.Angle,
            Span = HitObject.PrimaryHitAngle * 0.2f,
        };

        private void updateHeadAcquisition(double now)
        {
            if (headAcquired || Judged)
                return;

            double window = HitObject.HitWindows?.WindowFor(HitResult.Miss) ?? 0;
            if (now < HitObject.StartTime - window || now > HitObject.StartTime + window)
                return;

            Vector2 stick = playfield.StickVector(HitObject.Side);
            float actualAngle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Y, stick.X) * 180 / MathF.PI);
            float angleError = Math.Abs(SticksHitObject.DeltaAngle(actualAngle, HitObject.Angle));

            if (stick.Length >= tracking_magnitude && angleError <= HitObject.LenientHalfAngle)
            {
                headAcquired = true;
                base.PlaySamples();
            }
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (Judged || Time.Current < HitObject.EndTime)
                return;

            double trackedFraction = trackedTime / Math.Max(1, HitObject.Duration);
            updateHoldingSample(false);

            if (headAcquired && isStickInRange())
                base.PlaySamples();

            if (headAcquired && trackedFraction >= SticksHold.REQUIRED_TRACKING_FRACTION)
                ApplyMaxResult();
            else
                ApplyMinResult();
        }

        protected override double InitialLifetimeOffset => HitObject.ApproachDuration;

        void ISticksApproachRateAdjustable.RefreshApproachTransforms()
        {
            if (Judged)
                return;

            LifetimeStart = HitObject.StartTime - InitialLifetimeOffset;
            UpdateState(State.Value, true);
        }

        public override void PlaySamples()
        {
            // Hold start and end samples are played manually; automatic result playback
            // would otherwise duplicate the end sound.
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
            return stick.Length >= tracking_magnitude && angleError <= HitObject.LenientHalfAngle;
        }

        protected override void UpdateInitialTransforms() => this.FadeInFromZero(Math.Min(120, HitObject.ApproachDuration / 3));

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
