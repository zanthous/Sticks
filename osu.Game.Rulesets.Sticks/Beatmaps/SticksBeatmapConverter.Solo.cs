using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    public partial class SticksBeatmapConverter
    {
        private static void trimSoloSliders(Beatmap<SticksHitObject> converted, CancellationToken cancellationToken)
        {
            SticksHitObject[] ordered = converted.HitObjects.OrderBy(note => note.StartTime).ToArray();
            for (int i = 0; i < ordered.Length - 1; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double end = ordered[i + 1].StartTime;
                if (ordered[i] is not SticksSlider slider || slider.EndTime <= end)
                    continue;

                // Keep the original path and speed up to the next attack. Merely shortening
                // Duration would compress all the motion and move every earlier reversal.
                var arcs = new List<float>();
                var durations = new List<double>();
                bool cutInsideSegment = false;
                for (int segment = 0; segment < slider.SegmentCount; segment++)
                {
                    double available = end - slider.SegmentStartTimeAt(segment);
                    if (available <= 0)
                        break;

                    double fullDuration = slider.SegmentDurationAt(segment);
                    double keptDuration = Math.Min(available, fullDuration);
                    arcs.Add(slider.SegmentArcAngleAt(segment) * (float)(keptDuration / fullDuration));
                    durations.Add(keptDuration);
                    cutInsideSegment = available < fullDuration;
                    if (cutInsideSegment)
                        break;
                }

                // Do not move a future reversal/tail sound to an invented release point.
                if (cutInsideSegment && slider.NodeSamples.Count > arcs.Count)
                    slider.NodeSamples[arcs.Count] = cloneSamples(slider.Samples);
                while (slider.NodeSamples.Count > arcs.Count + 1)
                    slider.NodeSamples.RemoveAt(slider.NodeSamples.Count - 1);

                slider.SetTimedSegments(arcs, durations);
                slider.Duration = end - slider.StartTime;
            }
        }
    }
}
