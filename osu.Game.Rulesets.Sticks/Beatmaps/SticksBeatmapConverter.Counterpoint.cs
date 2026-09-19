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
        // Optional observer for corpus diagnostics; normal conversion allocates no report.
        internal Action<string, double, double, int, int>? CounterpointArrangementObserved { get; set; }

        private void applyCounterpoint(Beatmap<SticksHitObject> converted, IBeatmap source, CancellationToken cancellationToken)
            => new CounterpointPlanner(this, converted, source, cancellationToken).Apply();

        /// <summary>
        /// Builds the default two-stick arrangement by assigning hand roles and adding source-supported responses.
        /// It never consumes an existing head or changes an existing slider's trajectory.
        /// </summary>
        private sealed partial class CounterpointPlanner
        {
            private const double epsilon = 0.01;
            private readonly SticksBeatmapConverter converter;
            private readonly Beatmap<SticksHitObject> converted;
            private readonly IBeatmap beatmap;
            private readonly CancellationToken cancellationToken;
            private readonly HitObject[] source;
            private readonly SticksHitObject[] baseline;
            private readonly HashSet<double> simultaneousSourceHeads;
            private readonly Dictionary<double, SticksHitObject[]> heads;
            private readonly Lazy<SticksCounterpointMotifs> motifs;
            private Lane left = null!;
            private Lane right = null!;
            private readonly Dictionary<string, Recipe> rememberedRecipes = new Dictionary<string, Recipe>();
            private readonly double intensity;
            private double lastPhraseEnd = double.NegativeInfinity;
            private double lastDoubleTime = double.NegativeInfinity;
            private Family? lastFamily;
            private StickSide lastLead = StickSide.Right;

            public CounterpointPlanner(SticksBeatmapConverter converter, Beatmap<SticksHitObject> converted,
                                       IBeatmap beatmap, CancellationToken cancellationToken)
            {
                this.converter = converter;
                this.converted = converted;
                this.beatmap = beatmap;
                this.cancellationToken = cancellationToken;
                source = beatmap.HitObjects.OrderBy(note => note.StartTime).ToArray();
                simultaneousSourceHeads = source.GroupBy(note => note.StartTime).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
                baseline = converted.HitObjects.OrderBy(note => note.StartTime).ToArray();
                heads = baseline.GroupBy(note => note.StartTime).ToDictionary(group => group.Key, group => group.ToArray());
                motifs = new Lazy<SticksCounterpointMotifs>(() => new SticksCounterpointMotifs(source, beatmap, cancellationToken));
                rebuildLanes();
                double od = double.IsFinite(beatmap.Difficulty.OverallDifficulty) ? beatmap.Difficulty.OverallDifficulty : 5;
                intensity = Math.Clamp((od - 4) / 4, 0, 1);
            }

            public void Apply()
            {
                var candidates = new List<Candidate>();
                for (int i = 0; i < source.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double beat = beatAt(source[i].StartTime);
                    addDurationCandidates(i, beat, candidates);
                    addLeadCandidates(i, beat, candidates);
                    addRhythmicVoices(i, beat, candidates);
                    addPunctuation(i, beat, candidates);
                }

                // Compare alternatives within a musical window before committing: the
                // first available tick should not pre-empt a later reversal or arrival.
                foreach (var window in candidates.GroupBy(candidate => windowAt(candidate.Start)).OrderBy(group => group.Key))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    while (true)
                    {
                        Candidate? selected = window.Where(eligible)
                            .OrderByDescending(score)
                            .ThenBy(candidate => candidate.Start)
                            .ThenBy(candidate => candidate.Family)
                            .FirstOrDefault();
                        if (selected == null)
                            break;

                        converter.CounterpointArrangementObserved?.Invoke(selected.Family.ToString(), selected.Start, selected.End,
                            selected.Assignments.Count(assignment => assignment.Note.Side != assignment.Side), selected.Additions.Length);
                        foreach (HandAssignment assignment in selected.Assignments)
                            assignment.Note.Side = assignment.Side;
                        converted.HitObjects.AddRange(selected.Additions);
                        rebuildLanes();
                        lastPhraseEnd = selected.End;
                        lastFamily = selected.Family;
                        if (selected.DoubleTimes.Length > 0)
                            lastDoubleTime = selected.DoubleTimes[^1];
                        if (selected.Assignments.Length > 0)
                            lastLead = selected.LeadSide;
                        string? key = motifs.Value.KeyAt(selected.SourceIndex);
                        if (key != null)
                            rememberedRecipes.TryAdd(key, recipeFor(selected));
                    }
                }

                converted.HitObjects = converted.HitObjects.OrderBy(note => note.StartTime).ToList();
            }

            private void addDurationCandidates(int index, double beat, List<Candidate> candidates)
            {
                HitObject original = source[index];
                if (original is not IHasDuration { Duration: > 0 } duration
                    || !heads.TryGetValue(original.StartTime, out SticksHitObject[]? group)
                    || group.OfType<SticksSlider>().FirstOrDefault(slider => !slider.IsStationary) is not SticksSlider primary
                    || duration.Duration > beat * 12
                    || beatmap.ControlPointInfo.TimingPointAt(duration.EndTime).Time
                       != beatmap.ControlPointInfo.TimingPointAt(original.StartTime).Time)
                    return;

                SliderEventDescriptor[] events = SticksCounterpointCheckpoints.Generate(original, beatmap, cancellationToken)
                    .OrderBy(checkpoint => checkpoint.Time).ToArray();
                StickSide side = other(primary.Side);
                double recovery = recoveryAt(beat);
                SliderEventDescriptor[] available = events.Where(checkpoint => !hasHeadAt(checkpoint.Time)
                    && free(side, checkpoint.Time, checkpoint.Time, recovery)
                    && (intensity > 0 || checkpoint.Type == SliderEventType.Tail)).ToArray();

                // Source repeats/ticks/tails are possible times, not automatic accents.
                // Choose a complete response using the surrounding manual rhythm or a
                // node-specific accent, rather than consuming the first available event.
                SticksCounterpointRhythm.SupportedCheckpoint[] supported = SticksCounterpointRhythm.Evaluate(source, index, beatmap, available, cancellationToken);
                foreach (SticksCounterpointRhythm.Response response in SticksCounterpointRhythm.Select(
                             supported, recovery, intensity > 0 ? 3 : 1, cancellationToken))
                {
                    SticksHitObject[] answer = response.Checkpoints.Select((checkpoint, offset) => (SticksHitObject)new SticksFlick
                    {
                        StartTime = checkpoint.Time,
                        Side = side,
                        Angle = responseAngle(index, side, checkpoint.Time, offset, primary),
                        Samples = samplesAt(original, checkpoint.Type == SliderEventType.Tick ? -1 : checkpoint.SpanIndex + 1),
                    }).ToArray();
                    candidates.Add(new Candidate(Family.RhythmAnswer, index, answer,
                        Array.Empty<HandAssignment>(), side, Array.Empty<double>(), response.Evidence));
                }

                if (intensity <= 0)
                    return;

                // Stagger the second trajectory between source checkpoints. Keep every
                // turn and segment time from that portion of the lead, at its own angle.
                foreach (SliderEventDescriptor start in events.Where(checkpoint => checkpoint.Time < duration.EndTime && !hasHeadAt(checkpoint.Time)))
                {
                    foreach (SliderEventDescriptor end in events.Where(checkpoint => checkpoint.Time > start.Time).Reverse().Take(8))
                    {
                        if (end.Time - start.Time < Math.Max(350, beat * 0.75)
                            || !free(side, start.Time, end.Time, recovery))
                            continue;
                        SticksSlider? echo = slice(primary, start.Time, end.Time, side,
                            responseAngle(index, side, start.Time, 0, primary), original, start.Type == SliderEventType.Tick ? -1 : start.SpanIndex + 1, responseDirection(index, primary, start.Time));
                        if (echo == null)
                            continue;
                        double evidence = 8 + (start.Type == SliderEventType.Repeat ? 1 : 0)
                                          + (supported.Any(point => Math.Abs(point.Checkpoint.Time - start.Time) <= epsilon) ? 2 : 0);
                        candidates.Add(new Candidate(Family.StaggeredVoice, index, new SticksHitObject[] { echo },
                            Array.Empty<HandAssignment>(), side, Array.Empty<double>(), evidence));
                        addStaggeredPhrase(index, primary, echo, evidence, candidates);
                        SticksSlider? recalled = neighbouringContour(index, echo);
                        if (recalled != null)
                            addStaggeredPhrase(index, primary, recalled, evidence + 1, candidates, recallsContour: true);
                    }
                }
            }

            private void addLeadCandidates(int index, double beat, List<Candidate> candidates)
            {
                if (intensity <= 0 || converter.rapidJumpHeads.Contains(source[index])
                    || (!onBeat(source[index].StartTime, beat) && source[index] is not IHasCombo { NewCombo: true }))
                    return;

                var phrase = new List<SticksFlick>();
                double recovery = recoveryAt(beat);
                double timing = beatmap.ControlPointInfo.TimingPointAt(source[index].StartTime).Time;
                for (int i = index; i < Math.Min(source.Length, index + 8); i++)
                {
                    HitObject note = source[i];
                    if (note is IHasDuration || simultaneousSourceHeads.Contains(note.StartTime) || converter.rapidJumpHeads.Contains(note) || !heads.TryGetValue(note.StartTime, out SticksHitObject[]? group)
                        || group.Length is < 1 or > 2 || group.Any(head => head is not SticksFlick)
                        || beatmap.ControlPointInfo.TimingPointAt(note.StartTime).Time != timing
                        || (i > index && note is IHasCombo { NewCombo: true }))
                        break;
                    if (i > index && (note.StartTime - source[i - 1].StartTime < recovery
                                      || note.StartTime - source[i - 1].StartTime > beat * 1.1))
                        break;
                    SticksFlick flick = group.Cast<SticksFlick>()
                        .OrderBy(head => Math.Abs(SticksHitObject.DeltaAngle(converter.plans[note].Angle, head.Angle)))
                        .ThenBy(head => head.Side == converter.plans[note].Side ? 0 : 1).First();
                    phrase.Add(flick);
                }
                if (phrase.Count < (source[index] is IHasCombo { NewCombo: true } ? 3 : 4)
                    || (structureAt(index, beat) == 0 && motifs.Value.OccurrencesAt(index) < 2))
                    return;

                // Use a source accent or a beat inside the phrase, keeping the pickup intact.
                int accent = Enumerable.Range(1, phrase.Count - 1)
                    .Where(offset => onBeat(phrase[offset].StartTime, beat))
                    .OrderByDescending(offset => structureAt(index + offset, beat))
                    .ThenBy(offset => Math.Abs(offset - phrase.Count / 2))
                    .DefaultIfEmpty(-1).First();
                bool existingAccent = phrase.Any(note => heads[note.StartTime].Length == 2);
                foreach (StickSide lead in new[] { StickSide.Left, StickSide.Right })
                {
                    if (existingAccent)
                    {
                        // Reorganise the phrase around its existing punctuation. A base
                        // double is a useful musical anchor, not an obstacle to variety.
                        candidates.Add(new Candidate(Family.PhraseLead, index, Array.Empty<SticksHitObject>(),
                            phrase.Select(note => new HandAssignment(note, lead)).ToArray(), lead, Array.Empty<double>(),
                            9 + Math.Min(3, motifs.Value.OccurrencesAt(index)) + structureAt(index, beat)));
                    }
                    else if (accent >= 0)
                    {
                        var punctuation = new SticksFlick
                        {
                            StartTime = phrase[accent].StartTime,
                            Side = other(lead),
                            Angle = phrase[0].Angle,
                            Samples = samplesAt(source[index + accent], 0),
                        };
                        alignAddedHead(punctuation, phrase[accent]);
                        candidates.Add(new Candidate(Family.PhraseLead, index, new SticksHitObject[] { punctuation },
                            phrase.Select(note => new HandAssignment(note, lead)).ToArray(), lead, new[] { punctuation.StartTime },
                            7 + Math.Min(3, motifs.Value.OccurrencesAt(index)) + structureAt(index, beat)));
                    }

                    // A continuous supporting voice may accompany articulated source
                    // circles. Every original circle is still a manual lead-hand flick.
                    var arcs = new List<float>();
                    var durations = new List<double>();
                    float position = 0;
                    float minimum = 0;
                    float maximum = 0;
                    bool playable = true;
                    for (int i = 1; i < phrase.Count; i++)
                    {
                        float arc = SticksHitObject.DeltaAngle(phrase[i - 1].Angle, phrase[i].Angle);
                        double length = phrase[i].StartTime - phrase[i - 1].StartTime;
                        playable &= Math.Abs(arc) / length * 1000 <= MAX_GENERATED_SLIDER_ANGULAR_VELOCITY;
                        arcs.Add(arc);
                        durations.Add(length);
                        position += arc;
                        minimum = Math.Min(minimum, position);
                        maximum = Math.Max(maximum, position);
                    }
                    if (playable && maximum - minimum > SticksHitObject.HitAngleForCircleSize(beatmap.Difficulty.CircleSize))
                    {
                        var support = new SticksSlider
                        {
                            StartTime = phrase[0].StartTime,
                            Duration = phrase[^1].StartTime - phrase[0].StartTime,
                            Angle = phrase[0].Angle,
                            Side = other(lead),
                            Samples = samplesAt(source[index], 0),
                        };
                        support.SetTimedSegments(arcs, durations);
                        if (!SticksCounterpointMotion.HasReadableReversals(support))
                            continue;
                        for (int i = 0; i < phrase.Count; i++)
                            support.NodeSamples.Add(samplesAt(source[index + i], 0));
                        candidates.Add(new Candidate(Family.PhraseSupport, index, new SticksHitObject[] { support },
                            phrase.Select(note => new HandAssignment(note, lead)).ToArray(), lead, new[] { support.StartTime },
                            9 + Math.Min(3, motifs.Value.OccurrencesAt(index)) + structureAt(index, beat)));
                    }
                }
            }

            private void addPunctuation(int index, double beat, List<Candidate> candidates)
            {
                if (structureAt(index, beat) < 2
                    || !heads.TryGetValue(source[index].StartTime, out SticksHitObject[]? group)
                    || group.Length != 1 || group[0] is not SticksFlick primary)
                    return;
                var partner = new SticksFlick
                {
                    StartTime = primary.StartTime,
                    Side = other(primary.Side),
                    Angle = primary.Angle,
                    Samples = samplesAt(source[index], 0),
                };
                candidates.Add(new Candidate(Family.Punctuation, index, new SticksHitObject[] { partner },
                    Array.Empty<HandAssignment>(), primary.Side, new[] { partner.StartTime },
                    5 + structureAt(index, beat)));
            }

            private bool eligible(Candidate candidate)
            {
                if (candidate.Additions.Length == 0 && candidate.Assignments.All(assignment => assignment.Note.Side == assignment.Side))
                    return false;
                if (candidate.Family == Family.RhythmicVoices
                    && candidate.Assignments.All(assignment => assignment.Note.Side != assignment.Side))
                    return false;
                if (introducesCoordination(candidate) && !converter.canIntroduceCoordination(candidate.Start))
                    return false;
                double beat = beatAt(candidate.Start);
                double cooldown = candidate.Additions.Length == 0 ? recoveryAt(beat) : Math.Max(2500, beat * (8 - 4 * intensity));
                if (candidate.Start - lastPhraseEnd < cooldown)
                    return false;
                if (candidate.DoubleTimes.Length > 0
                    && candidate.DoubleTimes[0] - lastDoubleTime < Math.Max(8000, beat * (32 - 8 * intensity)))
                    return false;
                double recovery = recoveryAt(beat);
                var excluded = candidate.Assignments.Select(assignment => assignment.Note).ToHashSet();
                foreach (double time in candidate.DoubleTimes)
                {
                    // A new double needs room for both hands, and should not crowd
                    // doubles that the base converter already placed nearby.
                    if (heads.Values.Any(group => group.Length > 1 && group.Select(note => note.Side).Distinct().Count() > 1
                                                   && Math.Abs(group[0].StartTime - time) < beat * 2))
                        return false;
                    var atHead = new HashSet<SticksHitObject>(excluded);
                    foreach (SticksHitObject note in baseline.Where(note => Math.Abs(note.StartTime - time) <= epsilon))
                        atHead.Add(note);
                    if (!free(StickSide.Left, time, time, recovery, atHead) || !free(StickSide.Right, time, time, recovery, atHead))
                        return false;
                }
                foreach (HandAssignment assignment in candidate.Assignments)
                {
                    SticksHitObject note = assignment.Note;
                    if (!free(assignment.Side, note.StartTime, note.GetEndTime(), recovery, excluded))
                        return false;
                }
                foreach (SticksHitObject note in candidate.Additions)
                {
                    if (!free(note.Side, note.StartTime, note.GetEndTime(), recovery, excluded))
                        return false;
                }
                HandAssignment[] gestures = candidate.Assignments.Concat(candidate.Additions.Select(note => new HandAssignment(note, note.Side))).ToArray();
                for (int i = 0; i < gestures.Length; i++)
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (gestures[i].Side == gestures[j].Side
                            && intervalsConflict(gestures[i].Note.StartTime, gestures[i].Note.GetEndTime(),
                                gestures[j].Note.StartTime, gestures[j].Note.GetEndTime(), recovery))
                            return false;
                    }
                }
                return true;
            }

            private bool introducesCoordination(Candidate candidate)
            {
                if (converter.coordinationAllowance?.IsRestricted != true)
                    return false;

                // A response on the release is sequential. Keep it, along with ordinary
                // hand assignments; only an added head inside another gesture (or a
                // same-time double) needs an opportunity for simultaneous play.
                return candidate.Additions.Any(added => converted.HitObjects.Concat(candidate.Additions).Any(existing =>
                    !ReferenceEquals(added, existing) && added.Side != sideAfterAssignment(existing)
                    && (Math.Abs(added.StartTime - existing.StartTime) <= epsilon
                        || added.StartTime < existing.GetEndTime() - epsilon && existing.StartTime < added.GetEndTime() - epsilon)));

                StickSide sideAfterAssignment(SticksHitObject note)
                {
                    foreach (HandAssignment assignment in candidate.Assignments)
                    {
                        if (ReferenceEquals(assignment.Note, note))
                            return assignment.Side;
                    }
                    return note.Side;
                }
            }

            private double score(Candidate candidate)
            {
                double value = candidate.Evidence;
                string? key = motifs.Value.KeyAt(candidate.SourceIndex);
                if (key != null && rememberedRecipes.TryGetValue(key, out Recipe? recipe))
                {
                    Recipe current = recipeFor(candidate);
                    value += recipe.Family == current.Family ? 4 : -2;
                    if (recipe == current)
                        value += 4;
                }
                else if (lastFamily == candidate.Family)
                    value -= 1.5;
                if (candidate.Assignments.Length > 0 && candidate.LeadSide != lastLead)
                    value += 0.5;
                return value;
            }

            private Recipe recipeFor(Candidate candidate)
            {
                double start = source[candidate.SourceIndex].StartTime;
                double beat = beatAt(start);
                string rhythm = string.Join(";", candidate.Additions.Select(note =>
                    $"{Math.Round((note.StartTime - start) / beat * 96)},{Math.Round((note.GetEndTime() - start) / beat * 96)}"))
                    + "/" + string.Join(",", candidate.Assignments.Select(assignment => $"{Math.Round((assignment.Note.StartTime - start) / beat * 96)}:{Math.Round((assignment.Note.GetEndTime() - start) / beat * 96)}:{assignment.Side}"));
                return new Recipe(candidate.Family, rhythm, candidate.Assignments.Length > 0 ? candidate.LeadSide : null);
            }

            private bool free(StickSide side, double start, double end, double recovery, HashSet<SticksHitObject>? excluded = null)
            {
                if (excluded == null || excluded.Count == 0)
                    return !(side == StickSide.Left ? left : right).Conflicts(start, end, recovery);
                foreach (SticksHitObject note in converted.HitObjects)
                {
                    if (note.Side == side && (excluded == null || !excluded.Contains(note))
                        && intervalsConflict(start, end, note.StartTime, note.GetEndTime(), recovery))
                        return false;
                }
                return true;
            }

            private void rebuildLanes()
            {
                left = new Lane(converted.HitObjects.Where(note => note.Side == StickSide.Left));
                right = new Lane(converted.HitObjects.Where(note => note.Side == StickSide.Right));
            }

            // Sorted starts plus prefix endpoint maxima answer occupancy queries in
            // logarithmic time, including a sustain that began several heads earlier.
            private sealed class Lane
            {
                private readonly double[] starts;
                private readonly double[] ends;

                public Lane(IEnumerable<SticksHitObject> notes)
                {
                    SticksHitObject[] ordered = notes.OrderBy(note => note.StartTime).ToArray();
                    starts = new double[ordered.Length];
                    ends = new double[ordered.Length];
                    double end = double.NegativeInfinity;
                    for (int i = 0; i < ordered.Length; i++)
                    {
                        starts[i] = ordered[i].StartTime;
                        ends[i] = end = Math.Max(end, ordered[i].GetEndTime());
                    }
                }

                public bool Conflicts(double start, double end, double recovery)
                {
                    int lower = 0;
                    int upper = starts.Length;
                    while (lower < upper)
                    {
                        int middle = lower + (upper - lower) / 2;
                        if (starts[middle] < end + recovery - epsilon)
                            lower = middle + 1;
                        else
                            upper = middle;
                    }
                    return lower > 0 && ends[lower - 1] > start - recovery + epsilon;
                }
            }

            private static bool intervalsConflict(double start, double end, double otherStart, double otherEnd, double recovery)
                => start < otherEnd + recovery - epsilon && end > otherStart - recovery + epsilon;

            private double recoveryAt(double beat) => Math.Max(420 - 220 * intensity, beat * (0.75 - 0.35 * intensity));
            private double beatAt(double time) => validBeatLength(beatmap.ControlPointInfo.TimingPointAt(time).BeatLength);

            private double windowAt(double time)
            {
                var timing = beatmap.ControlPointInfo.TimingPointAt(time);
                double size = validBeatLength(timing.BeatLength) * 8;
                return timing.Time + Math.Floor((time - timing.Time) / size) * size;
            }

            private bool onBeat(double time, double beat)
            {
                double position = (time - beatmap.ControlPointInfo.TimingPointAt(time).Time) / beat;
                return Math.Abs(position - Math.Round(position)) < 0.04;
            }

            private int structureAt(int index, double beat)
            {
                HitObject note = source[index];
                double[] localGaps = Enumerable.Range(Math.Max(1, index - 4), Math.Min(source.Length - 1, index + 4) - Math.Max(1, index - 4) + 1)
                    .Select(i => source[i].StartTime - source[i - 1].StartTime)
                    .Where(gap => gap > 0 && gap <= beat * 2).Order().ToArray();
                double cadence = localGaps.Length > 0 ? localGaps[localGaps.Length / 2] : beat;
                double rest = Math.Max(beat * 1.5, cadence * 2.5);
                int strength = note is IHasCombo { NewCombo: true } ? 1 : 0;
                if (index > 0 && note.StartTime - source[index - 1].GetEndTime() > rest)
                    strength += 2;
                if (index + 1 < source.Length && source[index + 1].StartTime - note.GetEndTime() > rest)
                    strength += 2;
                if (duetAccented(note) && !source.Skip(Math.Max(0, index - 3)).Take(Math.Min(3, index)).Any(duetAccented))
                    strength += 2;
                return strength;
            }

            private float responseAngle(int index, StickSide side, double time, int offset, SticksSlider primary)
            {
                double beat = beatAt(time);
                HitObject[] recalled = source.Skip(index).Take(4)
                    .Where(note => Math.Abs(time - note.StartTime) <= beat * 8 && note is IHasPosition).ToArray();
                if (recalled.Length > 0)
                    return converter.plans[recalled[offset % recalled.Length]].Angle;
                SticksHitObject? neighbour = baseline.Where(note => note.Side == side && Math.Abs(note.StartTime - time) <= beat * 8)
                    .OrderBy(note => Math.Abs(note.StartTime - time)).FirstOrDefault();
                return neighbour?.Angle ?? SticksHitObject.NormaliseAngle(180 - primary.AngleAt(time));
            }

            private int responseDirection(int index, SticksSlider primary, double time)
            {
                // Continue the source phrase's signed contour when it supplies one.
                // This permits parallel and contrary motion without an angle-fit gate.
                if (index + 1 < source.Length && source[index + 1].StartTime - source[index].GetEndTime() <= beatAt(time) * 2)
                {
                    float contour = SticksHitObject.DeltaAngle(converter.plans[source[index]].Angle, converter.plans[source[index + 1]].Angle);
                    int direction = Math.Sign(primary.SegmentArcAngleAt(primary.SegmentIndexAt(time)));
                    if (Math.Abs(contour) > 5 && Math.Abs(contour) < 175 && direction != 0)
                        return Math.Sign(contour) * direction;
                }
                return 1;
            }

            private SticksSlider? slice(SticksSlider primary, double start, double end, StickSide side, float angle, HitObject original, int startNode, int motion)
            {
                var cuts = new List<double> { start };
                double boundary = primary.StartTime;
                for (int segment = 0; segment < primary.SegmentCount; segment++)
                {
                    boundary += primary.SegmentDurationAt(segment);
                    if (boundary > start + epsilon && boundary < end - epsilon)
                        cuts.Add(boundary);
                }
                cuts.Add(end);
                if (cuts.Count - 1 > SticksSlider.MAX_SEGMENT_COUNT)
                    return null;
                var arcs = new List<float>();
                var durations = new List<double>();
                float position = 0;
                float minimum = 0;
                float maximum = 0;
                for (int i = 1; i < cuts.Count; i++)
                {
                    // Keep the complete signed arc, including segments longer than half a turn.
                    double duration = cuts[i] - cuts[i - 1];
                    int segment = primary.SegmentIndexAt((cuts[i - 1] + cuts[i]) / 2);
                    float arc = motion * primary.SegmentArcAngleAt(segment) * (float)(duration / primary.SegmentDurationAt(segment));
                    if (Math.Abs(arc) / duration * 1000 > MAX_GENERATED_SLIDER_ANGULAR_VELOCITY + 0.001)
                        return null;
                    arcs.Add(arc);
                    durations.Add(duration);
                    position += arc;
                    minimum = Math.Min(minimum, position);
                    maximum = Math.Max(maximum, position);
                }
                if (maximum - minimum <= SticksHitObject.HitAngleForCircleSize(beatmap.Difficulty.CircleSize))
                    return null;
                var answer = new SticksSlider { StartTime = start, Duration = end - start, Side = side, Angle = angle, Samples = samplesAt(original, startNode) };
                answer.SetTimedSegments(arcs, durations);
                if (!SticksCounterpointMotion.HasReadableReversals(answer))
                    return null;
                for (int i = 0; i < cuts.Count; i++)
                    answer.NodeSamples.Add(samplesAtTime(original, cuts[i]));
                return answer;
            }

            private bool hasHeadAt(double time) => baseline.Any(note => Math.Abs(note.StartTime - time) <= epsilon);

            private IList<HitSampleInfo> samplesAtTime(HitObject original, double time)
            {
                if (original is IHasDuration { Duration: > 0 } duration && original is IHasRepeats repeats)
                {
                    double node = (time - original.StartTime) / duration.Duration * (repeats.RepeatCount + 1);
                    if (Math.Abs(node - Math.Round(node)) < 0.00001)
                        return samplesAt(original, (int)Math.Round(node));
                }
                return samplesAt(original, -1);
            }

            private IList<HitSampleInfo> samplesAt(HitObject original, int node)
            {
                if (!converter.DisableBeatmapHitsounds && original is IHasRepeats repeats && node >= 0
                    && node < repeats.NodeSamples.Count && repeats.NodeSamples[node].Count > 0)
                    return cloneSamples(repeats.NodeSamples[node]);
                if (node < 0)
                {
                    HitSampleInfo? normal = original.Samples.FirstOrDefault(sample => sample.Name == HitSampleInfo.HIT_NORMAL);
                    return !converter.DisableBeatmapHitsounds && normal != null ? cloneSamples(new[] { normal }) : normalisedConversionSamples();
                }
                return converter.conversionSamples(original);
            }

            private static void alignAddedHead(SticksHitObject added, SticksHitObject existing)
            {
                if (Math.Abs(SticksHitObject.DeltaAngle(added.Angle, existing.Angle)) <= 5)
                    added.Angle = existing.Angle;
            }

            private enum Family { StaggeredVoice, RhythmAnswer, PhraseLead, PhraseSupport, Punctuation, RhythmicVoices, StaggeredPhrase }
            private sealed record Recipe(Family Family, string Rhythm, StickSide? Lead);
            private readonly record struct HandAssignment(SticksHitObject Note, StickSide Side);
            private sealed record Candidate(Family Family, int SourceIndex, SticksHitObject[] Additions,
                                            HandAssignment[] Assignments, StickSide LeadSide, double[] DoubleTimes, double Evidence)
            {
                public double Start => Math.Min(Additions.Length > 0 ? Additions.Min(note => note.StartTime) : double.PositiveInfinity,
                    Assignments.Length > 0 ? Assignments.Min(assignment => assignment.Note.StartTime) : double.PositiveInfinity);
                public double End => Math.Max(Additions.Length > 0 ? Additions.Max(note => note.GetEndTime()) : double.NegativeInfinity,
                    Assignments.Length > 0 ? Assignments.Max(assignment => assignment.Note.GetEndTime()) : double.NegativeInfinity);
            }
        }
    }
}
