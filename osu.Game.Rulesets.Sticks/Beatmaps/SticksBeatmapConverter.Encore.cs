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
            AddEncoreSlices(result, source, cancellationToken);
            converted.HitObjects.Clear();
            converted.HitObjects.AddRange(result.OrderBy(hitObject => hitObject.StartTime).ThenBy(hitObject => hitObject.Side));
        }

        internal static void AddEncoreSlices(List<SticksHitObject> notes, IBeatmap source, CancellationToken cancellationToken)
        {
            var ordered = notes.Select((note, index) => (Note: note, Index: index)).OrderBy(entry => entry.Note.StartTime).ToArray();
            // Future objects have not been reassigned yet, so these remain valid during
            // the forward pass. They let us check a run's exit without scanning the map.
            var nextByHand = new int[2, ordered.Length + 1];
            nextByHand[0, ordered.Length] = nextByHand[1, ordered.Length] = ordered.Length;
            for (int i = ordered.Length - 1; i >= 0; i--)
            {
                nextByHand[0, i] = nextByHand[0, i + 1];
                nextByHand[1, i] = nextByHand[1, i + 1];
                nextByHand[ordered[i].Note.Side == StickSide.Left ? 0 : 1, i] = i;
            }
            // New combos are explicit source phrase boundaries, including when the opening
            // note uses the other hand. Breaks also interrupt any preceding gesture.
            double[] phraseStarts = source.HitObjects.Where(note => note is IHasCombo { NewCombo: true })
                .Select(note => note.StartTime).Concat(source.Breaks.Select(period => period.EndTime))
                .Distinct().OrderBy(time => time).ToArray();
            int phrase = 0;
            var previous = new SticksHitObject?[2];
            double lastSlice = double.NegativeInfinity;
            for (int i = 0; i < ordered.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SticksHitObject note = ordered[i].Note;
                while (phrase < phraseStarts.Length && phraseStarts[phrase] <= note.StartTime)
                {
                    Array.Clear(previous);
                    phrase++;
                }
                int hand = note.Side == StickSide.Left ? 0 : 1;
                int count = sliceRunLength(i, hand);

                // Normal conversion alternates rapid flicks. For a Slice, keeping the
                // preceding stick out is the point: let a short connected run use it.
                // Prefer existing same-hand follow-ups and only borrow the other hand
                // from the immediately preceding aimed object in this phrase.
                if (count == 0 && i > 0 && previous[1 - hand] == ordered[i - 1].Note)
                {
                    int alternateCount = sliceRunLength(i, 1 - hand);
                    if (alternateCount > 0)
                    {
                        hand = 1 - hand;
                        count = alternateCount;
                    }
                }

                if (count == 0)
                {
                    previous[hand] = note;
                    continue;
                }

                for (int end = i + count; i < end; i++)
                {
                    var entry = ordered[i];
                    var slice = new SticksSlice
                    {
                        StartTime = entry.Note.StartTime,
                        Side = hand == 0 ? StickSide.Left : StickSide.Right,
                        Angle = entry.Note.Angle,
                        Samples = entry.Note.Samples,
                    };
                    notes[entry.Index] = slice;
                    ordered[i] = (slice, entry.Index);
                    previous[hand] = slice;
                    lastSlice = slice.StartTime;
                }
                i--;
            }

            int sliceRunLength(int start, int hand)
            {
                SticksHitObject? before = previous[hand];
                // Space whole runs across both hands, rather than alternating runs
                // so frequently that nearly every ordinary flick becomes a Slice.
                if (before == null || before is SticksClick || ordered[start].Note.StartTime - lastSlice < 500)
                    return 0;

                int count = 0;
                for (int index = start; index < Math.Min(start + 2, ordered.Length); index++)
                {
                    SticksHitObject candidate = ordered[index].Note;
                    if (candidate is not SticksFlick || (phrase < phraseStarts.Length && phraseStarts[phrase] <= candidate.StartTime))
                        break;

                    double gap = candidate.StartTime - before.GetEndTime();
                    double beatLength = validBeatLength(source.ControlPointInfo.TimingPointAt(candidate.StartTime).BeatLength);
                    float angle = before is SticksSlider slider ? slider.AngleAt(slider.EndTime) : before.Angle;
                    double travel = Math.Abs(SticksHitObject.DeltaAngle(angle, candidate.Angle));
                    bool chord = index > 0 && candidate.StartTime - ordered[index - 1].Note.StartTime < 1
                                 || index + 1 < ordered.Length && ordered[index + 1].Note.StartTime - candidate.StartTime < 1;

                    // Keep the fast-follow-up requirement, jumps and simultaneous heads.
                    // Measuring from the tail also prevents taking an occupied stick.
                    if (chord || gap <= 0 || gap > Math.Min(150, beatLength / 2) || travel < 10 || travel > 80)
                        break;

                    int next = nextByHand[hand, index + 1];
                    // A reassigned final Slice must not steal recovery from the next
                    // flick/slider head. Ending on this hand's original note preserves
                    // its existing recovery; otherwise require the normal safe gap.
                    if ((candidate.Side == StickSide.Left ? 0 : 1) == hand || next == ordered.Length
                        || ordered[next].Note.StartTime - candidate.StartTime >= RAPID_ALTERNATION_THRESHOLD)
                        count = index - start + 1;

                    before = candidate;
                }
                return count;
            }
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

            // Keep beginner clicks isolated, but reach half-beat spacing on ordinary
            // hard maps. Check note starts and slider releases separately: ending a
            // sustain does not need the same preparation time as another note head.
            double difficulty = source.Difficulty.OverallDifficulty;
            double intensity = Math.Clamp(((double.IsFinite(difficulty) ? difficulty : 5) - 2) / 4, 0, 1);
            double minimumGapMilliseconds = 500 - 350 * intensity;
            // Leave a small musical margin below a half beat at the hard end: source
            // timestamps round to milliseconds, so a true half beat may be 171 or 172 ms.
            double minimumGapBeats = 1 - 0.55 * intensity;
            double minimumTailGapMilliseconds = 200 - 80 * intensity;
            double minimumTailGapBeats = 0.5 - 0.25 * intensity;
            double minimumClickMilliseconds = 8000 - 4000 * intensity;
            double minimumClickBeats = 16 - 8 * intensity;
            var candidates = new List<(SticksFlick Note, double BeatLength, int Accent)>();

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
                double previousHeadGap = index > 0 ? time - groups[index - 1].Key : double.PositiveInfinity;
                double previousTailGap = time - previousSourceEndTimes[index];
                double nextGap = index + 1 < groups.Length ? groups[index + 1].Key - time : double.PositiveInfinity;
                double minimumGap = Math.Max(minimumGapMilliseconds, beatLength * minimumGapBeats);
                double minimumTailGap = Math.Max(minimumTailGapMilliseconds, beatLength * minimumTailGapBeats);

                if (previousHeadGap < minimumGap || previousTailGap < minimumTailGap || nextGap < minimumGap)
                    continue;

                SticksFlick? replacement = directionalObjects.OfType<SticksFlick>()
                                                            .FirstOrDefault(flick => Math.Abs(flick.StartTime - time) < 0.01);

                // Check both sticks, including generated accompaniment. A released
                // slider is allowed nearby; active sustains and doubles remain intact.
                if (replacement == null || directionalObjects.Any(hitObject => hitObject != replacement
                    && (Math.Abs(hitObject.StartTime - time) < minimumGap
                        || hitObject.StartTime < time && time - hitObject.GetEndTime() < minimumTailGap)))
                    continue;

                int accent = (accented ? 2 : 0) + (downbeat ? 2 : 0) + (original is IHasCombo { NewCombo: true } ? 1 : 0);
                candidates.Add((replacement, beatLength, accent));
            }

            for (int index = 0; index < candidates.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = candidates[index];
                if (!hasClickSpacing(candidate))
                    continue;

                // Prefer a nearby musical accent, without requiring special hitsounds
                // or a particular beat phase for a map to get any clicks at all.
                int best = index;
                for (int next = index + 1; next < candidates.Count
                     && candidates[next].Note.StartTime <= candidate.Note.StartTime + candidate.BeatLength; next++)
                {
                    if (candidates[next].Accent > candidates[best].Accent && hasClickSpacing(candidates[next]))
                        best = next;
                }
                index = best;
                SticksFlick replacement = candidates[best].Note;
                double time = replacement.StartTime;

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

            bool hasClickSpacing((SticksFlick Note, double BeatLength, int Accent) candidate) =>
                candidate.Note.StartTime - lastClickTime >= Math.Max(minimumClickMilliseconds, candidate.BeatLength * minimumClickBeats);
        }
    }
}
