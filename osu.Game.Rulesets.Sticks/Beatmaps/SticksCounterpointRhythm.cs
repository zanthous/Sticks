#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Finds manual responses supported by nearby source attacks or an explicit node accent.
    /// Checkpoint timing alone does not establish a new manual articulation.
    /// </summary>
    internal static class SticksCounterpointRhythm
    {
        internal readonly record struct SupportedCheckpoint(SliderEventDescriptor Checkpoint, double Evidence, bool ExplicitAccent);
        internal sealed record Response(SliderEventDescriptor[] Checkpoints, double Evidence);
        private readonly record struct Pulse(double Period, double Phase, int Matches);

        internal static Response[] Select(HitObject[] source, int sourceIndex, IBeatmap beatmap,
                                          IReadOnlyList<SliderEventDescriptor> checkpoints, double minimumInterval, int maximumHeads,
                                          CancellationToken cancellationToken = default)
            => Select(Evaluate(source, sourceIndex, beatmap, checkpoints, cancellationToken), minimumInterval, maximumHeads, cancellationToken);

        /// <summary>
        /// Returns only supported original checkpoints. This also lets a caller assess
        /// a staggered sustain's entrance without imposing a second-hand trajectory.
        /// </summary>
        internal static SupportedCheckpoint[] Evaluate(HitObject[] source, int sourceIndex, IBeatmap beatmap,
                                                       IReadOnlyList<SliderEventDescriptor> checkpoints, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (checkpoints.Count == 0 || sourceIndex < 0 || sourceIndex >= source.Length || source[sourceIndex] is not IHasDuration { Duration: > 0 } duration)
                return Array.Empty<SupportedCheckpoint>();

            HitObject original = source[sourceIndex];
            var timing = beatmap.ControlPointInfo.TimingPointAt(original.StartTime);
            double beat = timing.BeatLength;
            if (!double.IsFinite(beat) || beat <= 0 || !double.IsFinite(original.StartTime) || !double.IsFinite(duration.EndTime)
                || beatmap.ControlPointInfo.TimingPointAt(duration.EndTime).Time != timing.Time)
                return Array.Empty<SupportedCheckpoint>();

            double tolerance = Math.Max(2, beat * 0.01);
            Pulse? pulse = findPulse(surroundingHeads(source, sourceIndex, beatmap, beat, timing.Time), beat, tolerance, cancellationToken);
            var result = new List<SupportedCheckpoint>();
            foreach (SliderEventDescriptor checkpoint in checkpoints.OrderBy(point => point.Time).Take(64))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (checkpoint.Type is not (SliderEventType.Tick or SliderEventType.Repeat or SliderEventType.Tail)
                    || !double.IsFinite(checkpoint.Time) || checkpoint.Time <= original.StartTime || checkpoint.Time > duration.EndTime + 0.01)
                    continue;

                bool accent = nodeAccent(original, checkpoint);
                bool onPulse = pulse is Pulse rhythm && distanceToPulse(checkpoint.Time, rhythm.Period, rhythm.Phase) <= tolerance;
                if (!accent && !onPulse)
                    continue;

                double evidence = accent ? 6 : 4;
                if (onPulse)
                    evidence += accent ? 2 : Math.Min(1, (pulse!.Value.Matches - 3) / 8.0);
                result.Add(new SupportedCheckpoint(checkpoint, evidence, accent));
            }

            return result.GroupBy(point => point.Checkpoint.Time)
                .Select(group => group.OrderByDescending(point => point.Evidence).First()).ToArray();
        }

        /// <summary>
        /// Returns the strongest response for each allowed head count. Interval checks
        /// concern the response itself; the caller remains responsible for lane occupancy.
        /// </summary>
        internal static Response[] Select(IReadOnlyList<SupportedCheckpoint> supported, double minimumInterval, int maximumHeads,
                                          CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(minimumInterval) || minimumInterval < 0 || maximumHeads <= 0)
                return Array.Empty<Response>();

            SupportedCheckpoint[] points = supported.Where(point => double.IsFinite(point.Checkpoint.Time) && double.IsFinite(point.Evidence))
                .OrderBy(point => point.Checkpoint.Time).GroupBy(point => point.Checkpoint.Time)
                .Select(group => group.OrderByDescending(point => point.Evidence).First()).Take(64).ToArray();
            maximumHeads = Math.Min(maximumHeads, 3);
            var best = new Selection?[points.Length + 1, maximumHeads + 1];
            for (int i = 0; i <= points.Length; i++)
                best[i, 0] = new Selection(Array.Empty<int>(), 0);

            for (int i = 1; i <= points.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int previous = i - 1;
                while (previous > 0 && points[i - 1].Checkpoint.Time - points[previous - 1].Checkpoint.Time < minimumInterval - 0.01)
                    previous--;
                for (int count = 1; count <= maximumHeads; count++)
                {
                    best[i, count] = best[i - 1, count];
                    if (best[previous, count - 1] is not Selection prefix)
                        continue;
                    double score = prefix.Score + points[i - 1].Evidence;
                    if (best[i, count] is not Selection current || score > current.Score)
                        best[i, count] = new Selection(prefix.Indices.Append(i - 1).ToArray(), score);
                }
            }

            return Enumerable.Range(1, maximumHeads).Select(count => best[points.Length, count])
                .OfType<Selection>().Select(selection => new Response(selection.Indices.Select(index => points[index].Checkpoint).ToArray(),
                    selection.Score / selection.Indices.Length + 0.75 * (selection.Indices.Length - 1)))
                .OrderByDescending(response => response.Evidence).ToArray();
        }

        private static HitObject[] surroundingHeads(HitObject[] source, int index, IBeatmap beatmap, double beat, double timingStart)
        {
            var heads = new List<HitObject> { source[index] };
            foreach (int direction in new[] { -1, 1 })
            {
                for (int offset = 1; offset <= 8; offset++)
                {
                    int next = index + offset * direction;
                    if (next < 0 || next >= source.Length)
                        break;
                    HitObject before = source[direction > 0 ? next - 1 : next];
                    HitObject after = source[direction > 0 ? next : next + 1];
                    HitObject note = source[next];
                    if (!double.IsFinite(note.StartTime) || beatmap.ControlPointInfo.TimingPointAt(note.StartTime).Time != timingStart
                        || after.StartTime - before.GetEndTime() > beat * 2
                        || note.StartTime < source[index].StartTime - beat * 4 || note.StartTime > source[index].GetEndTime() + beat * 4)
                        break;
                    heads.Add(note);
                }
            }
            return heads.OrderBy(note => note.StartTime).ToArray();
        }

        private static Pulse? findPulse(HitObject[] context, double beat, double tolerance, CancellationToken cancellationToken)
        {
            double[] heads = context.Select(note => note.StartTime).Distinct().ToArray();
            if (heads.Length < 3)
                return null;
            // A fill may occur between pulse attacks. Derive periods from nearby
            // pairs, then require three consecutive attacks on that particular phase.
            double[] gaps = heads.SelectMany((first, i) => heads.Skip(i + 1).Take(4).Select(second => second - first))
                .Where(gap => gap > tolerance * 2 && gap <= beat * 2).ToArray();
            var sustains = new List<(double Start, double End)>();
            foreach (HitObject note in context.Where(note => note.GetEndTime() > note.StartTime))
            {
                if (sustains.Count > 0 && note.StartTime <= sustains[^1].End)
                    sustains[^1] = (sustains[^1].Start, Math.Max(sustains[^1].End, note.GetEndTime()));
                else
                    sustains.Add((note.StartTime, note.GetEndTime()));
            }
            Pulse? best = null;
            double bestScore = 0;
            var evaluatedPeriods = new HashSet<double>();
            foreach (double gap in gaps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double[] similar = gaps.Where(other => Math.Abs(other - gap) <= tolerance).ToArray();
                if (similar.Length < 2)
                    continue;
                double period = similar.Average();
                if (!evaluatedPeriods.Add(period))
                    continue;
                foreach (double phase in heads)
                {
                    double[] aligned = heads.Where(time => distanceToPulse(time, period, phase) <= tolerance).ToArray();
                    int directSteps = 0, run = 0, longestRun = 0;
                    for (int i = 1; i < aligned.Length; i++)
                    {
                        if (Math.Abs(aligned[i] - aligned[i - 1] - period) <= tolerance)
                        {
                            directSteps++;
                            longestRun = Math.Max(longestRun, ++run);
                        }
                        else
                            run = 0;
                    }
                    if (aligned.Length < 3 || longestRun < 2)
                        continue;
                    // A shared fine subdivision is weak evidence when most of its
                    // slots have no manual attack. Existing sustains explain missing
                    // heads and must not dilute a pulse demonstrated before/after them.
                    double slots = Math.Round((aligned[^1] - aligned[0]) / period) + 1;
                    double emptySlots = 0;
                    foreach ((double start, double end) in sustains)
                    {
                        double firstSlot = Math.Max(0, Math.Floor((start + tolerance - aligned[0]) / period) + 1);
                        double lastSlot = Math.Min(slots - 1, Math.Ceiling((end - tolerance - aligned[0]) / period) - 1);
                        emptySlots += Math.Max(0, lastSlot - firstSlot + 1);
                    }
                    double occupancy = aligned.Length / Math.Max(aligned.Length, slots - emptySlots);
                    // Explaining existing attacks takes priority over a perfectly full
                    // coarser grid that simply discards intervening manual notes.
                    // Continuity and occupancy are supporting evidence, not vetoes.
                    double score = aligned.Length + 0.25 * directSteps + 0.5 * occupancy;
                    if (score > bestScore || (score == bestScore && best is Pulse previous && period < previous.Period))
                    {
                        best = new Pulse(period, phase, aligned.Length);
                        bestScore = score;
                    }
                }
            }
            return best;
        }

        private static double distanceToPulse(double time, double period, double phase)
            => Math.Abs(time - phase - Math.Round((time - phase) / period) * period);

        private static bool nodeAccent(HitObject original, SliderEventDescriptor checkpoint)
        {
            if (checkpoint.Type == SliderEventType.Tick || original is not IHasRepeats repeated)
                return false;
            int node = checkpoint.SpanIndex + 1;
            return node >= 0 && node < repeated.NodeSamples.Count && repeated.NodeSamples[node]
                .Any(sample => sample.Name is HitSampleInfo.HIT_CLAP or HitSampleInfo.HIT_FINISH);
        }

        private sealed record Selection(int[] Indices, double Score);
    }
}
