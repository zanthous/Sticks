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
        private readonly Dictionary<HitObject, bool> duetDurationPartners = new Dictionary<HitObject, bool>();

        /// <summary>
        /// Chooses whole musical gestures before committing either stick. Candidate scores favour
        /// recognizable source geometry over added density; no parity rule is used in this mode.
        /// </summary>
        private void applyDuetPatterns(HitObject[] objects, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            double lastPhraseStart = double.NegativeInfinity;
            DuetReservations reservations = createDuetReservations(objects);
            var protectedHeads = new HashSet<HitObject>(generatedChordPartners.Keys);
            // Explicit same-time source chords also need protection. A circle at a phrase
            // tail must not be consumed if it owns one of the baseline chord's two heads.
            for (int start = 0; start < objects.Length;)
            {
                int end = start + 1;
                while (end < objects.Length && objects[end].StartTime - objects[start].StartTime < 0.01)
                    end++;
                HitObject[] emitted = objects[start..end].Where(note => plans[note].Emit).ToArray();
                if (emitted.Select(note => plans[note].Side).Distinct().Count() == 2)
                    protectedHeads.UnionWith(emitted);
                start = end;
            }

            for (int i = 0; i < objects.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HitObject head = objects[i];
                var timing = beatmap.ControlPointInfo.TimingPointAt(head.StartTime);
                double beatLength = validBeatLength(timing.BeatLength);
                double beatPosition = (head.StartTime - timing.Time) / beatLength;
                bool onBeat = Math.Abs(beatPosition - Math.Round(beatPosition)) <= 0.06;
                bool afterRest = i == 0 || head.StartTime - objects[i - 1].GetEndTime() >= beatLength * 1.5;

                if ((!onBeat && !afterRest) || head.StartTime - lastPhraseStart < beatLength * 8)
                    continue;

                // A bounded window permits different phrase lengths and syncopation without
                // searching all pairs of map objects. Timing changes terminate the window.
                var window = new List<HitObject>(9);
                for (int j = i; j < objects.Length && window.Count < 9; j++)
                {
                    HitObject note = objects[j];
                    if (!isAvailableOrdinaryAnchor(note) || protectedHeads.Contains(note)
                        || beatmap.ControlPointInfo.TimingPointAt(note.StartTime).Time != timing.Time)
                        break;

                    if (j > i)
                    {
                        double interval = note.StartTime - objects[j - 1].StartTime;
                        if (interval < 100 || interval > beatLength * 1.6 || !duetRhythmicInterval(interval, beatLength))
                            break;
                    }

                    if (note.StartTime - head.StartTime > beatLength * 8)
                        break;

                    window.Add(note);
                }

                DuetCandidate? best = null;
                for (int length = 4; length <= window.Count; length++)
                {
                    HitObject[] phrase = window.Take(length).ToArray();
                    foreach (DuetCandidate candidate in duetCandidates(phrase, beatLength, Math.Max(1, beatmap.Difficulty.SliderTickRate)))
                    {
                        if (best != null && candidate.Score <= best.Score)
                            continue;

                        if (duetCanReserve(candidate.Gestures, phrase, reservations))
                            best = candidate;
                        else
                        {
                            DuetGesture[] swapped = candidate.Gestures.Select(gesture => gesture with { Side = other(gesture.Side) }).ToArray();
                            if (duetCanReserve(swapped, phrase, reservations))
                                best = candidate with { Gestures = swapped };
                        }
                    }
                }

                if (best == null)
                    continue;

                commitDuetCandidate(best);
                for (int j = i; j < i + best.Sources.Length; j++)
                    updateDuetReservation(reservations, objects[j], j);
                lastPhraseStart = head.StartTime;
                i += best.Sources.Length - 1;
            }

            // The standard passes already handled streams and rapid alternation. Running
            // them again could move or delete untouched baseline notes after a Duet commit.
            planDuetDurationPartners(objects, beatmap, cancellationToken);
            planDuetAccompaniment(objects, beatmap, cancellationToken);
        }

        private IEnumerable<DuetCandidate> duetCandidates(HitObject[] phrase, double beatLength, double tickRate)
        {
            StickSide primary = plans[phrase[0]].Side;
            StickSide secondary = other(primary);
            double duration = phrase[^1].StartTime - phrase[0].StartTime;

            // A-B-C-A shapes suggest keeping an outer anchor held while the other stick
            // traces B-C. Moving outer endpoints instead produce nested sweeps.
            if (phrase.Length == 4 && duration >= Math.Max(1200, beatLength * 2)
                && phrase[2].StartTime - phrase[1].StartTime >= Math.Max(600, beatLength)
                && duetEndpointArc(phrase[0], phrase[3], out float outerArc)
                && duetEndpointArc(phrase[1], phrase[2], out float innerArc)
                && Math.Abs(innerArc) >= 30)
            {
                bool outerHold = Math.Abs(outerArc) <= 20;
                yield return new DuetCandidate(phrase, new[]
                {
                    new DuetGesture(phrase[0], phrase[3], primary, outerHold ? 0 : outerArc),
                    new DuetGesture(phrase[1], phrase[2], secondary, innerArc),
                }, outerHold ? 100 : 76);
            }

            // Split alternating source anchors into two voices without fitting a sweep.
            // Each moving voice keeps every direction at its source timestamp, including
            // changes in speed, pauses and reversals. Boundaries provide their own checkpoints.
            if (phrase.Length % 2 == 0)
            {
                HitObject[] first = phrase.Where((_, index) => index % 2 == 0).ToArray();
                HitObject[] second = phrase.Where((_, index) => index % 2 != 0).ToArray();
                if (first[^1].StartTime - first[0].StartTime >= Math.Max(700, beatLength * 1.5)
                    && second[^1].StartTime - second[0].StartTime >= Math.Max(700, beatLength * 1.5)
                    && tryDuetVoicePath(first, beatLength / tickRate, out DuetVoicePath firstPath)
                    && tryDuetVoicePath(second, beatLength / tickRate, out DuetVoicePath secondPath)
                    && (firstPath.TotalTravel > 0 || secondPath.TotalTravel > 0))
                {
                    double separation = Math.Abs(SticksHitObject.DeltaAngle(plans[first[0]].Angle, plans[second[0]].Angle));
                    bool contrary = firstPath.NetArc * secondPath.NetArc < 0;
                    if (separation >= 45 || contrary)
                    {
                        yield return new DuetCandidate(phrase, new[]
                        {
                            new DuetGesture(first[0], first[^1], primary, firstPath.NetArc, Path: firstPath),
                            new DuetGesture(second[0], second[^1], secondary, secondPath.NetArc, Path: secondPath),
                        }, 78 + phrase.Length + (contrary ? 8 : 0));
                    }
                }
            }

            // Keep the inner rhythm as distinct flicks, including uneven / syncopated
            // spacing. Only head and tail become the sustained voice's timing anchors.
            if (phrase.Length >= 5 && duration >= Math.Max(1000, beatLength * 2)
                && phrase.Zip(phrase.Skip(1), (a, b) => b.StartTime - a.StartTime).All(gap => gap > RAPID_ALTERNATION_THRESHOLD)
                && duetEndpointArc(phrase[0], phrase[^1], out float accompanimentArc))
            {
                float spread = phrase.Max(note => Math.Abs(SticksHitObject.DeltaAngle(plans[phrase[0]].Angle, plans[note].Angle)));
                bool hold = spread <= 20;
                // Returning to the same angle after a moving phrase does not by itself
                // imply a held voice; keep it for a chord or another window instead.
                if (hold || Math.Abs(accompanimentArc) >= 25)
                {
                    var gestures = new List<DuetGesture>
                    {
                        new DuetGesture(phrase[0], phrase[^1], primary, hold ? 0 : accompanimentArc),
                    };
                    gestures.AddRange(phrase.Skip(1).Take(phrase.Length - 2)
                                            .Select(note => new DuetGesture(note, note, secondary, 0)));
                    yield return new DuetCandidate(phrase, gestures.ToArray(), 55 + phrase.Length + (hold ? 4 : 0));
                }
            }

            // A recurring jump/accent rhythm becomes a short chord phrase. A single
            // relationship is maintained throughout: horizontal reflection for wide
            // source jumps, parallel motion for explicitly accented compact patterns.
            if (phrase.Length <= 5
                && phrase.Zip(phrase.Skip(1), (a, b) => b.StartTime - a.StartTime)
                         .All(gap => gap > RAPID_ALTERNATION_THRESHOLD && gap <= beatLength * 1.1))
            {
                double[] jumps = phrase.Zip(phrase.Skip(1), (a, b) =>
                    (double)Math.Abs(SticksHitObject.DeltaAngle(plans[a].Angle, plans[b].Angle))).ToArray();
                bool jumping = jumps.All(jump => jump >= 60) && jumps.Max() - jumps.Min() <= 30;
                bool accented = phrase.Count(duetAccented) >= 2;
                if (jumping || accented)
                {
                    var gestures = new List<DuetGesture>();
                    foreach (HitObject note in phrase)
                    {
                        gestures.Add(new DuetGesture(note, note, primary, 0));
                        gestures.Add(new DuetGesture(note, note, secondary, 0, jumping));
                    }

                    yield return new DuetCandidate(phrase, gestures.ToArray(), 68 + phrase.Length + (accented ? 8 : 0));
                }
            }
        }

        private bool duetEndpointArc(HitObject head, HitObject tail, out float arc)
        {
            arc = SticksHitObject.DeltaAngle(plans[head].Angle, plans[tail].Angle);
            double duration = tail.StartTime - head.StartTime;
            return duration > 0 && Math.Abs(arc) / duration * 1000 <= MAX_GENERATED_SLIDER_ANGULAR_VELOCITY;
        }

        private bool tryDuetVoicePath(HitObject[] voice, double tickInterval, out DuetVoicePath path)
        {
            var arcs = new float[voice.Length - 1];
            var durations = new double[arcs.Length];
            int previousDirection = 0;
            path = null!;
            for (int i = 0; i < arcs.Length; i++)
            {
                arcs[i] = SticksHitObject.DeltaAngle(plans[voice[i]].Angle, plans[voice[i + 1]].Angle);
                durations[i] = voice[i + 1].StartTime - voice[i].StartTime;
                if (durations[i] <= 0 || !float.IsFinite(arcs[i])
                    || Math.Abs(arcs[i]) / durations[i] * 1000 > MAX_GENERATED_SLIDER_ANGULAR_VELOCITY)
                    return false;

                int direction = Math.Sign(arcs[i]);
                if (direction == 0)
                    continue;
                if (DisableReversals && previousDirection != 0 && direction != previousDirection)
                    return false;
                previousDirection = direction;
            }

            path = new DuetVoicePath(voice, arcs, durations);
            // Entirely stationary voices use holds, whose existing ticks must still
            // cover the consumed interior notes. Moving paths carry explicit anchors.
            return path.TotalTravel > 0 || duetVoiceTicksAligned(voice, tickInterval);
        }

        private static bool duetVoiceTicksAligned(HitObject[] voice, double tickInterval) =>
            voice.Skip(1).Take(voice.Length - 2).All(note =>
            {
                double ticks = (note.StartTime - voice[0].StartTime) / tickInterval;
                return Math.Abs(ticks - Math.Round(ticks)) * tickInterval <= 1;
            });

        private static bool duetRhythmicInterval(double interval, double beatLength)
        {
            double beats = interval / beatLength;
            // Straight and triplet grids; timing tolerance is proportional to the beat.
            return Math.Abs(beats * 4 - Math.Round(beats * 4)) <= 0.12
                   || Math.Abs(beats * 3 - Math.Round(beats * 3)) <= 0.09;
        }

        private static bool duetCanReserve(DuetGesture[] gestures, HitObject[] sources, DuetReservations reservations)
        {
            for (int i = 0; i < gestures.Length; i++)
            {
                DuetGesture gesture = gestures[i];
                double start = gesture.Head.StartTime;
                double end = gesture.Tail.StartTime;
                if (reservations.Conflicts(gesture.Side, start, end, sources[0], sources[^1]))
                    return false;

                for (int j = 0; j < i; j++)
                {
                    DuetGesture previous = gestures[j];
                    if (previous.Side == gesture.Side
                        && start <= previous.Tail.StartTime + RAPID_ALTERNATION_THRESHOLD
                        && end >= previous.Head.StartTime - RAPID_ALTERNATION_THRESHOLD)
                        return false;
                }
            }

            return true;
        }

        private void commitDuetCandidate(DuetCandidate candidate)
        {
            foreach (HitObject source in candidate.Sources)
                plans[source] = plans[source] with { Emit = false };

            foreach (DuetGesture gesture in candidate.Gestures)
            {
                HitObject head = gesture.Head;
                ConversionPlan original = plans[head];
                float angle = gesture.Reflect ? SticksHitObject.NormaliseAngle(180 - original.Angle) : original.Angle;
                var plan = new ConversionPlan(gesture.Side, angle, gesture.Arc, true);

                if (original.Emit)
                {
                    generatedChordPartners[head] = plan;
                    continue;
                }

                plans[head] = plan;
                double duration = gesture.Tail.StartTime - head.StartTime;
                if (duration <= 0)
                    continue;

                if (gesture.Path is { TotalTravel: 0 } || gesture.Path == null && gesture.Arc == 0)
                    generatedFlickHoldDurations[head] = duration;
                else
                    generatedSliders[head] = new GeneratedSliderSpec(duration, gesture.Arc, gesture.Tail, gesture.Path);
            }
        }

        private void planDuetDurationPartners(HitObject[] objects, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            double lastPair = double.NegativeInfinity;
            // The alternation pass can move or suppress leftover heads. Refresh the index
            // before considering native partners so these final assignments are authoritative.
            DuetReservations reservations = createDuetReservations(objects);
            for (int i = 0; i < objects.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HitObject source = objects[i];
                if (!plans[source].Emit || source is not IHasDuration { Duration: > 0 } duration || isHoldSource(source))
                    continue;

                double beatLength = validBeatLength(beatmap.ControlPointInfo.TimingPointAt(source.StartTime).BeatLength);
                bool afterRest = i == 0 || source.StartTime - objects[i - 1].GetEndTime() >= beatLength;
                if (duration.Duration < Math.Max(700, beatLength * 1.5) || duration.Duration > beatLength * 8
                    || source.StartTime - lastPair < beatLength * 8
                    || (!afterRest && !duetAccented(source)) || Math.Abs(plans[source].ArcAngle) < 1)
                    continue;

                // Reserve both complete durations, including head and release margins.
                // The primary duration is excluded only because we are validating it too.
                var tail = new DuetTimeAnchor { StartTime = source.StartTime + duration.Duration };
                var gestures = new[]
                {
                    new DuetGesture(source, tail, plans[source].Side, 0),
                    new DuetGesture(source, tail, other(plans[source].Side), 0),
                };
                if (!duetCanReserve(gestures, new[] { source }, reservations))
                    continue;

                // Wide horizontal source placement suggests reflection; a central /
                // vertical source gesture suggests the parallel pairs in the references.
                float angle = plans[source].Angle;
                duetDurationPartners[source] = Math.Abs(MathF.Cos(angle * MathF.PI / 180)) > 0.7f;
                updateDuetReservation(reservations, source, i);
                lastPair = source.StartTime;
            }
        }

        private void addDuetDurationPartners(Beatmap<SticksHitObject> converted)
        {
            foreach ((HitObject source, bool reflect) in duetDurationPartners)
            {
                SticksSlider? primary = converted.HitObjects.OfType<SticksSlider>().FirstOrDefault(slider =>
                    slider.StartTime == source.StartTime && slider.Side == plans[source].Side);
                if (primary == null)
                    continue;

                var partner = new SticksSlider
                {
                    StartTime = primary.StartTime,
                    Duration = primary.Duration,
                    Side = other(primary.Side),
                    Angle = reflect ? SticksHitObject.NormaliseAngle(180 - primary.Angle) : primary.Angle,
                    ArcAngle = reflect ? -primary.ArcAngle : primary.ArcAngle,
                    RepeatCount = primary.RepeatCount,
                    Samples = cloneSamples(primary.Samples),
                };
                foreach (IList<HitSampleInfo> samples in primary.NodeSamples)
                    partner.NodeSamples.Add(cloneSamples(samples));

                converted.HitObjects.Add(partner);
            }

            // The converter returns a chronological top-level list to gameplay, difficulty
            // calculation and the editor, even when a late pass adds duration partners.
            converted.HitObjects = converted.HitObjects.OrderBy(note => note.StartTime).ToList();
        }

        private static bool duetAccented(HitObject source) => source.Samples.Any(sample =>
            sample.Name == HitSampleInfo.HIT_CLAP || sample.Name == HitSampleInfo.HIT_FINISH);

        private DuetReservations createDuetReservations(HitObject[] objects)
        {
            var reservations = new DuetReservations(objects);
            for (int i = 0; i < objects.Length; i++)
                updateDuetReservation(reservations, objects[i], i);
            return reservations;
        }

        private void updateDuetReservation(DuetReservations reservations, HitObject source, int index)
        {
            ConversionPlan plan = plans[source];
            double end = plan.Emit
                ? source.StartTime + (tryGetPlannedDuration(source, out double duration) ? duration : 0)
                : double.NegativeInfinity;
            bool bothSides = generatedChordPartners.ContainsKey(source) || duetDurationPartners.ContainsKey(source);
            reservations.Set(index, plan.Side, end, bothSides);
        }

        /// <summary>
        /// A segment tree of latest occupied endpoints on each stick. Source starts remain
        /// sorted, while committing a phrase updates only its few leaves. A reservation can
        /// skip entire past/future subtrees and the contiguous source window it replaces.
        /// </summary>
        private sealed class DuetReservations
        {
            private readonly HitObject[] objects;
            private readonly Dictionary<HitObject, int> indices;
            private readonly double[] leftEnds;
            private readonly double[] rightEnds;

            public DuetReservations(HitObject[] objects)
            {
                this.objects = objects;
                indices = objects.Select((note, index) => (note, index)).ToDictionary(pair => pair.note, pair => pair.index);
                leftEnds = new double[Math.Max(1, objects.Length * 4)];
                rightEnds = new double[leftEnds.Length];
                Array.Fill(leftEnds, double.NegativeInfinity);
                Array.Fill(rightEnds, double.NegativeInfinity);
            }

            public void Set(int index, StickSide side, double end, bool bothSides) =>
                set(1, 0, objects.Length - 1, index, side, end, bothSides);

            // Recursion allocates no per-query lists or delegates while traversing the tree.
            private void set(int node, int first, int last, int index, StickSide side, double end, bool bothSides)
            {
                if (first == last)
                {
                    leftEnds[node] = bothSides || side == StickSide.Left ? end : double.NegativeInfinity;
                    rightEnds[node] = bothSides || side == StickSide.Right ? end : double.NegativeInfinity;
                    return;
                }

                int middle = (first + last) / 2;
                if (index <= middle)
                    set(node * 2, first, middle, index, side, end, bothSides);
                else
                    set(node * 2 + 1, middle + 1, last, index, side, end, bothSides);
                leftEnds[node] = Math.Max(leftEnds[node * 2], leftEnds[node * 2 + 1]);
                rightEnds[node] = Math.Max(rightEnds[node * 2], rightEnds[node * 2 + 1]);
            }

            public bool Conflicts(StickSide side, double start, double end, HitObject firstSource, HitObject lastSource) =>
                conflicts(1, 0, objects.Length - 1, side == StickSide.Left ? leftEnds : rightEnds,
                    start - RAPID_ALTERNATION_THRESHOLD, end + RAPID_ALTERNATION_THRESHOLD,
                    indices[firstSource], indices[lastSource]);

            private bool conflicts(int node, int first, int last, double[] ends, double earliestEnd, double latestStart, int excludedFirst, int excludedLast)
            {
                if (ends[node] < earliestEnd || objects[first].StartTime > latestStart
                    || first >= excludedFirst && last <= excludedLast)
                    return false;
                if (first == last)
                    return true;

                int middle = (first + last) / 2;
                return conflicts(node * 2, first, middle, ends, earliestEnd, latestStart, excludedFirst, excludedLast)
                       || conflicts(node * 2 + 1, middle + 1, last, ends, earliestEnd, latestStart, excludedFirst, excludedLast);
            }
        }

        private sealed record DuetCandidate(HitObject[] Sources, DuetGesture[] Gestures, double Score);

        private readonly record struct DuetGesture(HitObject Head, HitObject Tail, StickSide Side, float Arc, bool Reflect = false, DuetVoicePath? Path = null);

        private sealed record DuetVoicePath(HitObject[] Anchors, float[] Arcs, double[] Durations)
        {
            public float NetArc => Arcs.Sum();
            public float TotalTravel => Arcs.Sum(Math.Abs);
        }

        private sealed class DuetTimeAnchor : HitObject
        {
        }
    }
}
