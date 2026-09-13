using System;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Applies the base converter's reversal readability limits to added timed paths.
    /// Speed changes and dwells do not introduce artificial reversal boundaries.
    /// </summary>
    internal static class SticksCounterpointMotion
    {
        public static bool HasReadableReversals(SticksSlider slider)
        {
            double maximumVelocity = 0;
            for (int i = 0; i < slider.SegmentCount; i++)
            {
                double duration = slider.SegmentDurationAt(i);
                float arc = slider.SegmentArcAngleAt(i);
                if (!double.IsFinite(duration) || duration <= 0 || !float.IsFinite(arc))
                    return false;
                maximumVelocity = Math.Max(maximumVelocity, Math.Abs(arc) / duration * 1000);
            }

            double elapsed = 0;
            double lastReversal = 0;
            bool hasReversal = false;
            for (int i = 0; i < slider.SegmentCount; i++)
            {
                elapsed += slider.SegmentDurationAt(i);
                if (!slider.SegmentEndsWithReversal(i))
                    continue;

                hasReversal = true;
                if (!longEnough(elapsed - lastReversal))
                    return false;
                lastReversal = elapsed;
            }

            return !hasReversal
                   || longEnough(elapsed - lastReversal)
                   && maximumVelocity < SticksBeatmapConverter.MAX_REVERSAL_ANGULAR_VELOCITY - 0.001;
        }

        private static bool longEnough(double duration) =>
            duration + 0.001 >= SticksBeatmapConverter.MIN_GENERATED_REVERSAL_SPAN_DURATION;
    }
}
