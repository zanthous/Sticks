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
        double TimingPrecision);

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

        public static SticksDifficultyBreakdown Calculate(IEnumerable<SticksHitObject> hitObjects, double clockRate, float overallDifficulty)
        {
            SticksHitObject[] objects = hitObjects.OrderBy(hitObject => hitObject.StartTime).ToArray();
            if (objects.Length == 0)
                return default;

            clockRate = double.IsFinite(clockRate) && clockRate > 0 ? clockRate : 1;
            overallDifficulty = float.IsFinite(overallDifficulty) ? overallDifficulty : SticksDifficultyScaling.REFERENCE_OVERALL_DIFFICULTY;

            var mechanical = new PerSideStrainAccumulator(mechanical_decay, clockRate);
            var reading = new ScalarStrainAccumulator(reading_decay, clockRate);
            var control = new PerSideStrainAccumulator(control_decay, clockRate);
            var coordination = new ScalarStrainAccumulator(coordination_decay, clockRate);

            var mechanicalStrains = new List<double>();
            var readingStrains = new List<double>();
            var controlStrains = new List<double>();
            var coordinationStrains = new List<double>();

            var previousBySide = new Dictionary<StickSide, PreviousSideObject>();
            var activeTracking = new List<ActiveTrackingObject>();
            var readingHistory = new List<PatternGroup>();

            double fullGreatWindow = 2 * SticksDifficultyScaling.GreatWindowFor(overallDifficulty) / clockRate;

            for (int objectIndex = 0; objectIndex < objects.Length;)
            {
                double timestamp = objects[objectIndex].StartTime;
                int groupEnd = objectIndex + 1;

                while (groupEnd < objects.Length && Math.Abs(objects[groupEnd].StartTime - timestamp) <= simultaneous_epsilon)
                    groupEnd++;

                SticksHitObject[] group = objects[objectIndex..groupEnd];
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

                objectIndex = groupEnd;
            }

            double mechanicalRating = Math.Sqrt(harmonicDifficulty(mechanicalStrains, mechanical_harmonic_scale));
            double readingRating = Math.Sqrt(harmonicDifficulty(readingStrains, reading_harmonic_scale)) * 0.85;
            double controlRating = Math.Sqrt(harmonicDifficulty(controlStrains, control_harmonic_scale)) * 1.55;
            double coordinationRating = Math.Sqrt(harmonicDifficulty(coordinationStrains, coordination_harmonic_scale)) * 0.9;

            double combined = pNorm(skill_norm_exponent, mechanicalRating, readingRating, controlRating, coordinationRating);
            double angularPrecision = median(objects.Select(hitObject =>
                SticksDifficultyScaling.AngularPrecisionMultiplier(hitObject.PrimaryHitAngle, hitObject.SecondaryHitAngle)));
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
                timingPrecision);
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

        private static double calculateReadingImpulse(SticksHitObject[] group, double timestamp,
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

            double novelty = 0;

            if (previous.HasValue)
            {
                foreach (SticksHitObject current in group)
                {
                    float signedStep = nearestSignedStep(previous.Value.Angles, current.Angle);
                    double objectNovelty = Math.Pow(Math.Abs(signedStep) / 180, 0.7);

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

                    novelty += objectNovelty;
                }

                novelty /= group.Length;
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

            double chordBonus = group.Length > 1 ? 0.12 : 0;
            double impulse = (0.3 + novelty * 0.95 + regionComplexity + objectTypeBonus + chordBonus) * density;

            bool followsActiveSliderArc = group.Any(current => activeTracking.Any(active =>
                active.Object is SticksSlider activeSlider
                && active.Object.Side != current.Side
                && Math.Abs(SticksHitObject.DeltaAngle(activeSlider.AngleAt(timestamp), current.Angle)) <= 30));

            if (followsActiveSliderArc)
                impulse *= 0.85;

            return impulse;
        }

        private static double calculateCoordinationImpulse(SticksHitObject[] group, double timestamp,
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

        private static double harmonicDifficulty(IEnumerable<double> strains, double harmonicScale)
        {
            double difficulty = 0;
            int index = 0;

            foreach (double strain in strains.Where(value => value > 0).OrderByDescending(value => value))
            {
                double weight = (1 + harmonicScale / (1 + index))
                                / (Math.Pow(index, 0.9) + 1 + harmonicScale / (1 + index));
                difficulty += strain * weight;
                index++;
            }

            return difficulty;
        }

        private static double pNorm(double exponent, params double[] values) =>
            Math.Pow(values.Sum(value => Math.Pow(Math.Max(0, value), exponent)), 1 / exponent);

        private static double median(IEnumerable<double> values)
        {
            double[] sorted = values.OrderBy(value => value).ToArray();
            if (sorted.Length == 0)
                return 1;

            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
        }

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

            private readonly record struct SideStrain(double Value, double LastTime);
        }
    }
}
