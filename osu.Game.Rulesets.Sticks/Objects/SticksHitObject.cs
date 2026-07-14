// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Objects
{
    public abstract class SticksHitObject : HitObject, IHasPosition
    {
        private static readonly Vector2 legacy_centre = new Vector2(256, 192);

        private const float legacy_left_radius = 160;
        private const float legacy_right_radius = 105;

        public const float VISIBLE_ARC_SPAN = 20;
        public const float PRECISE_HALF_ANGLE = VISIBLE_ARC_SPAN / 2;
        public const float LENIENT_HALF_ANGLE = VISIBLE_ARC_SPAN;

        public const float EASY_CIRCLE_SIZE = 3;
        public const float DEFAULT_CIRCLE_SIZE = 4;
        public const float HARD_CIRCLE_SIZE = 5.4f;

        public const float EASY_HIT_ANGLE = 30;
        public const float DEFAULT_HIT_ANGLE = 20;
        public const float HARD_HIT_ANGLE = 15;

        private static readonly double circle_size_curve_exponent =
            Math.Log((DEFAULT_HIT_ANGLE - HARD_HIT_ANGLE) / (EASY_HIT_ANGLE - HARD_HIT_ANGLE))
            / Math.Log(1 - (DEFAULT_CIRCLE_SIZE - EASY_CIRCLE_SIZE) / (HARD_CIRCLE_SIZE - EASY_CIRCLE_SIZE));

        public float PrimaryHitAngle { get; set; } = VISIBLE_ARC_SPAN;

        public float SecondaryHitAngle { get; set; } = VISIBLE_ARC_SPAN;

        public float PreciseHalfAngle => PrimaryHitAngle / 2;

        public float LenientHalfAngle => (PrimaryHitAngle + SecondaryHitAngle) / 2;

        private StickSide side;

        public StickSide Side
        {
            get => side;
            set
            {
                side = value;
                RefreshLegacyEditorMarker();
            }
        }

        private float angle;

        public float Angle
        {
            get => angle;
            set
            {
                angle = value;
                RefreshLegacyEditorMarker();
            }
        }

        /// <summary>
        /// A mode-0 carrier position used by lazer's legacy encoder. Sticks gameplay continues to
        /// use <see cref="Side"/> and <see cref="Angle"/> directly.
        /// </summary>
        [JsonIgnore]
        public Vector2 Position
        {
            get
            {
                float radians = Angle * MathF.PI / 180;
                float radius = Side == StickSide.Left ? legacy_left_radius : legacy_right_radius;
                return legacy_centre + new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * radius;
            }
            set
            {
                Vector2 offset = value - legacy_centre;
                Side = offset.Length >= (legacy_left_radius + legacy_right_radius) / 2
                    ? StickSide.Left
                    : StickSide.Right;
                Angle = NormaliseAngle(MathF.Atan2(offset.Y, offset.X) * 180 / MathF.PI);
            }
        }

        [JsonIgnore]
        public float X
        {
            get => Position.X;
            set => Position = new Vector2(value, Position.Y);
        }

        [JsonIgnore]
        public float Y
        {
            get => Position.Y;
            set => Position = new Vector2(Position.X, value);
        }

        private bool legacyEditorMarkerEnabled;

        /// <summary>
        /// Enables and synchronises the lossless custom-sample marker required by stock editor
        /// save and undo. The marker itself is the object's sole normal sample, avoiding duplicate
        /// hitsound playback and LegacyBeatmapEncoder's single-normal-sample invariant.
        /// </summary>
        public void EnsureLegacyEditorMarker()
        {
            legacyEditorMarkerEnabled = true;
            SticksAuthoredBeatmapCodec.SynchroniseMarker(this);
        }

        protected void RefreshLegacyEditorMarker()
        {
            if (legacyEditorMarkerEnabled)
                SticksAuthoredBeatmapCodec.SynchroniseMarker(this);
        }

        /// <summary>
        /// Returns the samples which should actually be played for this object. The lossless
        /// editor marker is metadata carried as a legacy normal sample, so it must be replaced by
        /// an ordinary normal sample before reaching the skin/sample lookup pipeline.
        /// </summary>
        public IList<HitSampleInfo> CreatePlayableSamples() => CreatePlayableSamples(Samples);

        /// <summary>
        /// Creates the continuous slider samples using the marker-free playback samples.
        /// </summary>
        public IList<HitSampleInfo> CreatePlayableSlidingSamples()
        {
            IList<HitSampleInfo> playableSamples = CreatePlayableSamples();
            var slidingSamples = new List<HitSampleInfo>();

            HitSampleInfo normalSample = playableSamples.FirstOrDefault(sample => sample.Name == HitSampleInfo.HIT_NORMAL);
            if (normalSample != null)
                slidingSamples.Add(normalSample.With("sliderslide"));

            HitSampleInfo whistleSample = playableSamples.FirstOrDefault(sample => sample.Name == HitSampleInfo.HIT_WHISTLE);
            if (whistleSample != null)
                slidingSamples.Add(whistleSample.With("sliderwhistle"));

            return slidingSamples;
        }

        internal static IList<HitSampleInfo> CreatePlayableSamples(IEnumerable<HitSampleInfo> sourceSamples)
        {
            HitSampleInfo[] source = sourceSamples.ToArray();
            bool hasRegularNormal = source.Any(sample => sample.Name == HitSampleInfo.HIT_NORMAL
                                                        && !SticksAuthoredBeatmapCodec.IsMarker(sample));
            bool suppliedMarkerNormal = false;
            var playableSamples = new List<HitSampleInfo>(source.Length);

            foreach (HitSampleInfo sample in source)
            {
                if (!SticksAuthoredBeatmapCodec.IsMarker(sample))
                {
                    playableSamples.Add(sample.With());
                    continue;
                }

                if (!hasRegularNormal && !suppliedMarkerNormal)
                {
                    playableSamples.Add(new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: sample.Volume));
                    suppliedMarkerNormal = true;
                }
            }

            return playableSamples;
        }

        public StickSide? SyncedNoteSide { get; set; }

        public float SyncedNoteAngle { get; set; }

        public double ApproachDuration { get; private set; } = 850;

        public static double ApproachDurationFor(float approachRate) =>
            IBeatmapDifficultyInfo.DifficultyRange(approachRate, 1200, 850, 500);

        /// <summary>
        /// Computes the full width of both angular grading bands from circle size.
        /// The curve reaches its 20 degree reference at CS 4 while easing out towards
        /// the 15 degree lower bound, rather than changing abruptly near CS 5.4.
        /// </summary>
        public static float HitAngleForCircleSize(float circleSize)
        {
            if (circleSize <= EASY_CIRCLE_SIZE)
                return EASY_HIT_ANGLE;

            if (circleSize >= HARD_CIRCLE_SIZE)
                return HARD_HIT_ANGLE;

            double progress = (circleSize - EASY_CIRCLE_SIZE) / (HARD_CIRCLE_SIZE - EASY_CIRCLE_SIZE);
            double remaining = Math.Clamp(1 - progress, 0, 1);
            return (float)(HARD_HIT_ANGLE + (EASY_HIT_ANGLE - HARD_HIT_ANGLE) * Math.Pow(remaining, circle_size_curve_exponent));
        }

        public void ApplyPlayerApproachRate(float approachRate)
        {
            ApproachDuration = ApproachDurationFor(approachRate);

            foreach (HitObject nested in NestedHitObjects)
            {
                if (nested is SticksHitObject sticksNested)
                    sticksNested.ApplyPlayerApproachRate(approachRate);
            }
        }

        public override Judgement CreateJudgement() => new SticksJudgement();

        protected override HitWindows CreateHitWindows() => new SticksHitWindows();

        public HitResult ResultForCurrentAngleError(float angleError)
        {
            angleError = Math.Abs(angleError);

            if (angleError <= PreciseHalfAngle)
                return HitResult.Great;

            if (angleError <= LenientHalfAngle)
                return HitResult.Ok;

            return HitResult.Miss;
        }

        /// <summary>
        /// Resolves the two equally-weighted parts of a Sticks note. A note only succeeds when
        /// both its timing and angular requirements succeed; either failure makes both recorded
        /// components misses so no partial accuracy is awarded for a physically missed note.
        /// </summary>
        public static (HitResult Timing, HitResult Angle) ResolveComponentResults(HitResult timingResult, HitResult angleResult)
        {
            bool timingHit = timingResult is HitResult.Great or HitResult.Ok or HitResult.Meh;
            bool angleHit = angleResult is HitResult.Great or HitResult.Ok;

            return timingHit && angleHit
                ? (timingResult, angleResult)
                : (HitResult.Miss, HitResult.Miss);
        }

        public static HitResult ResultForAngleError(float angleError)
        {
            angleError = Math.Abs(angleError);
            if (angleError <= PRECISE_HALF_ANGLE) return HitResult.Great;
            if (angleError <= LENIENT_HALF_ANGLE) return HitResult.Ok;
            return HitResult.Miss;
        }

        /// <summary>
        /// Makes an approaching note grow slowly while it is first being read, then accelerate
        /// towards its final size as the hit time approaches.
        /// </summary>
        public static double ApproachGrowthProgress(double linearProgress)
        {
            linearProgress = Math.Clamp(linearProgress, 0, 1);
            return linearProgress * linearProgress * linearProgress;
        }

        protected override void ApplyDefaultsToSelf(ControlPointInfo controlPointInfo, IBeatmapDifficultyInfo difficulty)
        {
            base.ApplyDefaultsToSelf(controlPointInfo, difficulty);
            PrimaryHitAngle = SecondaryHitAngle = HitAngleForCircleSize(difficulty.CircleSize);
            ApproachDuration = ApproachDurationFor(difficulty.ApproachRate);
        }

        public static float NormaliseAngle(float angle)
        {
            angle %= 360;
            return angle < 0 ? angle + 360 : angle;
        }

        public static float DeltaAngle(float from, float to)
        {
            float delta = NormaliseAngle(to) - NormaliseAngle(from);
            if (delta > 180) delta -= 360;
            if (delta < -180) delta += 360;
            return delta;
        }

    }
}
