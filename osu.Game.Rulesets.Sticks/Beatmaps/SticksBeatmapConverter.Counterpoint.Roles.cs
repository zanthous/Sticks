#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    public partial class SticksBeatmapConverter
    {
        private sealed partial class CounterpointPlanner
        {
            private void addRhythmicVoices(int index, double beat, List<Candidate> candidates)
            {
                HitObject first = source[index];
                // A phrase may start between timing-grid beats. Its own recurring
                // attacks establish the pulse; NewCombo only marks a possible entrance.
                if (intensity <= 0 || converter.rapidJumpHeads.Contains(first)
                    || (first is not IHasCombo { NewCombo: true }
                        && index > 0 && first.StartTime - source[index - 1].GetEndTime() < beat))
                    return;

                double tolerance = Math.Max(2, beat * 0.015);
                double recovery = recoveryAt(beat);
                var rhythms = new List<(double Phase, double Period)>();
                // Derive candidate periods from actual nearby attacks. No rounding or
                // retiming is applied to a note. A pickup can precede the steady part:
                // try the first few attacks as its phase, retaining earlier heads as fills.
                for (int phaseIndex = index; phaseIndex < Math.Min(source.Length, index + 3); phaseIndex++)
                {
                    double phase = source[phaseIndex].StartTime;
                    if (phase - first.StartTime > beat + tolerance)
                        break;
                    foreach (HitObject next in source.Skip(phaseIndex + 1).Take(6))
                    {
                        double period = next.StartTime - phase;
                        if (period < recovery || period > beat * 2 + tolerance || phase - first.StartTime >= period
                            || rhythms.Any(previous => Math.Abs(previous.Phase - phase) <= tolerance && Math.Abs(previous.Period - period) <= tolerance))
                            continue;
                        rhythms.Add((phase, period));
                    }
                }

                foreach ((double phaseStart, double period) in rhythms)
                {
                    var phrase = new List<SticksHitObject>();
                    var pulse = new List<bool>();
                    double phraseEnd = phaseStart + period * 4;
                    double timing = beatmap.ControlPointInfo.TimingPointAt(first.StartTime).Time;
                    bool interrupted = false;
                    for (int i = index; i < source.Length && source[i].StartTime < phraseEnd - tolerance; i++)
                    {
                        HitObject original = source[i];
                        // A new combo can close an already complete four-pulse phrase;
                        // the validation below still rejects an incomplete fragment.
                        if (i > index && original is IHasCombo { NewCombo: true })
                            break;
                        if (phrase.Count == 16 || converter.rapidJumpHeads.Contains(original)
                            || simultaneousSourceHeads.Contains(original.StartTime)
                            || !heads.TryGetValue(original.StartTime, out SticksHitObject[]? group)
                            || group.Length != 1 || group[0] is SticksClick
                            || Math.Abs(group[0].GetEndTime() - original.GetEndTime()) > tolerance
                            || beatmap.ControlPointInfo.TimingPointAt(original.StartTime).Time != timing
                            || beatmap.ControlPointInfo.TimingPointAt(original.GetEndTime()).Time != timing
                            || (i > index && original.StartTime - source[i - 1].StartTime > period + tolerance))
                        {
                            interrupted = true;
                            break;
                        }
                        phrase.Add(group[0]);
                        double phase = (original.StartTime - phaseStart) / period;
                        pulse.Add(original.StartTime >= phaseStart - tolerance && Math.Abs(phase - Math.Round(phase)) <= tolerance / period);
                    }
                    if (interrupted || phrase.Count < 6 || pulse.Count(value => value) != 4)
                        continue;
                    int[] pulseSlots = phrase.Where((_, i) => pulse[i])
                        .Select(note => (int)Math.Round((note.StartTime - phaseStart) / period)).ToArray();
                    if (!pulseSlots.SequenceEqual(new[] { 0, 1, 2, 3 }))
                        continue;
                    int fillCycles = phrase.Where((_, i) => !pulse[i])
                        .Select(note => (int)Math.Floor((note.StartTime - phaseStart) / period)).Distinct().Count();
                    double[] gaps = phrase.Zip(phrase.Skip(1), (a, b) => b.StartTime - a.StartTime).ToArray();
                    // Uniform alternation already expresses its source rhythm. A useful
                    // division requires recurring work and a genuinely different fill rhythm.
                    if (fillCycles < 2 || gaps.Max() - gaps.Min() <= tolerance)
                        continue;

                    foreach (StickSide lead in new[] { StickSide.Left, StickSide.Right })
                    {
                        HandAssignment[] assignments = phrase.Select((note, i) => new HandAssignment(note, pulse[i] ? lead : other(lead))).ToArray();
                        // Swapping every colour is not a new coordination pattern.
                        if (assignments.All(a => a.Note.Side == a.Side) || assignments.All(a => a.Note.Side != a.Side))
                            continue;
                        candidates.Add(new Candidate(Family.RhythmicVoices, index, Array.Empty<SticksHitObject>(), assignments,
                            lead, Array.Empty<double>(), 10 + Math.Min(3, fillCycles) + structureAt(index, beat)));
                    }
                }
            }

            private void addStaggeredPhrase(int index, SticksSlider primary, SticksSlider echo, double evidence, List<Candidate> candidates, bool recallsContour = false)
            {
                double beat = beatAt(primary.StartTime);
                if (index + 1 >= source.Length || source[index + 1].StartTime - primary.EndTime > beat + 2
                    || source[index + 1].StartTime < primary.EndTime)
                    return;

                foreach (StickSide answerSide in new[] { primary.Side, other(primary.Side) })
                {
                    var response = new List<HandAssignment>();
                    for (int i = index + 1; i < Math.Min(source.Length, index + 5); i++)
                    {
                        HitObject original = source[i];
                        if (converter.rapidJumpHeads.Contains(original) || simultaneousSourceHeads.Contains(original.StartTime)
                            || !heads.TryGetValue(original.StartTime, out SticksHitObject[]? group) || group.Length is < 1 or > 2
                            || group.Any(note => note is SticksClick) || group.Select(note => note.Side).Distinct().Count() != group.Length
                            || beatmap.ControlPointInfo.TimingPointAt(original.GetEndTime()).Time
                               != beatmap.ControlPointInfo.TimingPointAt(primary.StartTime).Time
                            || (i > index + 1 && (original is IHasCombo { NewCombo: true }
                                || original.StartTime - source[i - 1].GetEndTime() > beat + 2)))
                            break;
                        SticksHitObject lead = group.OrderBy(note => Math.Abs(SticksHitObject.DeltaAngle(note.Angle, converter.plans[original].Angle)))
                            .ThenBy(note => note.Side == converter.plans[original].Side ? 0 : 1).First();
                        if (Math.Abs(lead.GetEndTime() - original.GetEndTime()) > 2)
                            break;
                        response.Add(new HandAssignment(lead, answerSide));
                        foreach (SticksHitObject partner in group.Where(note => note != lead))
                            response.Add(new HandAssignment(partner, other(answerSide)));
                    }
                    int attacks = response.Select(assignment => assignment.Note.StartTime).Distinct().Count();
                    bool singlePhraseAnswer = attacks == 1 && (index + 2 >= source.Length
                        || source[index + 2] is IHasCombo { NewCombo: true }
                        || source[index + 2].StartTime - source[index + 1].GetEndTime() > beat + 2);
                    if (attacks == 0 || (attacks == 1 && !singlePhraseAnswer)
                        || (!recallsContour && response.All(assignment => assignment.Note.Side == assignment.Side)))
                        continue;
                    // Either released hand may take the answer; checking both avoids
                    // forcing a flick onto a hand still recovering from its slider.
                    // Existing doubles retain both heads and opposite hand ownership.
                    candidates.Add(new Candidate(Family.StaggeredPhrase, index, new SticksHitObject[] { echo },
                        response.ToArray(), answerSide, Array.Empty<double>(), evidence + 2));
                }
            }

            private SticksSlider? neighbouringContour(int index, SticksSlider echo)
            {
                // An added part can recall a nearby source slider's complete contour,
                // rather than always following a parallel/mirrored slice of the lead.
                // Only its duration is fitted to the proposed entrance/release interval.
                foreach (int offset in new[] { -1, 1, -2, 2, -3, 3 })
                {
                    int neighbour = index + offset;
                    if ((uint)neighbour >= (uint)source.Length || source[neighbour] is not IHasDuration
                        || Math.Max(source[neighbour].StartTime - source[index].GetEndTime(),
                            source[index].StartTime - source[neighbour].GetEndTime()) > beatAt(echo.StartTime) * 8
                        || beatmap.ControlPointInfo.TimingPointAt(source[neighbour].StartTime).Time
                           != beatmap.ControlPointInfo.TimingPointAt(source[index].StartTime).Time
                        || !heads.TryGetValue(source[neighbour].StartTime, out SticksHitObject[]? group)
                        || group.OfType<SticksSlider>().FirstOrDefault(slider => !slider.IsStationary) is not SticksSlider template
                        || template.Duration <= 0 || Math.Abs(template.EndTime - source[neighbour].GetEndTime()) > 2
                        || beatmap.ControlPointInfo.TimingPointAt(template.EndTime).Time
                           != beatmap.ControlPointInfo.TimingPointAt(source[index].StartTime).Time)
                        continue;
                    bool crossesRest = false;
                    for (int i = Math.Min(index, neighbour) + 1; i <= Math.Max(index, neighbour); i++)
                        crossesRest |= source[i].StartTime - source[i - 1].GetEndTime() > beatAt(source[i].StartTime) * 2 + 2;
                    if (crossesRest)
                        continue;
                    float[] arcs = Enumerable.Range(0, template.SegmentCount).Select(template.SegmentArcAngleAt).ToArray();
                    double[] lengths = Enumerable.Range(0, template.SegmentCount)
                        .Select(segment => template.SegmentDurationAt(segment) / template.Duration * echo.Duration).ToArray();
                    if (arcs.Where((arc, i) => lengths[i] <= 0 || Math.Abs(arc) * 1000 / lengths[i] > MAX_GENERATED_SLIDER_ANGULAR_VELOCITY).Any())
                        continue;
                    if (arcs.Length == echo.SegmentCount && Enumerable.Range(0, arcs.Length).All(i =>
                            Math.Abs(arcs[i] - echo.SegmentArcAngleAt(i)) <= 0.001
                            && Math.Abs(lengths[i] - echo.SegmentDurationAt(i)) <= 0.01))
                        continue;
                    float position = 0, minimum = 0, maximum = 0;
                    foreach (float arc in arcs)
                    {
                        position += arc;
                        minimum = Math.Min(minimum, position);
                        maximum = Math.Max(maximum, position);
                    }
                    if (maximum - minimum <= SticksHitObject.HitAngleForCircleSize(beatmap.Difficulty.CircleSize))
                        continue;
                    var answer = new SticksSlider
                    {
                        StartTime = echo.StartTime, Duration = echo.Duration, Side = echo.Side,
                        Angle = echo.Angle, Samples = cloneSamples(echo.Samples),
                    };
                    answer.SetTimedSegments(arcs, lengths);
                    if (!SticksCounterpointMotion.HasReadableReversals(answer))
                        continue;
                    // Recalled shape is visual/motor phrasing, not additional authored
                    // impacts. Do not transplant the other slider's node accents.
                    for (int i = 0; i <= arcs.Length; i++)
                        answer.NodeSamples.Add(i == 0 ? cloneSamples(echo.Samples)
                            : i == arcs.Length && echo.NodeSamples.Count > 0 ? cloneSamples(echo.NodeSamples[^1])
                            : samplesAt(source[index], -1));
                    return answer;
                }
                return null;
            }
        }
    }
}
