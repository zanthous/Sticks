using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    public partial class SticksBeatmapConverter
    {
        private readonly HashSet<HitObject> rapidJumpHeads = new HashSet<HitObject>();

        private bool usesSourceAwarePatterns => ConversionMode is SticksConversionMode.Duet or SticksConversionMode.ParityDuet;

        private void findRapidJumpRuns(HitObject[] objects, IBeatmap beatmap)
        {
            rapidJumpHeads.Clear();
            if (!usesSourceAwarePatterns)
                return;

            // Match osu!standard's displayed circle size. Disjoint circles require
            // a new aim target, including position jumps along the same radial bearing.
            float cs = float.IsFinite(beatmap.Difficulty.CircleSize) ? beatmap.Difficulty.CircleSize : 4;
            double diameter = 128 * LegacyRulesetExtensions.CalculateScaleFromCircleSize(cs, true);
            double diameterSquared = diameter * diameter;
            int runStart = 0;

            for (int i = 1; i <= objects.Length; i++)
            {
                if (i < objects.Length)
                {
                    HitObject previous = objects[i - 1];
                    HitObject current = objects[i];
                    double gap = current.StartTime - previous.StartTime;
                    if (!hasDuration(previous) && !hasDuration(current)
                        && previous is IHasPosition first && current is IHasPosition second
                        && gap > 0.01 && gap <= RAPID_ALTERNATION_THRESHOLD
                        && (second.Position - first.Position).LengthSquared > diameterSquared
                        && beatmap.ControlPointInfo.TimingPointAt(previous.StartTime).Time
                           == beatmap.ControlPointInfo.TimingPointAt(current.StartTime).Time)
                        continue;
                }

                // Four attacks form the smallest phrase the sustain planners can
                // consume. Keep their manual pulse instead of interpreting alternating
                // distant targets as two slow, independently drifting tracks.
                if (i - runStart >= 4)
                {
                    for (int j = runStart; j < i; j++)
                        rapidJumpHeads.Add(objects[j]);
                }

                runStart = i;
            }
        }
    }
}
