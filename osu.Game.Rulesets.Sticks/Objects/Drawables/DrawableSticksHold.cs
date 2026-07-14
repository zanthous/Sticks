// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksHold : DrawableHitObject<SticksHitObject>, ISticksApproachRateAdjustable
    {
        private const float tracking_magnitude = 0.56f;

        private readonly SticksArcMarker headMarker;
        private readonly SmoothPath durationRail;
        private readonly Circle durationCursor;
        private readonly PausableSkinnableSound holdingSample;
        private readonly Container nestedHitObjectContainer;
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
        private long observedSequence;

        public new SticksHold HitObject => (SticksHold)base.HitObject;

        public override bool HandlePositionalInput => false;

        public override bool DisplayResult => false;

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

            AddInternal(nestedHitObjectContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                AlwaysPresent = true,
                Depth = -20,
            });

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
        private void load(SticksPlayfield sticksPlayfield)
        {
            playfield = sticksPlayfield;
            observedSequence = playfield.FlickSequence(HitObject.Side);
        }

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

            updateHeadJudgement(now);
            updateHeadCue(now);

            // Like a standard slider, tracking can resume after the head or any intermediate
            // checkpoint was missed. Only checkpoints crossed while away are lost.
            bool currentlyTracking = active && isStickInRange();
            updateHoldingSample(currentlyTracking && !Judged);
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
                observedSequence = playfield.FlickSequence(HitObject.Side);
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

        private void updateHeadJudgement(double now)
        {
            if (headJudged || Judged)
                return;

            long sequence = playfield.FlickSequence(HitObject.Side);
            if (sequence != observedSequence)
            {
                observedSequence = sequence;
                SticksInputTracker.FlickEvent flick = playfield.LastFlick(HitObject.Side);
                double offset = flick.Time - HitObject.StartTime;

                if (offset >= -SticksFlick.EARLY_HIT_WINDOW
                    && offset <= SticksFlick.LATE_HIT_WINDOW
                    && playfield.TryConsumeFlick(HitObject.Side, flick.Sequence))
                {
                    float angleError = Math.Abs(SticksHitObject.DeltaAngle(flick.Angle, HitObject.Angle));
                    headJudged = true;
                    drawableHead.ApplyHead(offset, angleError);
                    headHit = drawableHead.BothComponentsHit;

                    if (headHit)
                        playHeadSample();

                    return;
                }
            }

            double timeOffset = now - HitObject.StartTime;
            if (drawableHead.HitObject.HitWindows is not null
                && !drawableHead.HitObject.HitWindows.CanBeHit(timeOffset))
                markHeadMiss();
        }

        private void markHeadMiss()
        {
            if (headJudged)
                return;

            headJudged = true;
            drawableHead.ApplyMiss();
        }

        private void updateHeadCue(double now)
        {
            if (now < HitObject.StartTime)
            {
                headMarker.Alpha = 1;
                return;
            }

            double timeSinceStart = now - HitObject.StartTime;
            if (timeSinceStart <= 120)
            {
                headMarker.Alpha = 1 - (float)(timeSinceStart / 120);
                return;
            }

            headMarker.Alpha = 0;
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (Judged || Time.Current < HitObject.EndTime)
                return;

            updateHoldingSample(false);

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
