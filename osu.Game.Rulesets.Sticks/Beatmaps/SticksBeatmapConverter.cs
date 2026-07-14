// Copyright (c) Zanthous. Licensed under the MIT Licence.

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
using osuTK;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    public class SticksBeatmapConverter : BeatmapConverter<SticksHitObject>
    {
        public static readonly Vector2 STANDARD_CENTRE = new Vector2(256, 192);

        public const double MAX_REVERSAL_ANGULAR_VELOCITY = 180;

        public const double MIN_GENERATED_REVERSAL_SPAN_DURATION = 250;

        /// <summary>
        /// Maximum angular velocity of sliders produced by procedural conversion, in degrees per second.
        /// Authored Sticks sliders are intentionally not affected.
        /// </summary>
        public const double MAX_GENERATED_SLIDER_ANGULAR_VELOCITY = 120;

        // Base conversion readability on the ruleset's default player AR 5. Faster ARs have
        // a shorter approach, while unusually slow player overrides may exceed this window.
        public const double VISIBILITY_PREEMPT = 850;

        // This is a physical readability threshold, not a hit-window duration. Standard-style
        // miss windows are intentionally broad and must not cause playable half-beat patterns to
        // be deleted merely because two notes fall within each other's judgement lifetime.
        public const double RAPID_ALTERNATION_THRESHOLD = 260;

        private readonly Dictionary<HitObject, ConversionPlan> plans = new Dictionary<HitObject, ConversionPlan>();
        private readonly Dictionary<HitObject, ConversionPlan> generatedChordPartners = new Dictionary<HitObject, ConversionPlan>();
        private readonly Dictionary<HitObject, ConversionPlan> chordLinkTargets = new Dictionary<HitObject, ConversionPlan>();
        private readonly HashSet<HitObject> generatedHoldSources = new HashSet<HitObject>();
        private readonly Dictionary<HitObject, double> generatedFlickHoldDurations = new Dictionary<HitObject, double>();
        private readonly Ruleset targetRuleset;

        public bool DisableReversals { get; set; }

        public SticksBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset)
        {
            targetRuleset = ruleset;
            buildPlans(beatmap);
        }

        public override bool CanConvert() => true;

        protected override Beatmap<SticksHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
        {
            Beatmap<SticksHitObject> converted = base.ConvertBeatmap(original, cancellationToken);

            // The base converter retains the source map's ruleset metadata. Sticks needs its own
            // instantiation info here so EditorBeatmap constructs the Sticks editor processor,
            // while ordinary gameplay keeps the custom online ID (-1).
            converted.BeatmapInfo.Ruleset = targetRuleset.RulesetInfo.Clone();

            foreach (IGrouping<double, SticksFlick> group in converted.HitObjects.OfType<SticksFlick>().GroupBy(flick => flick.StartTime))
            {
                SticksFlick[] simultaneous = group.ToArray();
                if (simultaneous.Length != 2 || simultaneous[0].Side == simultaneous[1].Side)
                    continue;

                simultaneous[0].SyncedNoteSide = simultaneous[1].Side;
                simultaneous[0].SyncedNoteAngle = simultaneous[1].Angle;
            }

            return converted;
        }

        protected override IEnumerable<SticksHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (SticksAuthoredBeatmapCodec.TryDecode(original, out SticksHitObject? authoredObject))
            {
                if (DisableReversals && authoredObject is SticksSlider { RepeatCount: > 0 } authoredSlider)
                {
                    float continuousArc = authoredSlider.InitialDirection * authoredSlider.TotalAngularDistance;
                    authoredSlider.RepeatCount = 0;
                    authoredSlider.ArcAngle = continuousArc;
                }

                yield return authoredObject!;
                yield break;
            }

            ConversionPlan plan = plans[original];

            if (!plan.Emit)
                yield break;

            if (generatedFlickHoldDurations.TryGetValue(original, out double generatedHoldDuration))
            {
                yield return new SticksHold
                {
                    StartTime = original.StartTime,
                    Duration = generatedHoldDuration,
                    Side = plan.Side,
                    Angle = plan.Angle,
                    Samples = normalisedConversionSamples(),
                };
                yield break;
            }

            if (original is IHasDuration duration && duration.Duration > 0)
            {
                if (isHoldSource(original))
                {
                    yield return new SticksHold
                    {
                        StartTime = original.StartTime,
                        Duration = duration.Duration,
                        Side = plan.Side,
                        Angle = plan.Angle,
                        Samples = normalisedConversionSamples(),
                    };
                    yield break;
                }

                int sourceRepeatCount = original is IHasRepeats repeats ? Math.Max(0, repeats.RepeatCount) : 0;
                int sourceSpanCount = sourceRepeatCount + 1;
                double sourceSpanDuration = duration.Duration / sourceSpanCount;
                double angularVelocity = Math.Abs(plan.ArcAngle) / Math.Max(1, sourceSpanDuration) * 1000;
                bool removeReversals = DisableReversals
                                       || sourceRepeatCount > 0
                                       && (sourceSpanDuration < MIN_GENERATED_REVERSAL_SPAN_DURATION
                                           || angularVelocity >= MAX_REVERSAL_ANGULAR_VELOCITY - 0.001);
                var slider = new SticksSlider
                {
                    StartTime = original.StartTime,
                    Duration = duration.Duration,
                    RepeatCount = removeReversals ? 0 : sourceRepeatCount,
                    Side = plan.Side,
                    Angle = plan.Angle,
                    ArcAngle = removeReversals ? plan.ArcAngle * sourceSpanCount : plan.ArcAngle,
                    Samples = normalisedConversionSamples(),
                };

                yield return slider;
            }
            else
            {
                var flick = new SticksFlick
                {
                    StartTime = original.StartTime,
                    Side = plan.Side,
                    Angle = plan.Angle,
                    Samples = normalisedConversionSamples(),
                };

                if (chordLinkTargets.TryGetValue(original, out ConversionPlan linkedPlan))
                {
                    flick.SyncedNoteSide = linkedPlan.Side;
                    flick.SyncedNoteAngle = linkedPlan.Angle;
                }

                yield return flick;

                if (generatedChordPartners.TryGetValue(original, out ConversionPlan partner))
                {
                    yield return new SticksFlick
                    {
                        StartTime = original.StartTime,
                        Side = partner.Side,
                        Angle = partner.Angle,
                        Samples = normalisedConversionSamples(),
                    };
                }
            }
        }

        private static IList<HitSampleInfo> normalisedConversionSamples() =>
            new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) };

        private void buildPlans(IBeatmap beatmap)
        {
            HitObject[] objects = beatmap.HitObjects.OrderBy(hitObject => hitObject.StartTime).ToArray();
            var activeSliders = new List<(double endTime, StickSide side)>();
            var lastUsed = new Dictionary<StickSide, double>
            {
                [StickSide.Left] = double.NegativeInfinity,
                [StickSide.Right] = double.NegativeInfinity,
            };
            StickSide phraseSide = StickSide.Left;
            int phraseRemaining = 0;
            var usedSidesAtTimestamp = new HashSet<StickSide>();
            double currentTimestamp = double.NaN;
            bool hasPreviousHead = false;
            double previousHeadTime = double.NegativeInfinity;
            StickSide previousHeadSide = StickSide.Left;

            for (int i = 0; i < objects.Length; i++)
            {
                HitObject current = objects[i];

                if (double.IsNaN(currentTimestamp) || Math.Abs(current.StartTime - currentTimestamp) >= 0.01)
                {
                    currentTimestamp = current.StartTime;
                    usedSidesAtTimestamp.Clear();
                }

                // Keep a slider reserved until a following note's entire approach animation
                // would begin after it. This prevents same-colour notes appearing beneath it.
                activeSliders.RemoveAll(slider => slider.endTime < current.StartTime - VISIBILITY_PREEMPT - 0.01);
                bool isDuration = current is IHasDuration { Duration: > 0 };
                TimingControlPoint timing = beatmap.ControlPointInfo.TimingPointAt(current.StartTime);
                double beatLength = validBeatLength(timing.BeatLength);
                StickSide side;

                StickSide[] occupied = activeSliders.Select(slider => slider.side).Distinct().ToArray();
                StickSide[] available = new[] { StickSide.Left, StickSide.Right }
                                        .Where(candidate => !occupied.Contains(candidate) && !usedSidesAtTimestamp.Contains(candidate))
                                        .ToArray();

                if (available.Length == 0)
                {
                    float suppressedAngle = sourceAngle(current, i);
                    float suppressedArc = isDuration ? generatedArc(current, beatmap, suppressedAngle, i) : 0;
                    plans[current] = new ConversionPlan(StickSide.Left, suppressedAngle, suppressedArc, false);
                    continue;
                }

                if (available.Length == 1)
                {
                    side = available[0];
                }
                else if (isDuration)
                {
                    side = lastUsed[StickSide.Left] <= lastUsed[StickSide.Right] ? StickSide.Left : StickSide.Right;
                }
                else
                {
                    bool startsPhrase = phraseRemaining <= 0
                                        || (i > 0 && current.StartTime - objects[i - 1].StartTime >= beatLength * 1.5);

                    if (startsPhrase)
                    {
                        phraseSide = lastUsed[StickSide.Left] <= lastUsed[StickSide.Right] ? StickSide.Left : StickSide.Right;
                        phraseRemaining = 2 + (int)(stableHash($"{i}:{current.StartTime}") % 3);
                    }

                    side = phraseSide;
                    phraseRemaining--;
                }

                if (hasPreviousHead)
                {
                    double interval = current.StartTime - previousHeadTime;
                    StickSide alternate = other(previousHeadSide);

                    if (interval > 0.01 && interval <= RAPID_ALTERNATION_THRESHOLD && side == previousHeadSide && available.Contains(alternate))
                    {
                        side = alternate;
                        phraseSide = alternate;
                    }
                }

                float angle = sourceAngle(current, i);
                float arcAngle = isDuration ? generatedArc(current, beatmap, angle, i) : 0;
                plans[current] = new ConversionPlan(side, angle, arcAngle, true);
                lastUsed[side] = current.StartTime;
                usedSidesAtTimestamp.Add(side);

                hasPreviousHead = true;
                previousHeadTime = current.StartTime;
                previousHeadSide = side;

                if (current is IHasDuration activeDuration && activeDuration.Duration > 0)
                    activeSliders.Add((current.StartTime + activeDuration.Duration, side));
            }

            applyGeneratedHoldSections(objects, beatmap);
            applyGeneratedFlickHoldSections(objects, beatmap);
            applyRarePatterns(objects, beatmap);
            enforceRapidAlternation(objects);
            applyGeneratedSyncedChords(objects, beatmap);
        }

        private void applyRarePatterns(HitObject[] objects, IBeatmap beatmap)
        {
            applyCoordinatedChords(objects);
            HashSet<HitObject> sliderAccompaniment = applySliderAccompaniment(objects, beatmap);
            applyAlternatingStreams(objects, sliderAccompaniment);
        }

        private void applyCoordinatedChords(HitObject[] objects)
        {
            int chordIndex = 0;
            float[] chordOffsets = { 180, 90, -90, 135, -135, 0 };

            for (int groupStart = 0; groupStart < objects.Length;)
            {
                int groupEnd = groupStart + 1;
                while (groupEnd < objects.Length && Math.Abs(objects[groupEnd].StartTime - objects[groupStart].StartTime) < 0.01)
                    groupEnd++;

                HitObject[] flicks = objects[groupStart..groupEnd]
                                     .Where(hitObject => plans[hitObject].Emit && !convertsToHoldOrSlider(hitObject))
                                     .ToArray();

                if (flicks.Length == 2)
                {
                    float anchor = plans[flicks[0]].Angle;
                    float offset = chordOffsets[chordIndex++ % chordOffsets.Length];

                    plans[flicks[0]] = plans[flicks[0]] with { Angle = anchor };
                    plans[flicks[1]] = plans[flicks[1]] with
                    {
                        Angle = SticksHitObject.NormaliseAngle(anchor + offset),
                    };
                    chordLinkTargets[flicks[0]] = plans[flicks[1]];
                }

                groupStart = groupEnd;
            }
        }

        private void applyGeneratedSyncedChords(HitObject[] objects, IBeatmap beatmap)
        {
            double lastChordStreakTime = double.NegativeInfinity;
            int chordStreakIndex = 0;
            float[] angleOffsets = { 180, 90, -90, 135, -135 };

            for (int i = 1; i < objects.Length - 1; i++)
            {
                HitObject current = objects[i];
                ConversionPlan plan = plans[current];

                if (!plan.Emit || convertsToHoldOrSlider(current))
                    continue;

                int simultaneousCount = objects.Count(candidate => Math.Abs(candidate.StartTime - current.StartTime) < 0.01 && plans[candidate].Emit);
                if (simultaneousCount != 1)
                    continue;

                TimingControlPoint timing = beatmap.ControlPointInfo.TimingPointAt(current.StartTime);
                double beatLength = validBeatLength(timing.BeatLength);
                double beatPosition = (current.StartTime - timing.Time) / beatLength;
                long nearestBeat = (long)Math.Round(beatPosition);
                bool onBeat = Math.Abs(beatPosition - nearestBeat) <= 0.08;
                bool strongBeat = onBeat && ((nearestBeat % 4) + 4) % 4 == 0;
                double previousGap = current.StartTime - objects[i - 1].StartTime;
                double nextGap = objects[i + 1].StartTime - current.StartTime;
                bool phraseEdge = previousGap >= beatLength * 1.5 || nextGap >= beatLength * 1.5;
                bool hasBreathingRoom = previousGap >= beatLength * 0.45 && nextGap >= beatLength * 0.45;
                bool safeChordSpeed = previousGap >= RAPID_ALTERNATION_THRESHOLD && nextGap >= RAPID_ALTERNATION_THRESHOLD;
                double minimumChordSpacing = Math.Max(2500, beatLength * 6);

                if ((!strongBeat && !phraseEdge)
                    || !hasBreathingRoom
                    || !safeChordSpeed
                    || current.StartTime - lastChordStreakTime < minimumChordSpacing)
                    continue;

                float relationshipOffset = angleOffsets[chordStreakIndex % angleOffsets.Length];
                int desiredStreakLength = Math.Min(3, 2 + (int)(stableHash($"chord-streak:{current.StartTime}") & 1));
                int added = 0;

                for (int offset = 0; offset < desiredStreakLength && i + offset < objects.Length - 1; offset++)
                {
                    HitObject candidate = objects[i + offset];

                    if (offset > 0)
                    {
                        double interval = candidate.StartTime - objects[i + offset - 1].StartTime;
                        double followingInterval = objects[i + offset + 1].StartTime - candidate.StartTime;
                        if (interval < RAPID_ALTERNATION_THRESHOLD || followingInterval < RAPID_ALTERNATION_THRESHOLD || interval > beatLength * 1.1)
                            break;
                    }

                    if (!tryAddGeneratedChord(candidate, objects, relationshipOffset))
                        break;

                    added++;
                }

                if (added > 0)
                {
                    lastChordStreakTime = current.StartTime;
                    chordStreakIndex++;
                    i += added - 1;
                }
            }
        }

        private bool tryAddGeneratedChord(HitObject current, HitObject[] objects, float relationshipOffset)
        {
            ConversionPlan plan = plans[current];
            if (!plan.Emit || convertsToHoldOrSlider(current))
                return false;

            if (objects.Count(candidate => Math.Abs(candidate.StartTime - current.StartTime) < 0.01 && plans[candidate].Emit) != 1)
                return false;

            StickSide partnerSide = other(plan.Side);
            bool partnerOccupied = objects.Any(candidate =>
                candidate != current
                && plans[candidate].Emit
                && plans[candidate].Side == partnerSide
                && candidate is IHasDuration { Duration: > 0 } duration
                && candidate.StartTime < current.StartTime
                && candidate.StartTime + duration.Duration >= current.StartTime - VISIBILITY_PREEMPT)
                || generatedFlickHoldDurations.Any(hold =>
                    plans[hold.Key].Side == partnerSide
                    && hold.Key.StartTime < current.StartTime
                    && hold.Key.StartTime + hold.Value >= current.StartTime - VISIBILITY_PREEMPT);

            if (partnerOccupied)
                return false;

            bool partnerHasRapidNeighbour = objects.Any(candidate =>
                candidate != current
                && plans[candidate].Emit
                && !convertsToHoldOrSlider(candidate)
                && plans[candidate].Side == partnerSide
                && Math.Abs(candidate.StartTime - current.StartTime) > 0.01
                && Math.Abs(candidate.StartTime - current.StartTime) <= RAPID_ALTERNATION_THRESHOLD)
                || generatedChordPartners.Any(chord =>
                    chord.Value.Side == partnerSide
                    && Math.Abs(chord.Key.StartTime - current.StartTime) <= RAPID_ALTERNATION_THRESHOLD);

            if (partnerHasRapidNeighbour)
                return false;

            var partner = new ConversionPlan(
                partnerSide,
                SticksHitObject.NormaliseAngle(plan.Angle + relationshipOffset),
                0,
                true);

            generatedChordPartners[current] = partner;
            chordLinkTargets[current] = partner;
            return true;
        }

        private void applyGeneratedHoldSections(HitObject[] objects, IBeatmap beatmap)
        {
            int eligibleSection = 0;
            double lastHoldTime = double.NegativeInfinity;

            foreach (HitObject durationObject in objects.Where(hitObject => plans[hitObject].Emit && hasDuration(hitObject) && !isNativeHoldSource(hitObject)))
            {
                var duration = (IHasDuration)durationObject;
                if (durationObject is IHasRepeats { RepeatCount: > 0 })
                    continue;

                double beatLength = validBeatLength(beatmap.ControlPointInfo.TimingPointAt(durationObject.StartTime).BeatLength);
                double beats = duration.Duration / beatLength;
                if (beats < 1.5 || beats > 6 || durationObject.StartTime - lastHoldTime < beatLength * 16)
                    continue;

                ConversionPlan durationPlan = plans[durationObject];
                HitObject[] accompaniment = objects.Where(hitObject =>
                    plans[hitObject].Emit
                    && !hasDuration(hitObject)
                    && plans[hitObject].Side != durationPlan.Side
                    && hitObject.StartTime > durationObject.StartTime + 100
                    && hitObject.StartTime < durationObject.StartTime + duration.Duration - 100)
                    .ToArray();

                if (accompaniment.Length < 3)
                    continue;

                bool intervalsPlayable = accompaniment.Zip(accompaniment.Skip(1), (first, second) => second.StartTime - first.StartTime)
                                                        .All(interval => interval >= 180);
                if (!intervalsPlayable || eligibleSection++ % 2 != 0)
                    continue;

                generatedHoldSources.Add(durationObject);
                lastHoldTime = durationObject.StartTime;
            }
        }

        private void applyGeneratedFlickHoldSections(HitObject[] objects, IBeatmap beatmap)
        {
            double lastHoldTime = double.NegativeInfinity;

            for (int i = 0; i < objects.Length - 3; i++)
            {
                HitObject anchor = objects[i];
                ConversionPlan anchorPlan = plans[anchor];
                if (!anchorPlan.Emit || hasDuration(anchor) || generatedFlickHoldDurations.ContainsKey(anchor))
                    continue;

                TimingControlPoint timing = beatmap.ControlPointInfo.TimingPointAt(anchor.StartTime);
                double beatLength = validBeatLength(timing.BeatLength);
                if (anchor.StartTime - lastHoldTime < beatLength * 12)
                    continue;

                double beatPosition = (anchor.StartTime - timing.Time) / beatLength;
                long nearestBeat = (long)Math.Round(beatPosition);
                bool strongBeat = Math.Abs(beatPosition - nearestBeat) <= 0.08
                                  && ((nearestBeat % 4) + 4) % 4 == 0;
                bool phraseStart = i == 0 || anchor.StartTime - objects[i - 1].StartTime >= beatLength * 1.5;
                if (!strongBeat && !phraseStart)
                    continue;

                var accompaniment = new List<HitObject>();
                double previousTime = anchor.StartTime;

                for (int j = i + 1; j < objects.Length && accompaniment.Count < 4; j++)
                {
                    HitObject candidate = objects[j];
                    double elapsed = candidate.StartTime - anchor.StartTime;
                    if (elapsed > beatLength * 4)
                        break;

                    if (!plans[candidate].Emit || hasDuration(candidate)
                        || objects.Count(otherObject => Math.Abs(otherObject.StartTime - candidate.StartTime) < 0.01 && plans[otherObject].Emit) != 1)
                        break;

                    double interval = candidate.StartTime - previousTime;
                    if (interval < 240 || interval > beatLength * 1.1)
                        break;

                    accompaniment.Add(candidate);
                    previousTime = candidate.StartTime;
                }

                if (accompaniment.Count < 3)
                    continue;

                double duration = accompaniment[^1].StartTime - anchor.StartTime;
                if (duration < beatLength * 1.5)
                    continue;

                StickSide playingSide = other(anchorPlan.Side);
                generatedFlickHoldDurations[anchor] = duration;

                foreach (HitObject note in accompaniment)
                    plans[note] = plans[note] with { Side = playingSide };

                lastHoldTime = anchor.StartTime;
                i += accompaniment.Count;
            }
        }

        private HashSet<HitObject> applySliderAccompaniment(HitObject[] objects, IBeatmap beatmap)
        {
            var coordinatedTaps = new HashSet<HitObject>();
            int eligiblePhraseIndex = 0;

            foreach (HitObject sliderObject in objects.Where(hitObject => plans[hitObject].Emit && hasDuration(hitObject) && !generatedHoldSources.Contains(hitObject)))
            {
                var duration = (IHasDuration)sliderObject;
                ConversionPlan sliderPlan = plans[sliderObject];
                double beatLength = validBeatLength(beatmap.ControlPointInfo.TimingPointAt(sliderObject.StartTime).BeatLength);
                double edgePadding = Math.Min(150, beatLength * 0.25);

                HitObject[] taps = objects.Where(hitObject =>
                                                   plans[hitObject].Emit
                                                   && !convertsToHoldOrSlider(hitObject)
                                                   && plans[hitObject].Side != sliderPlan.Side
                                                   && hitObject.StartTime > sliderObject.StartTime + edgePadding
                                                   && hitObject.StartTime < sliderObject.StartTime + duration.Duration - edgePadding)
                                          .Take(6)
                                          .ToArray();

                if (duration.Duration < beatLength * 1.5 || taps.Length < 2)
                    continue;

                bool selected = eligiblePhraseIndex++ % 3 == 0;
                if (!selected)
                    continue;

                foreach (HitObject tap in taps)
                {
                    double pathProgress = pathProgressAt(sliderObject, duration, tap.StartTime);
                    float angle = SticksHitObject.NormaliseAngle(sliderPlan.Angle + sliderPlan.ArcAngle * (float)pathProgress);
                    plans[tap] = plans[tap] with { Angle = angle };
                    coordinatedTaps.Add(tap);
                }
            }

            return coordinatedTaps;
        }

        private void applyAlternatingStreams(HitObject[] objects, HashSet<HitObject> excludedTaps)
        {
            HitObject[] flicks = objects.Where(hitObject =>
                                                   plans[hitObject].Emit
                                                   && !convertsToHoldOrSlider(hitObject)
                                                   && !excludedTaps.Contains(hitObject)
                                                   && !approachOverlapsSlider(hitObject.StartTime, objects))
                                       .ToArray();
            int eligiblePhraseIndex = 0;

            for (int runStart = 0; runStart < flicks.Length;)
            {
                int runEnd = runStart + 1;
                double previousInterval = double.NaN;

                while (runEnd < flicks.Length)
                {
                    double interval = flicks[runEnd].StartTime - flicks[runEnd - 1].StartTime;
                    bool consistent = double.IsNaN(previousInterval)
                                      || interval >= previousInterval * 0.65
                                      && interval <= previousInterval * 1.5;

                    if (interval <= 0.01 || interval > RAPID_ALTERNATION_THRESHOLD || !consistent)
                        break;

                    previousInterval = interval;
                    runEnd++;
                }

                int runLength = runEnd - runStart;

                if (runLength >= 4)
                {
                    int patternLength = Math.Min(8, runLength);
                    bool selected = eligiblePhraseIndex++ % 3 == 0;

                    if (selected)
                    {
                        HitObject first = flicks[runStart];
                        float anchor = plans[first].Angle;
                        float direction = (stableHash($"stream:{first.StartTime}") & 1) == 0 ? 1 : -1;
                        StickSide startingSide = plans[first].Side;

                        for (int offset = 0; offset < patternLength; offset++)
                        {
                            HitObject flick = flicks[runStart + offset];
                            float progress = patternLength == 1 ? 0 : (float)offset / (patternLength - 1);
                            plans[flick] = plans[flick] with
                            {
                                Side = offset % 2 == 0 ? startingSide : other(startingSide),
                                Angle = SticksHitObject.NormaliseAngle(anchor + direction * 30 * progress),
                            };
                        }
                    }
                }

                runStart = runEnd;
            }
        }

        private void enforceRapidAlternation(HitObject[] objects)
        {
            HitObject[] heads = objects.Where(hitObject => plans[hitObject].Emit)
                                       .OrderBy(hitObject => hitObject.StartTime)
                                       .ToArray();

            for (int i = 1; i < heads.Length; i++)
            {
                HitObject previous = heads[i - 1];
                HitObject current = heads[i];
                double interval = current.StartTime - previous.StartTime;
                if (interval <= 0.01 || interval > RAPID_ALTERNATION_THRESHOLD)
                    continue;

                ConversionPlan previousPlan = plans[previous];
                ConversionPlan currentPlan = plans[current];
                if (currentPlan.Side != previousPlan.Side)
                    continue;

                StickSide alternate = other(currentPlan.Side);

                if (!convertsToHoldOrSlider(current) && canMoveHeadTo(current, alternate, objects))
                {
                    plans[current] = currentPlan with { Side = alternate };
                    continue;
                }

                if (convertsToHoldOrSlider(current)
                    && !convertsToHoldOrSlider(previous)
                    && canMoveHeadTo(previous, alternate, objects))
                {
                    plans[previous] = previousPlan with { Side = alternate };
                    continue;
                }

                // Do not leave an unplayable rapid re-flick when both sticks are committed.
                // Duration heads are preserved; a neighbouring standalone flick is expendable.
                if (!convertsToHoldOrSlider(current) && isOnlyHeadAtTimestamp(current, objects))
                    plans[current] = currentPlan with { Emit = false };
                else if (!convertsToHoldOrSlider(previous) && isOnlyHeadAtTimestamp(previous, objects))
                    plans[previous] = previousPlan with { Emit = false };
            }
        }

        private bool canMoveHeadTo(HitObject hitObject, StickSide side, HitObject[] objects) =>
            !sideOccupiedAt(side, hitObject.StartTime, objects)
            && !objects.Any(candidate =>
                candidate != hitObject
                && plans[candidate].Emit
                && plans[candidate].Side == side
                && Math.Abs(candidate.StartTime - hitObject.StartTime) <= RAPID_ALTERNATION_THRESHOLD);

        private bool isOnlyHeadAtTimestamp(HitObject hitObject, HitObject[] objects) =>
            objects.Count(candidate =>
                plans[candidate].Emit
                && Math.Abs(candidate.StartTime - hitObject.StartTime) < 0.01) == 1;

        private bool sideOccupiedAt(StickSide side, double time, HitObject[] objects) =>
            objects.Any(hitObject =>
                plans[hitObject].Emit
                && plans[hitObject].Side == side
                && hitObject is IHasDuration { Duration: > 0 } duration
                && hitObject.StartTime < time
                && hitObject.StartTime + duration.Duration >= time)
            || generatedFlickHoldDurations.Any(hold =>
                plans[hold.Key].Side == side
                && hold.Key.StartTime < time
                && hold.Key.StartTime + hold.Value >= time);

        private bool approachOverlapsSlider(double time, HitObject[] objects) =>
            objects.Any(hitObject =>
                plans[hitObject].Emit
                && hitObject is IHasDuration { Duration: > 0 } duration
                && time > hitObject.StartTime
                && time - VISIBILITY_PREEMPT < hitObject.StartTime + duration.Duration)
            || generatedFlickHoldDurations.Any(hold =>
                time > hold.Key.StartTime
                && time - VISIBILITY_PREEMPT < hold.Key.StartTime + hold.Value);

        private static bool hasDuration(HitObject hitObject) => hitObject is IHasDuration { Duration: > 0 };

        private bool convertsToHoldOrSlider(HitObject hitObject) => hasDuration(hitObject) || generatedFlickHoldDurations.ContainsKey(hitObject);

        private bool isHoldSource(HitObject hitObject) => generatedHoldSources.Contains(hitObject) || isNativeHoldSource(hitObject);

        private static bool isNativeHoldSource(HitObject hitObject)
        {
            string typeName = hitObject.GetType().Name;
            return typeName.Contains("Hold", StringComparison.OrdinalIgnoreCase)
                   || typeName.Contains("Spinner", StringComparison.OrdinalIgnoreCase);
        }

        private static double pathProgressAt(HitObject hitObject, IHasDuration duration, double time)
        {
            int spanCount = hitObject is IHasRepeats repeats ? repeats.SpanCount() : 1;
            double spanDuration = duration.Duration / spanCount;
            double elapsed = Math.Clamp(time - hitObject.StartTime, 0, duration.Duration);
            int spanIndex = Math.Min(spanCount - 1, (int)(elapsed / Math.Max(1, spanDuration)));
            double spanProgress = Math.Clamp((elapsed - spanIndex * spanDuration) / Math.Max(1, spanDuration), 0, 1);
            return spanIndex % 2 == 0 ? spanProgress : 1 - spanProgress;
        }

        private static float sourceAngle(HitObject hitObject, int index)
        {
            if (hitObject is IHasPosition positioned)
            {
                Vector2 offset = positioned.Position - STANDARD_CENTRE;
                if (offset.LengthSquared > 1)
                    return SticksHitObject.NormaliseAngle(MathF.Atan2(offset.Y, offset.X) * 180 / MathF.PI);
            }

            return stableHash($"angle:{index}:{hitObject.StartTime}") % 8 * 45;
        }

        private static float generatedArc(HitObject hitObject, IBeatmap beatmap, float startAngle, int index)
        {
            var duration = (IHasDuration)hitObject;
            double beatLength = validBeatLength(beatmap.ControlPointInfo.TimingPointAt(hitObject.StartTime).BeatLength);
            int spanCount = hitObject is IHasRepeats repeats ? repeats.SpanCount() : 1;
            double spanDuration = duration.Duration / spanCount;
            double beats = Math.Max(0.5, spanDuration / beatLength);
            int direction = sourceDirection(hitObject, startAngle, index);
            float magnitude = (float)Math.Clamp(Math.Round(beats * 45 / 15) * 15, 45, 270);

            // Preserve the source duration and shorten only the generated path when its musical
            // 45-degrees-per-beat length would move too quickly. Do not apply a minimum arc after
            // this cap: that previously forced short sliders back above the intended speed limit.
            float maximumMagnitude = (float)(spanDuration / 1000 * MAX_GENERATED_SLIDER_ANGULAR_VELOCITY);
            magnitude = Math.Min(magnitude, maximumMagnitude);
            return direction * magnitude;
        }

        private static int sourceDirection(HitObject hitObject, float startAngle, int index)
        {
            if (hitObject is IHasPath path && hitObject is IHasPosition positioned)
            {
                Vector2 endOffset = positioned.Position + path.Path.PositionAt(1) - STANDARD_CENTRE;
                if (endOffset.LengthSquared > 1)
                {
                    float endAngle = SticksHitObject.NormaliseAngle(MathF.Atan2(endOffset.Y, endOffset.X) * 180 / MathF.PI);
                    float delta = SticksHitObject.DeltaAngle(startAngle, endAngle);
                    if (Math.Abs(delta) > 5)
                        return Math.Sign(delta);
                }
            }

            return (stableHash($"direction:{index}:{hitObject.StartTime}") & 1) == 0 ? 1 : -1;
        }

        private static StickSide other(StickSide side) => side == StickSide.Left ? StickSide.Right : StickSide.Left;

        private static double validBeatLength(double beatLength) => double.IsFinite(beatLength) && beatLength > 0 ? beatLength : 500;

        private static uint stableHash(string value)
        {
            uint hash = 2166136261;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash;
        }

        private readonly record struct ConversionPlan(StickSide Side, float Angle, float ArcAngle, bool Emit);
    }
}
