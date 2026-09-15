using System;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks
{
    /// <summary>
    /// Converts Sticks' map difficulty settings into relative accuracy demands.
    /// CS 4 and OD 5 form the neutral reference. Approach rate is intentionally absent because
    /// Sticks treats it as a player readability setting rather than a source of star difficulty.
    /// </summary>
    public static class SticksDifficultyScaling
    {
        public const float REFERENCE_CIRCLE_SIZE = 4;
        public const float REFERENCE_OVERALL_DIFFICULTY = 5;
        public const double STAR_RATING_CALIBRATION_EXPONENT = 1.376;

        private static readonly double reference_primary_angle = SticksHitObject.HitAngleForCircleSize(REFERENCE_CIRCLE_SIZE);
        private static readonly double reference_secondary_angle = reference_primary_angle / 2;
        private static readonly double reference_timing_window = greatWindowFor(REFERENCE_OVERALL_DIFFICULTY);

        /// <summary>
        /// Converts the model's raw combined strain into the displayed star scale. The curve is
        /// anchored near 3.4 raw stars = 1.5 displayed stars and at 30 = 30, reducing the inflated
        /// low and middle ranges without flattening the high-end ordering into one multiplier.
        /// </summary>
        public static double CalibrateStarRating(double rawStarRating)
        {
            double normalised = Math.Clamp(rawStarRating / 30, 0, 1);
            return 30 * Math.Pow(normalised, STAR_RATING_CALIBRATION_EXPONENT);
        }

        /// <summary>
        /// Returns raw angular precision demand relative to CS 4.
        /// This is a diagnostic demand ratio, not a whole-map star multiplier.
        /// </summary>
        public static double AngularPrecisionMultiplier(float circleSize)
        {
            float hitAngle = SticksHitObject.HitAngleForCircleSize(circleSize);
            return AngularPrecisionMultiplier(hitAngle, hitAngle / 2);
        }

        /// <summary>
        /// Returns raw angular precision demand from the grading bands actually applied to an object.
        /// This keeps Difficulty Adjust overrides and any future per-object widths visible to difficulty calculation.
        /// </summary>
        public static double AngularPrecisionMultiplier(float primaryHitAngle, float secondaryHitAngle)
        {
            double greatPrecision = reference_primary_angle / Math.Max(1, primaryHitAngle);
            double successfulHitPrecision = (reference_primary_angle + reference_secondary_angle)
                                            / Math.Max(1, primaryHitAngle + secondaryHitAngle);
            return Math.Sqrt(greatPrecision * successfulHitPrecision);
        }

        /// <summary>
        /// Converts raw angular demand into an uncapped adjustment relative to CS 4.
        /// Tighter windows use a 0.606 * (precision - 1)^1.28 proportional bonus; wider windows
        /// retain a square-root reduction. With the usual secondary band at half the primary
        /// width W, precision is 27.5 / W. Actual object bands also account for DA overrides.
        /// </summary>
        public static double AngularPrecisionStarAdjustment(double baseStars, double angularPrecisionMultiplier)
        {
            double precision = Math.Max(0, angularPrecisionMultiplier);
            double relativeAdjustment = precision > 1
                ? 0.606 * Math.Pow(precision - 1, 1.28)
                : Math.Sqrt(precision) - 1;

            return Math.Max(0, baseStars) * relativeAdjustment;
        }

        /// <summary>
        /// Returns timing precision demand relative to OD 5, based on the actual Great window used in gameplay.
        /// </summary>
        public static double TimingPrecisionMultiplier(float overallDifficulty) =>
            reference_timing_window / Math.Max(1, greatWindowFor(overallDifficulty));

        /// <summary>
        /// Returns the mild OD contribution used by current osu!standard reading difficulty, normalised at OD 5.
        /// Raw inverse hit-window scaling is intentionally not applied to the whole star rating.
        /// </summary>
        public static double OverallDifficultyMultiplier(float overallDifficulty)
        {
            static double standardReadingFactor(float od) => 0.825 + Math.Pow(Math.Max(0, od), 2.2) / 1125;

            return standardReadingFactor(overallDifficulty) / standardReadingFactor(REFERENCE_OVERALL_DIFFICULTY);
        }

        /// <summary>
        /// Returns the timing multiplier applied after combining the independent Sticks skills.
        /// Angular precision is applied separately as a star adjustment, and approach rate
        /// is intentionally not part of this calculation.
        /// </summary>
        public static double StarRatingPrecisionMultiplier(IBeatmapDifficultyInfo difficulty) =>
            OverallDifficultyMultiplier(difficulty.OverallDifficulty);

        internal static double NoteSizeStrainMultiplier(SticksHitObject note)
        {
            if (note is SticksClick or SticksSlice || note.SizeMultiplier == 1)
                return 1;
            double precision = AngularPrecisionMultiplier(note.PrimaryHitAngle, note.SecondaryHitAngle);
            double baseline = precision * note.SizeMultiplier;
            double ratio = (1 + AngularPrecisionStarAdjustment(1, precision))
                           / (1 + AngularPrecisionStarAdjustment(1, baseline));
            return Math.Pow(ratio, 2 / STAR_RATING_CALIBRATION_EXPONENT);
        }

        internal static double GreatWindowFor(float overallDifficulty) => greatWindowFor(overallDifficulty);

        private static double greatWindowFor(float overallDifficulty)
        {
            var hitWindows = new SticksHitWindows();
            hitWindows.SetDifficulty(overallDifficulty);
            return hitWindows.WindowFor(HitResult.Great);
        }
    }
}
