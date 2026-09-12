#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    public partial class SticksBeatmapConverter
    {
        private void applyEncoreObjects(Beatmap<SticksHitObject> converted, IBeatmap source, CancellationToken cancellationToken)
        {
            HitObject[] originals = source.HitObjects.OrderBy(hitObject => hitObject.StartTime).ToArray();
            var result = converted.HitObjects.ToList();
            addEncoreClicks(result, originals, source, cancellationToken);
            converted.HitObjects.Clear();
            converted.HitObjects.AddRange(result.OrderBy(hitObject => hitObject.StartTime).ThenBy(hitObject => hitObject.Side));
        }

        private void addEncoreClicks(List<SticksHitObject> converted, HitObject[] originals, IBeatmap source,
                                     CancellationToken cancellationToken)
        {
            double lastClickTime = double.NegativeInfinity;
            var lastClickBySide = new[] { double.NegativeInfinity, double.NegativeInfinity };
            IGrouping<double, HitObject>[] groups = originals.GroupBy(hitObject => hitObject.StartTime).ToArray();
            SticksHitObject[] directionalObjects = converted.ToArray();
            var previousSourceEndTimes = new double[groups.Length];
            double latestSourceEndTime = double.NegativeInfinity;

            for (int index = 0; index < groups.Length; index++)
            {
                previousSourceEndTimes[index] = latestSourceEndTime;
                latestSourceEndTime = Math.Max(latestSourceEndTime, groups[index].Max(hitObject => hitObject.GetEndTime()));
            }

            // OD supplies the map's intended difficulty, while the actual surrounding
            // objects below determine whether this particular accent is easy to approach.
            // A low-OD stream must not become eligible merely because of its hitsounds.
            double difficulty = source.Difficulty.OverallDifficulty;
            double intensity = Math.Clamp(((double.IsFinite(difficulty) ? difficulty : 5) - 3) / 4, 0, 1);
            double minimumGapMilliseconds = 500 - 350 * intensity;
            // Leave a small musical margin below a half beat at the hard end: source
            // timestamps round to milliseconds, so a true half beat may be 171 or 172 ms.
            double minimumGapBeats = 1 - 0.55 * intensity;
            double minimumClickMilliseconds = 8000 - 4000 * intensity;
            double minimumClickBeats = 16 - 8 * intensity;

            for (int index = 0; index < groups.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double time = groups[index].Key;
                if (groups[index].Count() != 1 || groups[index].First() is IHasDuration { Duration: > 0 })
                    continue;

                HitObject original = groups[index].First();
                bool accented = original.Samples.Any(sample => sample.Name is HitSampleInfo.HIT_CLAP or HitSampleInfo.HIT_FINISH);

                TimingControlPoint timing = source.ControlPointInfo.TimingPointAt(time);
                double beatLength = validBeatLength(timing.BeatLength);
                double beatPosition = (time - timing.Time) / beatLength;
                long beat = (long)Math.Round(beatPosition);
                bool downbeat = Math.Abs(beatPosition - beat) < 0.08 && ((beat % 4) + 4) % 4 == 0;
                double previousGap = time - previousSourceEndTimes[index];
                double nextGap = index + 1 < groups.Length ? groups[index + 1].Key - time : double.PositiveInfinity;
                double minimumGap = Math.Max(minimumGapMilliseconds, beatLength * minimumGapBeats);

                // Every accent needs breathing room on both sides, including the release
                // of any earlier sustain. Hitsounds select meaningful moments but never
                // bypass these checks to insert buttons into a dense source phrase.
                if ((!accented && !downbeat) || previousGap < minimumGap || nextGap < minimumGap)
                    continue;
                if (time - lastClickTime < Math.Max(minimumClickMilliseconds, beatLength * minimumClickBeats))
                    continue;

                SticksFlick? replacement = directionalObjects.OfType<SticksFlick>()
                                                            .FirstOrDefault(flick => Math.Abs(flick.StartTime - time) < 0.01);

                // Conversion can add a partner, an accompaniment note, or a sustain over
                // a sparse source accent. Check the complete generated gestures as well,
                // preserving simultaneous heads and requiring room after duration tails.
                if (replacement == null || directionalObjects.Any(hitObject => hitObject != replacement
                    && hitObject.StartTime - time < minimumGap && time - hitObject.GetEndTime() < minimumGap))
                    continue;

                // Isolated buttons can alternate hands independently of directional phrases.
                StickSide side = lastClickBySide[0] <= lastClickBySide[1] ? StickSide.Left : StickSide.Right;
                converted.Remove(replacement);

                converted.Add(new SticksClick
                {
                    StartTime = time,
                    Side = side,
                    Samples = cloneSamples(replacement.Samples),
                });
                lastClickTime = time;
                lastClickBySide[side == StickSide.Left ? 0 : 1] = time;
            }
        }
    }
}
