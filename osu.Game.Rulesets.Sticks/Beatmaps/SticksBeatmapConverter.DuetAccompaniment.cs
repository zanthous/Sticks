#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    public partial class SticksBeatmapConverter
    {
        private readonly List<DuetAccompanimentFlick> duetAccompaniment = new List<DuetAccompanimentFlick>();

        private void clearDuetAccompaniment() => duetAccompaniment.Clear();

        /// <summary>
        /// Adds a sparse second voice while an ordinary source slider occupies the first stick.
        /// Every accent belongs to a source head, repeat, tick, or a half-beat rhythm established
        /// by nearby source heads. Existing objects and their directions remain authoritative.
        /// </summary>
        private void planDuetAccompaniment(HitObject[] objects, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            DuetReservations reservations = createDuetReservations(objects);
            var addedTimes = new Dictionary<StickSide, SortedSet<double>>
            {
                [StickSide.Left] = new SortedSet<double>(),
                [StickSide.Right] = new SortedSet<double>(),
            };
            double lastPhraseStart = double.NegativeInfinity;

            for (int index = 0; index < objects.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HitObject source = objects[index];
                ConversionPlan plan = plans[source];
                if (!plan.Emit || source is not IHasDuration { Duration: > 0 } duration || isHoldSource(source)
                    || duetDurationPartners.ContainsKey(source) || generatedChordPartners.ContainsKey(source)
                    || generatedSliders.ContainsKey(source) || Math.Abs(plan.ArcAngle) < 1
                    // Bound checkpoint work without changing the source repeat period.
                    || source is IHasRepeats { RepeatCount: >= 64 })
                    continue;

                double beatLength = validBeatLength(beatmap.ControlPointInfo.TimingPointAt(source.StartTime).BeatLength);
                if (duration.Duration < 180 || duration.Duration > beatLength * 8
                    || source.StartTime - lastPhraseStart < beatLength * 4)
                    continue;

                // A BPM change ends this phrase's rhythmic context. The next source slider
                // may establish a fresh one rather than extrapolating the old half-beat grid.
                if (beatmap.ControlPointInfo.TimingPointAt(duration.EndTime).Time
                    != beatmap.ControlPointInfo.TimingPointAt(source.StartTime).Time)
                    continue;

                StickSide accompanimentSide = other(plan.Side);
                DuetAccentCheckpoint[] checkpoints = duetAccentCheckpoints(source, index, objects, beatmap, beatLength, cancellationToken)
                    .OrderBy(checkpoint => checkpoint.Priority)
                    .ThenBy(checkpoint => Math.Abs(checkpoint.Time - (source.StartTime + duration.Duration * 0.5)))
                    .ThenBy(checkpoint => checkpoint.Time)
                    .ToArray();

                int added = 0;
                foreach (DuetAccentCheckpoint checkpoint in checkpoints)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double time = checkpoint.Time;
                    // Excluding this source is safe: it occupies only the opposite stick.
                    // Paired sources were rejected above; all other generated/native duration
                    // partners remain represented in the reservation tree.
                    if (reservations.Conflicts(accompanimentSide, time, time, source, source)
                        || addedTimes[accompanimentSide].GetViewBetween(
                            time - RAPID_ALTERNATION_THRESHOLD, time + RAPID_ALTERNATION_THRESHOLD).Any())
                        continue;

                    float? angle = duetAccompanimentAngle(objects, index, accompanimentSide, time, beatLength);
                    duetAccompaniment.Add(new DuetAccompanimentFlick(source, time, accompanimentSide, angle, checkpoint.NodeIndex));
                    addedTimes[accompanimentSide].Add(time);
                    if (++added == 2)
                        break;
                }

                if (added > 0)
                    lastPhraseStart = source.StartTime;
            }
        }

        private IEnumerable<DuetAccentCheckpoint> duetAccentCheckpoints(HitObject source, int index, HitObject[] objects,
                                                                        IBeatmap beatmap, double beatLength, CancellationToken cancellationToken)
        {
            var duration = (IHasDuration)source;
            int spanCount = source is IHasRepeats repeats ? Math.Max(0, repeats.RepeatCount) + 1 : 1;
            double spanDuration = duration.Duration / spanCount;
            var seen = new HashSet<long>();

            // Repeats are explicit musical events with their own hitsounds. Tick checkpoints
            // follow the same phase resets and tick rate as the converted duration object.
            for (int span = 0; span < spanCount; span++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double start = source.StartTime + span * spanDuration;
                if (span > 0 && seen.Add((long)Math.Round(start * 1000)))
                    yield return new DuetAccentCheckpoint(start, span, 0);

                foreach (double tick in SticksTickGenerator.Generate(beatmap.ControlPointInfo, start, start + spanDuration,
                             Math.Max(1, beatmap.Difficulty.SliderTickRate), cancellationToken))
                {
                    if (seen.Add((long)Math.Round(tick * 1000)))
                        yield return new DuetAccentCheckpoint(tick, -1, 1);
                }
            }

            // A typical one-beat slider has no interior whole-beat tick. Two nearby source
            // half-beat intervals establish a musical subdivision for its interior accent;
            // timing points alone are deliberately insufficient evidence for adding that pulse.
            if (duetHasLocalHalfBeatRhythm(objects, index, beatmap, beatLength))
            {
                for (double time = source.StartTime + beatLength * 0.5; time < duration.EndTime - 10; time += beatLength * 0.5)
                {
                    if (seen.Add((long)Math.Round(time * 1000)))
                        yield return new DuetAccentCheckpoint(time, -1, 2);
                }
            }

            // A source-head chord is the fallback when interior checkpoints cannot fit.
            // This also preserves meaningful accented short sliders without making up a pulse.
            yield return new DuetAccentCheckpoint(source.StartTime, 0, 3);
        }

        private static bool duetHasLocalHalfBeatRhythm(HitObject[] objects, int index, IBeatmap beatmap, double beatLength)
        {
            double sourceTime = objects[index].StartTime;
            double timingStart = beatmap.ControlPointInfo.TimingPointAt(sourceTime).Time;
            int matchingIntervals = 0;
            for (int i = Math.Max(1, index - 6); i <= Math.Min(objects.Length - 1, index + 6); i++)
            {
                HitObject first = objects[i - 1];
                HitObject second = objects[i];
                if (Math.Abs(second.StartTime - sourceTime) > beatLength * 4
                    || beatmap.ControlPointInfo.TimingPointAt(first.StartTime).Time != timingStart
                    || beatmap.ControlPointInfo.TimingPointAt(second.StartTime).Time != timingStart)
                    continue;

                double interval = second.StartTime - first.StartTime;
                if (Math.Abs(interval - beatLength * 0.5) <= Math.Max(2, beatLength * 0.03) && ++matchingIntervals >= 2)
                    return true;
            }

            return false;
        }

        private float? duetAccompanimentAngle(HitObject[] objects, int index, StickSide side, double time, double beatLength)
        {
            HitObject? before = null;
            HitObject? after = null;
            HitObject? nearest = null;
            double nearestDistance = double.PositiveInfinity;
            for (int i = Math.Max(0, index - 8); i <= Math.Min(objects.Length - 1, index + 8); i++)
            {
                HitObject source = objects[i];
                ConversionPlan plan = plans[source];
                double distance = Math.Abs(source.StartTime - time);
                if (i == index || !plan.Emit || distance > beatLength * 4)
                    continue;
                if (distance < nearestDistance)
                {
                    nearest = source;
                    nearestDistance = distance;
                }
                if (plan.Side != side)
                    continue;
                if (source.StartTime <= time && (before == null || source.StartTime > before.StartTime))
                    before = source;
                if (source.StartTime >= time && (after == null || source.StartTime < after.StartTime))
                    after = source;
            }

            if (before != null && after != null && after.StartTime > before.StartTime)
            {
                double progress = (time - before.StartTime) / (after.StartTime - before.StartTime);
                float from = plans[before].Angle;
                return SticksHitObject.NormaliseAngle(from + (float)progress * SticksHitObject.DeltaAngle(from, plans[after].Angle));
            }

            HitObject? anchor = before ?? after ?? nearest;
            return anchor == null ? null : plans[anchor].Angle;
        }

        private void addDuetAccompaniment(Beatmap<SticksHitObject> converted)
        {
            foreach (DuetAccompanimentFlick accent in duetAccompaniment)
            {
                // Conversion can collapse pathological native sliders to flicks. Only emit an
                // accompaniment if its intended primary really survived as a duration object.
                SticksSlider? primary = converted.HitObjects.OfType<SticksSlider>().FirstOrDefault(slider =>
                        slider.StartTime == accent.Source.StartTime && slider.Side != accent.Side
                        && accent.Time < slider.EndTime);
                if (primary == null)
                    continue;

                IList<HitSampleInfo> samples = conversionSamples(accent.Source);
                if (!DisableBeatmapHitsounds && accent.Source is IHasRepeats repeats
                    && accent.NodeIndex >= 0 && accent.NodeIndex < repeats.NodeSamples.Count
                    && repeats.NodeSamples[accent.NodeIndex].Count > 0)
                    samples = cloneSamples(repeats.NodeSamples[accent.NodeIndex]);

                converted.HitObjects.Add(new SticksFlick
                {
                    StartTime = accent.Time,
                    Side = accent.Side,
                    Angle = accent.Angle ?? SticksHitObject.NormaliseAngle(primary.AngleAt(accent.Time)),
                    Samples = samples,
                });
            }

            converted.HitObjects = converted.HitObjects.OrderBy(note => note.StartTime).ToList();
        }

        private readonly record struct DuetAccentCheckpoint(double Time, int NodeIndex, int Priority);
        private sealed record DuetAccompanimentFlick(HitObject Source, double Time, StickSide Side, float? Angle, int NodeIndex);
    }
}
