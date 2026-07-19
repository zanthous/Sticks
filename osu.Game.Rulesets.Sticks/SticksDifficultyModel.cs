// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks
{
    /// <summary>
    /// A diagnostic breakdown of the independent skills used for Sticks star difficulty.
    /// </summary>
    public readonly record struct SticksDifficultyBreakdown(
        double StarRating,
        double Mechanical,
        double Reading,
        double Control,
        double Coordination,
        double AngularPrecision,
        double TimingPrecision,
        double MechanicalDifficultStrainCount,
        double ReadingDifficultStrainCount,
        double ControlDifficultStrainCount,
        double CoordinationDifficultStrainCount);

    internal static class SticksDifficultyModel
    {
        private const double simultaneous_epsilon = 0.01;
        private const double skill_norm_exponent = 3.3;

        private const double mechanical_decay = 0.3;
        private const double reading_decay = 0.8;
        private const double control_decay = 0.45;
        private const double coordination_decay = 0.55;

        private const double mechanical_harmonic_scale = 20;
        private const double reading_harmonic_scale = 1;
        private const double control_harmonic_scale = 5;
        private const double coordination_harmonic_scale = 5;

        // Large angular jumps remain demanding even when they repeat between two predictable
        // regions. Broadly scattered patterns add a smaller visual-search demand on top.
        private const double large_jump_search_scale = 3.3;
        private const double broad_region_search_scale = 2.5;

        /// <summary>
        /// Calculates difficulty for hit objects already ordered by start time.
        /// </summary>
        public static SticksDifficultyBreakdown CalculateOrdered(SticksHitObject[] objects, double clockRate, float overallDifficulty)
        {
            if (objects.Length == 0)
                return default;

            var state = new IncrementalState(clockRate, overallDifficulty);

            foreach (SticksHitObject hitObject in objects)
                state.Append(hitObject);

            return state.GetBreakdown();
        }

        /// <summary>
        /// Processes a chronologically-growing beatmap without replaying its completed prefix.
        /// The current simultaneous group is reversible because lazer requests an attribute set
        /// after each individual object, including each member of a chord at the same timestamp.
        /// </summary>
        internal sealed class IncrementalState
        {
            private readonly double clockRate;
            private readonly float overallDifficulty;
            private readonly double fullGreatWindow;

            private PerSideStrainAccumulator mechanical;
            private ScalarStrainAccumulator reading;
            private PerSideStrainAccumulator control;
            private ScalarStrainAccumulator coordination;

            private readonly RollbackableRankedValues mechanicalStrains = RollbackableRankedValues.CreateStrains();
            private readonly RollbackableRankedValues readingStrains = RollbackableRankedValues.CreateStrains();
            private readonly RollbackableRankedValues controlStrains = RollbackableRankedValues.CreateStrains();
            private readonly RollbackableRankedValues coordinationStrains = RollbackableRankedValues.CreateStrains();

            private readonly Dictionary<StickSide, PreviousSideObject> previousBySide = new Dictionary<StickSide, PreviousSideObject>();
            private readonly List<ActiveTrackingObject> activeTracking = new List<ActiveTrackingObject>();
            private readonly List<PatternGroup> readingHistory = new List<PatternGroup>();
            private readonly RollbackableRankedValues angularPrecisionValues = RollbackableRankedValues.CreateAscending();
            private readonly List<SticksHitObject> currentGroup = new List<SticksHitObject>();

            private GroupCheckpoint currentGroupCheckpoint;
            private double currentGroupTimestamp;

            public int ObjectCount { get; private set; }

            /// <summary>
            /// Number of object evaluations performed, including the small re-evaluation required
            /// when a later object expands the current simultaneous group.
            /// </summary>
            public int ObjectEvaluationCount { get; private set; }

            public IncrementalState(double clockRate, float overallDifficulty)
            {
                this.clockRate = double.IsFinite(clockRate) && clockRate > 0 ? clockRate : 1;
                this.overallDifficulty = float.IsFinite(overallDifficulty) ? overallDifficulty : SticksDifficultyScaling.REFERENCE_OVERALL_DIFFICULTY;
                fullGreatWindow = 2 * SticksDifficultyScaling.GreatWindowFor(this.overallDifficulty) / this.clockRate;

                mechanical = new PerSideStrainAccumulator(mechanical_decay, this.clockRate);
                reading = new ScalarStrainAccumulator(reading_decay, this.clockRate);
                control = new PerSideStrainAccumulator(control_decay, this.clockRate);
                coordination = new ScalarStrainAccumulator(coordination_decay, this.clockRate);
            }

            public void Append(SticksHitObject hitObject)
            {
                if (hitObject == null)
                    throw new ArgumentNullException(nameof(hitObject));

                bool startsNewGroup = currentGroup.Count == 0
                                      || Math.Abs(hitObject.StartTime - currentGroupTimestamp) > simultaneous_epsilon;

                if (startsNewGroup)
                {
                    if (currentGroup.Count > 0 && hitObject.StartTime < currentGroupTimestamp)
                        throw new ArgumentException("Difficulty objects must be appended in chronological order.", nameof(hitObject));

                    currentGroupTimestamp = hitObject.StartTime;
                    currentGroup.Clear();
                    currentGroupCheckpoint = captureCheckpoint();
                }
                else
                {
                    restoreCheckpoint(currentGroupCheckpoint);
                }

                currentGroup.Add(hitObject);
                ObjectCount++;
                processCurrentGroup();
            }

            public SticksDifficultyBreakdown GetBreakdown()
            {
                if (ObjectCount == 0)
                    return default;

                double mechanicalDifficulty = mechanicalStrains.HarmonicDifficulty(mechanical_harmonic_scale);
                double readingDifficulty = readingStrains.HarmonicDifficulty(reading_harmonic_scale);
                double controlDifficulty = controlStrains.HarmonicDifficulty(control_harmonic_scale);
                double coordinationDifficulty = coordinationStrains.HarmonicDifficulty(coordination_harmonic_scale);

                double mechanicalRating = Math.Sqrt(mechanicalDifficulty);
                double readingRating = Math.Sqrt(readingDifficulty) * 0.85;
                double controlRating = Math.Sqrt(controlDifficulty) * 1.55;
                double coordinationRating = Math.Sqrt(coordinationDifficulty) * 0.9;

                double combined = pNorm(skill_norm_exponent, mechanicalRating, readingRating, controlRating, coordinationRating);
                double angularPrecision = angularPrecisionValues.Median();
                double timingPrecision = SticksDifficultyScaling.OverallDifficultyMultiplier(overallDifficulty);
                double calibratedBaseStars = SticksDifficultyScaling.CalibrateStarRating(combined * timingPrecision);
                double angularAdjustment = SticksDifficultyScaling.AngularPrecisionStarAdjustment(calibratedBaseStars, angularPrecision);
                double stars = Math.Clamp(calibratedBaseStars + angularAdjustment, 0, 30);

                return new SticksDifficultyBreakdown(
                    stars,
                    mechanicalRating,
                    readingRating,
                    controlRating,
                    coordinationRating,
                    angularPrecision,
                    timingPrecision,
                    mechanicalStrains.CountTopWeightedStrains(mechanicalDifficulty),
                    readingStrains.CountTopWeightedStrains(readingDifficulty),
                    controlStrains.CountTopWeightedStrains(controlDifficulty),
                    coordinationStrains.CountTopWeightedStrains(coordinationDifficulty));
            }

            private void processCurrentGroup()
            {
                double timestamp = currentGroupTimestamp;
                IReadOnlyList<SticksHitObject> group = currentGroup;

                activeTracking.RemoveAll(active => active.EndTime <= timestamp + simultaneous_epsilon);
                readingHistory.RemoveAll(pattern => effectiveInterval(timestamp - pattern.Time, clockRate) > 2000);

                var mechanicalImpulses = new Dictionary<StickSide, double>();
                var controlImpulses = new Dictionary<StickSide, double>();

                foreach (SticksHitObject current in group)
                {
                    double impulse = mechanicalImpulse(current, timestamp, previousBySide, fullGreatWindow, clockRate);
                    mechanicalImpulses[current.Side] = Math.Max(mechanicalImpulses.GetValueOrDefault(current.Side), impulse);

                    double continuousImpulse = controlImpulse(current, clockRate);
                    if (continuousImpulse > 0)
                        controlImpulses[current.Side] = Math.Max(controlImpulses.GetValueOrDefault(current.Side), continuousImpulse);
                }

                mechanicalStrains.Add(mechanical.Process(timestamp, mechanicalImpulses));

                if (controlImpulses.Count > 0)
                    controlStrains.Add(control.Process(timestamp, controlImpulses));

                double readingImpulse = calculateReadingImpulse(group, timestamp, readingHistory, activeTracking, clockRate);
                readingStrains.Add(reading.Process(timestamp, readingImpulse * 2.5));

                double coordinationImpulse = calculateCoordinationImpulse(group, timestamp, activeTracking);
                if (coordinationImpulse > 0)
                    coordinationStrains.Add(coordination.Process(timestamp, coordinationImpulse));

                foreach (IGrouping<StickSide, SticksHitObject> sideGroup in group.GroupBy(hitObject => hitObject.Side))
                {
                    SticksHitObject latestEnding = sideGroup.OrderByDescending(endTimeOf).First();
                    previousBySide[sideGroup.Key] = new PreviousSideObject(endTimeOf(latestEnding));
                }

                foreach (SticksHitObject current in group)
                {
                    if (current is SticksSlider slider)
                    {
                        double seconds = Math.Max(0.025, slider.Duration / 1000 / clockRate);
                        activeTracking.Add(new ActiveTrackingObject(slider, slider.EndTime, slider.TotalAngularDistance / seconds));
                    }
                    else if (current is SticksHold hold)
                    {
                        activeTracking.Add(new ActiveTrackingObject(hold, hold.EndTime, 0));
                    }
                }

                readingHistory.Add(new PatternGroup(
                    timestamp,
                    group.Select(hitObject => hitObject.Angle).ToArray(),
                    group.Select(kindOf).ToArray()));

                foreach (SticksHitObject hitObject in group)
                {
                    angularPrecisionValues.Add(
                        SticksDifficultyScaling.AngularPrecisionMultiplier(hitObject.PrimaryHitAngle, hitObject.SecondaryHitAngle));
                }

                ObjectEvaluationCount += group.Count;
            }

            private GroupCheckpoint captureCheckpoint() => new GroupCheckpoint(
                mechanical.Clone(),
                reading.Clone(),
                control.Clone(),
                coordination.Clone(),
                mechanicalStrains.Checkpoint,
                readingStrains.Checkpoint,
                controlStrains.Checkpoint,
                coordinationStrains.Checkpoint,
                angularPrecisionValues.Checkpoint,
                new Dictionary<StickSide, PreviousSideObject>(previousBySide),
                activeTracking.ToArray(),
                readingHistory.ToArray());

            private void restoreCheckpoint(GroupCheckpoint checkpoint)
            {
                mechanical.CopyFrom(checkpoint.Mechanical);
                reading.CopyFrom(checkpoint.Reading);
                control.CopyFrom(checkpoint.Control);
                coordination.CopyFrom(checkpoint.Coordination);
                mechanicalStrains.RollbackTo(checkpoint.MechanicalStrainCount);
                readingStrains.RollbackTo(checkpoint.ReadingStrainCount);
                controlStrains.RollbackTo(checkpoint.ControlStrainCount);
                coordinationStrains.RollbackTo(checkpoint.CoordinationStrainCount);
                angularPrecisionValues.RollbackTo(checkpoint.AngularPrecisionCount);

                previousBySide.Clear();
                foreach ((StickSide side, PreviousSideObject previous) in checkpoint.PreviousBySide)
                    previousBySide.Add(side, previous);

                activeTracking.Clear();
                activeTracking.AddRange(checkpoint.ActiveTracking);
                readingHistory.Clear();
                readingHistory.AddRange(checkpoint.ReadingHistory);
            }

            private sealed record GroupCheckpoint(
                PerSideStrainAccumulator Mechanical,
                ScalarStrainAccumulator Reading,
                PerSideStrainAccumulator Control,
                ScalarStrainAccumulator Coordination,
                int MechanicalStrainCount,
                int ReadingStrainCount,
                int ControlStrainCount,
                int CoordinationStrainCount,
                int AngularPrecisionCount,
                Dictionary<StickSide, PreviousSideObject> PreviousBySide,
                ActiveTrackingObject[] ActiveTracking,
                PatternGroup[] ReadingHistory);
        }

        private static double mechanicalImpulse(SticksHitObject current, double timestamp,
                                                IReadOnlyDictionary<StickSide, PreviousSideObject> previousBySide,
                                                double fullGreatWindow, double clockRate)
        {
            double impulse;

            if (!previousBySide.TryGetValue(current.Side, out PreviousSideObject previous))
            {
                impulse = 0.35;
            }
            else
            {
                double gap = Math.Max(25, effectiveInterval(timestamp - previous.EndTime, clockRate));

                // Match osu!standard's OD-aware speed cap. Wide windows make extremely short gaps
                // slightly easier without globally applying the inverse hit-window ratio.
                gap /= Math.Clamp((gap / Math.Max(1, fullGreatWindow)) / 0.93, 0.92, 1);

                const double high_speed_boundary = 60000.0 / 140 / 2; // Same-stick gap in a 140 BPM alternating 1/4 stream.
                double speedBonus = gap < high_speed_boundary
                    ? 0.75 * Math.Pow((high_speed_boundary - gap) / 50, 2)
                    : 0;

                impulse = 250 / Math.Max(25, gap) * (1 + speedBonus);
            }

            if (current is SticksSlider)
                impulse *= 1.1;
            else if (current is SticksHold)
                impulse *= 1.03;

            return impulse;
        }

        private static double controlImpulse(SticksHitObject current, double clockRate)
        {
            if (current is SticksHold)
                return 0.15;

            if (current is not SticksSlider slider)
                return 0;

            double durationSeconds = Math.Max(0.025, slider.Duration / 1000 / clockRate);
            double angularVelocity = slider.TotalAngularDistance / durationSeconds;
            double motion = Math.Pow(angularVelocity / 120, 2.2);
            double shortestSegmentSeconds = Enumerable.Range(0, slider.SegmentCount)
                                                      .Select(slider.SegmentDurationAt)
                                                      .DefaultIfEmpty(slider.Duration)
                                                      .Min() / 1000 / clockRate;
            int reversalCount = 0;

            for (int segment = 1; segment < slider.SegmentCount; segment++)
            {
                if (Math.Sign(slider.SegmentArcAngleAt(segment - 1)) != Math.Sign(slider.SegmentArcAngleAt(segment)))
                    reversalCount++;
            }

            double reversal = reversalCount == 0
                ? 0
                : 0.3 * Math.Log2(reversalCount + 1)
                      * Math.Pow(0.4 / Math.Max(0.1, shortestSegmentSeconds), 0.6)
                      * Math.Pow(Math.Max(angularVelocity, 60) / 120, 0.35);
            double endurance = 1 + 0.06 * Math.Log2(1 + durationSeconds);

            return (0.3 + motion + reversal) * endurance;
        }

        private static double calculateReadingImpulse(IReadOnlyList<SticksHitObject> group, double timestamp,
                                                      IReadOnlyList<PatternGroup> history,
                                                      IReadOnlyList<ActiveTrackingObject> activeTracking,
                                                      double clockRate)
        {
            PatternGroup? previous = history.Count > 0 ? history[^1] : null;
            PatternGroup? twoBack = history.Count > 1 ? history[^2] : null;
            double deltaTime = previous.HasValue ? effectiveInterval(timestamp - previous.Value.Time, clockRate) : double.PositiveInfinity;
            double density = previous.HasValue
                ? Math.Clamp(Math.Pow(125 / Math.Max(50, deltaTime), 0.75), 0.2, 3)
                : 0.5;

            double jumpDemand = 0;
            double novelty = 0;

            if (previous.HasValue)
            {
                foreach (SticksHitObject current in group)
                {
                    float signedStep = nearestSignedStep(previous.Value.Angles, current.Angle);
                    double objectJumpDemand = Math.Pow(Math.Abs(signedStep) / 180, 0.7);
                    double objectNovelty = objectJumpDemand;

                    if (twoBack.HasValue && twoBack.Value.Angles.Any(angle =>
                            Math.Abs(SticksHitObject.DeltaAngle(angle, current.Angle)) <= 15))
                    {
                        objectNovelty *= 0.65;
                    }

                    if (twoBack.HasValue)
                    {
                        float priorStep = nearestSignedStep(twoBack.Value.Angles, previous.Value.Angles[0]);
                        if (Math.Abs(Math.Abs(signedStep) - Math.Abs(priorStep)) <= 15)
                            objectNovelty *= 0.75;
                    }

                    jumpDemand += objectJumpDemand;
                    novelty += objectNovelty;
                }

                jumpDemand /= group.Count;
                novelty /= group.Count;
            }

            int distinctRegions = history.SelectMany(pattern => pattern.Angles)
                                         .Select(angle => (int)Math.Floor((SticksHitObject.NormaliseAngle(angle) + 22.5f) / 45) % 8)
                                         .Distinct()
                                         .Count();
            double regionComplexity = Math.Clamp((distinctRegions - 1) / 5.0, 0, 1) * 0.3;

            double objectTypeBonus = 0;
            SticksSlider slider = group.OfType<SticksSlider>().OrderByDescending(current => current.SegmentCount).FirstOrDefault();
            if (slider != null)
                objectTypeBonus = 0.25 + 0.08 * Math.Log2(slider.SegmentCount);
            else if (group.Any(current => current is SticksHold))
                objectTypeBonus = 0.08;

            if (previous.HasValue)
            {
                ObjectKind[] currentKinds = group.Select(kindOf).Distinct().ToArray();
                if (currentKinds.Any(kind => !previous.Value.Kinds.Contains(kind)))
                    objectTypeBonus += 0.08;
            }

            double chordBonus = group.Count > 1 ? 0.12 : 0;
            double spatialSearchMultiplier = SpatialSearchMultiplier(jumpDemand, novelty, distinctRegions);
            double impulse = (0.3 + novelty * 0.95 + regionComplexity + objectTypeBonus + chordBonus)
                             * density
                             * spatialSearchMultiplier;

            bool followsActiveSliderArc = group.Any(current => activeTracking.Any(active =>
                active.Object is SticksSlider activeSlider
                && active.Object.Side != current.Side
                && Math.Abs(SticksHitObject.DeltaAngle(activeSlider.AngleAt(timestamp), current.Angle)) <= 30));

            if (followsActiveSliderArc)
                impulse *= 0.85;

            return impulse;
        }

        internal static double SpatialSearchMultiplier(double jumpDemand, double novelty, int distinctRegions)
        {
            jumpDemand = Math.Clamp(jumpDemand, 0, 1);
            novelty = Math.Clamp(novelty, 0, jumpDemand);
            double regionDiversity = Math.Clamp((distinctRegions - 2) / 4.0, 0, 1);
            double broadSearch = novelty * regionDiversity * regionDiversity;
            return 1
                   + large_jump_search_scale * jumpDemand
                   + broad_region_search_scale * broadSearch;
        }

        private static double calculateCoordinationImpulse(IReadOnlyList<SticksHitObject> group, double timestamp,
                                                           IReadOnlyList<ActiveTrackingObject> activeTracking)
        {
            double impulse = 0;
            bool hasLeft = group.Any(current => current.Side == StickSide.Left);
            bool hasRight = group.Any(current => current.Side == StickSide.Right);

            if (hasLeft && hasRight)
                impulse += 0.45;

            if (group.Select(kindOf).Distinct().Count() > 1)
                impulse += 0.15;

            foreach (IGrouping<StickSide, SticksHitObject> sideGroup in group.GroupBy(current => current.Side))
            {
                if (sideGroup.Count() > 1)
                    impulse += 2 * (sideGroup.Count() - 1);
            }

            foreach (SticksHitObject current in group)
            {
                foreach (ActiveTrackingObject active in activeTracking)
                {
                    if (active.Object.Side == current.Side)
                    {
                        impulse += 2.5;
                        continue;
                    }

                    if (active.Object is SticksSlider activeSlider)
                    {
                        double overlap = 0.35 + 0.1 * Math.Min(2, active.AngularVelocity / 120);
                        if (Math.Abs(SticksHitObject.DeltaAngle(activeSlider.AngleAt(timestamp), current.Angle)) <= 30)
                            overlap *= 0.85;

                        impulse += overlap;
                    }
                    else if (active.Object is SticksHold)
                    {
                        impulse += 0.25;
                    }
                }
            }

            return impulse;
        }

        private static float nearestSignedStep(IEnumerable<float> fromAngles, float toAngle) =>
            fromAngles.Select(angle => SticksHitObject.DeltaAngle(angle, toAngle))
                      .OrderBy(Math.Abs)
                      .FirstOrDefault();

        private static double pNorm(double exponent, params double[] values) =>
            Math.Pow(values.Sum(value => Math.Pow(Math.Max(0, value), exponent)), 1 / exponent);

        private static double effectiveInterval(double interval, double clockRate) => interval / clockRate;

        private static double endTimeOf(SticksHitObject hitObject) => hitObject switch
        {
            SticksSlider slider => slider.EndTime,
            SticksHold hold => hold.EndTime,
            _ => hitObject.StartTime,
        };

        private static ObjectKind kindOf(SticksHitObject hitObject) => hitObject switch
        {
            SticksSlider => ObjectKind.Slider,
            SticksHold => ObjectKind.Hold,
            _ => ObjectKind.Flick,
        };

        private enum ObjectKind
        {
            Flick,
            Slider,
            Hold,
        }

        private readonly record struct PreviousSideObject(double EndTime);
        private readonly record struct PatternGroup(double Time, float[] Angles, ObjectKind[] Kinds);
        private readonly record struct ActiveTrackingObject(SticksHitObject Object, double EndTime, double AngularVelocity);

        /// <summary>
        /// An AVL order-statistics tree with a chronological insertion log. Checkpoints roll back
        /// simultaneous-group contributions in LIFO order without shifting a sorted array.
        /// </summary>
        internal sealed class RollbackableRankedValues
        {
            private readonly List<RankedKey> chronologicalInsertions = new List<RankedKey>();
            private readonly List<double> harmonicWeights = new List<double>();
            private readonly bool descending;
            private readonly bool positiveOnly;
            private Node root;
            private long nextInsertionId;
            private double cachedHarmonicScale = double.NaN;

            public int Checkpoint => chronologicalInsertions.Count;
            public int Count => sizeOf(root);
            public int TreeHeight => heightOf(root);
            public long MutationComparisonCount { get; private set; }
            public int LastHarmonicVisitCount { get; private set; }
            public int LastSelectionVisitCount { get; private set; }
            public int HarmonicWeightComputationCount { get; private set; }

            private RollbackableRankedValues(bool descending, bool positiveOnly)
            {
                this.descending = descending;
                this.positiveOnly = positiveOnly;
            }

            public static RollbackableRankedValues CreateStrains() => new RollbackableRankedValues(true, true);

            public static RollbackableRankedValues CreateAscending() => new RollbackableRankedValues(false, false);

            public void Add(double value)
            {
                // Preserve the old harmonic path's Where(value > 0) semantics, including its
                // exclusion of zero, negative values, and NaN.
                if (positiveOnly && !(value > 0))
                    return;

                var key = new RankedKey(value, nextInsertionId++);
                root = insert(root, key);
                chronologicalInsertions.Add(key);
            }

            public void RollbackTo(int checkpoint)
            {
                if ((uint)checkpoint > (uint)chronologicalInsertions.Count)
                    throw new ArgumentOutOfRangeException(nameof(checkpoint));

                for (int i = chronologicalInsertions.Count - 1; i >= checkpoint; i--)
                    root = remove(root, chronologicalInsertions[i]);

                if (chronologicalInsertions.Count > checkpoint)
                {
                    chronologicalInsertions.RemoveRange(
                        checkpoint,
                        chronologicalInsertions.Count - checkpoint);
                }
            }

            public double HarmonicDifficulty(double harmonicScale)
            {
                ensureHarmonicWeights(harmonicScale);

                // Each insertion changes the absolute rank (and therefore the non-decomposable
                // weight) of every value after it. Keeping the old rating bit-for-bit also means
                // retaining its left-to-right IEEE-754 addition order. A subtree sum or suffix
                // delta would reassociate those additions, so this exact path still visits each
                // ranked value while reusing all already-calculated weights.
                double difficulty = 0;
                int index = 0;
                LastHarmonicVisitCount = 0;
                accumulateHarmonic(root, ref index, ref difficulty);
                return difficulty;
            }

            public double Median()
            {
                if (root == null)
                {
                    LastSelectionVisitCount = 0;
                    return 1;
                }

                int middle = root.Size / 2;
                LastSelectionVisitCount = 0;
                double upper = valueAtRank(root, middle);

                if (root.Size % 2 != 0)
                    return upper;

                double lower = valueAtRank(root, middle - 1);
                return (lower + upper) / 2;
            }

            /// <summary>
            /// Counts strains relative to the top strain using osu!standard's difficult-strain
            /// weighting. This is consumed by its performance miss penalty, where a miss on a
            /// map with only a few difficult moments is more significant than one on a map with
            /// many similarly difficult moments.
            /// </summary>
            public double CountTopWeightedStrains(double difficultyValue)
            {
                if (root == null)
                    return 0;

                // This is the same DecayWeight and weighting curve used by lazer's StrainSkill.
                double consistentTopStrain = difficultyValue * (1 - 0.9);
                if (consistentTopStrain == 0)
                    return Count;

                double count = 0;
                accumulateTopWeightedStrains(root, consistentTopStrain, ref count);
                return count;
            }

            private void ensureHarmonicWeights(double harmonicScale)
            {
                if (!cachedHarmonicScale.Equals(harmonicScale))
                {
                    cachedHarmonicScale = harmonicScale;
                    harmonicWeights.Clear();
                }

                while (harmonicWeights.Count < Count)
                {
                    int index = harmonicWeights.Count;
                    double weight = (1 + harmonicScale / (1 + index))
                                    / (Math.Pow(index, 0.9) + 1 + harmonicScale / (1 + index));
                    harmonicWeights.Add(weight);
                    HarmonicWeightComputationCount++;
                }
            }

            private void accumulateHarmonic(Node node, ref int index, ref double difficulty)
            {
                if (node == null)
                    return;

                accumulateHarmonic(node.Left, ref index, ref difficulty);
                difficulty += node.Key.Value * harmonicWeights[index++];
                LastHarmonicVisitCount++;
                accumulateHarmonic(node.Right, ref index, ref difficulty);
            }

            private static void accumulateTopWeightedStrains(Node node, double consistentTopStrain, ref double count)
            {
                if (node == null)
                    return;

                accumulateTopWeightedStrains(node.Left, consistentTopStrain, ref count);
                count += 1.1 / (1 + Math.Exp(-10 * (node.Key.Value / consistentTopStrain - 0.88)));
                accumulateTopWeightedStrains(node.Right, consistentTopStrain, ref count);
            }

            private double valueAtRank(Node node, int rank)
            {
                while (node != null)
                {
                    LastSelectionVisitCount++;
                    int leftSize = sizeOf(node.Left);

                    if (rank < leftSize)
                    {
                        node = node.Left;
                    }
                    else if (rank == leftSize)
                    {
                        return node.Key.Value;
                    }
                    else
                    {
                        rank -= leftSize + 1;
                        node = node.Right;
                    }
                }

                throw new ArgumentOutOfRangeException(nameof(rank));
            }

            private Node insert(Node node, RankedKey key)
            {
                if (node == null)
                    return new Node(key);

                if (compare(key, node.Key) < 0)
                    node.Left = insert(node.Left, key);
                else
                    node.Right = insert(node.Right, key);

                return balance(node);
            }

            private Node remove(Node node, RankedKey key)
            {
                if (node == null)
                    throw new InvalidOperationException("Ranked value insertion log was inconsistent with the tree.");

                int comparison = compare(key, node.Key);

                if (comparison < 0)
                {
                    node.Left = remove(node.Left, key);
                }
                else if (comparison > 0)
                {
                    node.Right = remove(node.Right, key);
                }
                else
                {
                    if (node.Left == null)
                        return node.Right;

                    if (node.Right == null)
                        return node.Left;

                    Node successor = minimum(node.Right);
                    node.Key = successor.Key;
                    node.Right = remove(node.Right, successor.Key);
                }

                return balance(node);
            }

            private int compare(RankedKey left, RankedKey right)
            {
                MutationComparisonCount++;
                int valueComparison = descending
                    ? right.Value.CompareTo(left.Value)
                    : left.Value.CompareTo(right.Value);

                return valueComparison != 0
                    ? valueComparison
                    : left.InsertionId.CompareTo(right.InsertionId);
            }

            private static Node balance(Node node)
            {
                update(node);
                int balanceFactor = heightOf(node.Left) - heightOf(node.Right);

                if (balanceFactor > 1)
                {
                    if (heightOf(node.Left.Left) < heightOf(node.Left.Right))
                        node.Left = rotateLeft(node.Left);

                    return rotateRight(node);
                }

                if (balanceFactor < -1)
                {
                    if (heightOf(node.Right.Right) < heightOf(node.Right.Left))
                        node.Right = rotateRight(node.Right);

                    return rotateLeft(node);
                }

                return node;
            }

            private static Node rotateLeft(Node node)
            {
                Node replacement = node.Right;
                node.Right = replacement.Left;
                replacement.Left = node;
                update(node);
                update(replacement);
                return replacement;
            }

            private static Node rotateRight(Node node)
            {
                Node replacement = node.Left;
                node.Left = replacement.Right;
                replacement.Right = node;
                update(node);
                update(replacement);
                return replacement;
            }

            private static Node minimum(Node node)
            {
                while (node.Left != null)
                    node = node.Left;

                return node;
            }

            private static void update(Node node)
            {
                node.Height = Math.Max(heightOf(node.Left), heightOf(node.Right)) + 1;
                node.Size = sizeOf(node.Left) + sizeOf(node.Right) + 1;
            }

            private static int heightOf(Node node) => node?.Height ?? 0;

            private static int sizeOf(Node node) => node?.Size ?? 0;

            private readonly record struct RankedKey(double Value, long InsertionId);

            private sealed class Node
            {
                public RankedKey Key;
                public Node Left;
                public Node Right;
                public int Height = 1;
                public int Size = 1;

                public Node(RankedKey key)
                {
                    Key = key;
                }
            }
        }

        private sealed class ScalarStrainAccumulator
        {
            private readonly double decayBase;
            private readonly double clockRate;
            private bool hasValue;
            private double value;
            private double lastTime;

            public ScalarStrainAccumulator(double decayBase, double clockRate)
            {
                this.decayBase = decayBase;
                this.clockRate = clockRate;
            }

            public double Process(double time, double impulse)
            {
                if (!hasValue)
                {
                    hasValue = true;
                    value = impulse;
                }
                else
                {
                    double decay = Math.Pow(decayBase, effectiveInterval(time - lastTime, clockRate) / 1000);
                    value = value * decay + impulse * (1 - decay);
                }

                lastTime = time;
                return value;
            }

            public ScalarStrainAccumulator Clone()
            {
                var clone = new ScalarStrainAccumulator(decayBase, clockRate);
                clone.CopyFrom(this);
                return clone;
            }

            public void CopyFrom(ScalarStrainAccumulator source)
            {
                hasValue = source.hasValue;
                value = source.value;
                lastTime = source.lastTime;
            }
        }

        private sealed class PerSideStrainAccumulator
        {
            private readonly double decayBase;
            private readonly double clockRate;
            private readonly Dictionary<StickSide, SideStrain> sides = new Dictionary<StickSide, SideStrain>();

            public PerSideStrainAccumulator(double decayBase, double clockRate)
            {
                this.decayBase = decayBase;
                this.clockRate = clockRate;
            }

            public double Process(double time, IReadOnlyDictionary<StickSide, double> impulses)
            {
                foreach ((StickSide side, double impulse) in impulses)
                {
                    if (!sides.TryGetValue(side, out SideStrain state))
                    {
                        sides[side] = new SideStrain(impulse, time);
                        continue;
                    }

                    double decay = decayAt(state, time);
                    sides[side] = new SideStrain(state.Value * decay + impulse * (1 - decay), time);
                }

                double left = valueAt(StickSide.Left, time);
                double right = valueAt(StickSide.Right, time);
                return Math.Sqrt(left * left + right * right);
            }

            private double valueAt(StickSide side, double time) =>
                sides.TryGetValue(side, out SideStrain state) ? state.Value * decayAt(state, time) : 0;

            private double decayAt(SideStrain state, double time) =>
                Math.Pow(decayBase, effectiveInterval(time - state.LastTime, clockRate) / 1000);

            public PerSideStrainAccumulator Clone()
            {
                var clone = new PerSideStrainAccumulator(decayBase, clockRate);
                clone.CopyFrom(this);
                return clone;
            }

            public void CopyFrom(PerSideStrainAccumulator source)
            {
                sides.Clear();
                foreach ((StickSide side, SideStrain state) in source.sides)
                    sides.Add(side, state);
            }

            private readonly record struct SideStrain(double Value, double LastTime);
        }
    }
}
