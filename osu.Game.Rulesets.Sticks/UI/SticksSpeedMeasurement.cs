using System;
using System.Collections.Generic;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// Measures physical stick travel between a near-neutral boundary and the configured edge.
    /// Return timing begins when the stick crosses the edge inward, so time spent held out is not
    /// included.
    /// </summary>
    internal sealed class SticksSpeedMeasurement
    {
        public const float NEAR_NEUTRAL_THRESHOLD = 0.05f;

        private readonly float activationThreshold;

        private bool initialised;
        private Vector2 previousValue;
        private double previousTime;
        private bool neutralObserved;
        private bool edgeReached;
        private double? pressStartTime;
        private double? returnStartTime;
        private double totalPressTime;
        private double totalReturnTime;
        private readonly List<double> pressSamples = new List<double>();
        private readonly List<double> returnSamples = new List<double>();
        private double pressPercentile5;
        private double pressMedian;
        private double pressPercentile95;
        private double returnPercentile5;
        private double returnMedian;
        private double returnPercentile95;

        public int PressCount { get; private set; }

        public int ReturnCount { get; private set; }

        public double? LatestPressTime { get; private set; }

        public double? LatestReturnTime { get; private set; }

        public double? AveragePressTime => PressCount > 0 ? totalPressTime / PressCount : null;

        public double? AverageReturnTime => ReturnCount > 0 ? totalReturnTime / ReturnCount : null;

        public SticksSpeedMeasurement(float activationThreshold)
        {
            this.activationThreshold = Math.Clamp(activationThreshold,
                SticksInputTracker.MIN_ACTIVATION_THRESHOLD,
                SticksInputTracker.MAX_ACTIVATION_THRESHOLD);
        }

        public void Update(float magnitude, double time)
            => Update(new Vector2(Math.Clamp(magnitude, 0, 1), 0), time);

        public void Update(Vector2 value, double time)
        {
            value = value.LengthSquared > 1 ? value.Normalized() : value;

            if (!initialised || !double.IsFinite(time) || time <= previousTime)
            {
                initialise(value, time);
                return;
            }

            updatePressStart(value);

            Span<RadialCrossing> crossings = stackalloc RadialCrossing[4];
            int crossingCount = 0;
            collectCrossings(value, NEAR_NEUTRAL_THRESHOLD, CrossingBoundary.Neutral, crossings, ref crossingCount);
            collectCrossings(value, activationThreshold, CrossingBoundary.Edge, crossings, ref crossingCount);
            sortCrossings(crossings[..crossingCount]);

            foreach (RadialCrossing crossing in crossings[..crossingCount])
                processCrossing(crossing, previousTime + (time - previousTime) * crossing.Progress);

            previousValue = value;
            previousTime = time;
        }

        private void updatePressStart(Vector2 value)
        {
            if (!neutralObserved)
                return;

            float previousMagnitude = previousValue.Length;
            float currentMagnitude = value.Length;

            // Press timing starts at the last observed resting sample, rather than at the 5%
            // return boundary. While still near neutral, any pause or inward movement establishes
            // a newer resting sample for the next outward attempt.
            if (currentMagnitude > previousMagnitude)
            {
                pressStartTime ??= previousTime;
            }
            else if (currentMagnitude <= NEAR_NEUTRAL_THRESHOLD)
            {
                pressStartTime = null;
            }
        }

        public void Reset(float magnitude, double time)
            => Reset(new Vector2(Math.Clamp(magnitude, 0, 1), 0), time);

        public void Reset(Vector2 value, double time)
        {
            PressCount = 0;
            ReturnCount = 0;
            LatestPressTime = null;
            LatestReturnTime = null;
            totalPressTime = 0;
            totalReturnTime = 0;
            pressSamples.Clear();
            returnSamples.Clear();
            pressPercentile5 = pressMedian = pressPercentile95 = 0;
            returnPercentile5 = returnMedian = returnPercentile95 = 0;
            initialised = false;
            initialise(value.LengthSquared > 1 ? value.Normalized() : value, time);
        }

        private void initialise(Vector2 value, double time)
        {
            initialised = true;
            previousValue = value;
            previousTime = double.IsFinite(time) ? time : 0;
            neutralObserved = value.Length <= NEAR_NEUTRAL_THRESHOLD;
            edgeReached = value.Length >= activationThreshold;
            pressStartTime = null;
            returnStartTime = null;
        }

        private void collectCrossings(Vector2 value, float radius, CrossingBoundary boundary, Span<RadialCrossing> crossings, ref int count)
        {
            Vector2 delta = value - previousValue;
            double a = Vector2.Dot(delta, delta);
            if (a < 0.000000000001)
                return;

            double b = 2 * Vector2.Dot(previousValue, delta);
            double c = Vector2.Dot(previousValue, previousValue) - radius * radius;
            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
                return;

            double root = Math.Sqrt(discriminant);
            addCrossing((-b - root) / (2 * a), previousValue, delta, boundary, crossings, ref count);
            addCrossing((-b + root) / (2 * a), previousValue, delta, boundary, crossings, ref count);
        }

        private static void addCrossing(
            double progress,
            Vector2 previousValue,
            Vector2 delta,
            CrossingBoundary boundary,
            Span<RadialCrossing> crossings,
            ref int count)
        {
            const double crossing_epsilon = 0.0000001;

            if (progress <= crossing_epsilon || progress > 1 + crossing_epsilon || count >= crossings.Length)
                return;

            progress = Math.Min(progress, 1);
            Vector2 point = previousValue + delta * (float)progress;
            double radialDirection = Vector2.Dot(point, delta);

            // A tangent only touches the boundary; it does not enter or leave the region.
            if (Math.Abs(radialDirection) <= crossing_epsilon)
                return;

            crossings[count++] = new RadialCrossing(progress, boundary, radialDirection > 0);
        }

        private static void sortCrossings(Span<RadialCrossing> crossings)
        {
            for (int i = 1; i < crossings.Length; i++)
            {
                RadialCrossing value = crossings[i];
                int insertAt = i;

                while (insertAt > 0 && crossings[insertAt - 1].Progress > value.Progress)
                {
                    crossings[insertAt] = crossings[insertAt - 1];
                    insertAt--;
                }

                crossings[insertAt] = value;
            }
        }

        private void processCrossing(RadialCrossing crossing, double time)
        {
            if (crossing.Boundary == CrossingBoundary.Neutral)
            {
                if (crossing.Outward)
                {
                    // Press timing has already begun at the resting position. This boundary only
                    // marks that the stick is no longer near neutral.
                }
                else
                {
                    if (returnStartTime != null)
                    {
                        recordReturn(time - returnStartTime.Value);
                        returnStartTime = null;
                        edgeReached = false;
                    }

                    // A press which returned to neutral without reaching the edge is not a trial.
                    pressStartTime = null;
                    neutralObserved = true;
                }

                return;
            }

            if (crossing.Outward)
            {
                // The player moved back to the edge before reaching neutral. Wait for a complete
                // return rather than reporting a partial attempt.
                returnStartTime = null;
                edgeReached = true;

                if (pressStartTime != null)
                {
                    recordPress(time - pressStartTime.Value);
                    pressStartTime = null;
                }

                neutralObserved = false;
            }
            else if (edgeReached && returnStartTime == null)
            {
                returnStartTime = time;
            }
        }

        private void recordPress(double duration)
        {
            if (!double.IsFinite(duration) || duration < 0)
                return;

            LatestPressTime = duration;
            totalPressTime += duration;
            PressCount++;
            pressSamples.Add(duration);
            calculatePercentiles(pressSamples, out pressPercentile5, out pressMedian, out pressPercentile95);
        }

        private void recordReturn(double duration)
        {
            if (!double.IsFinite(duration) || duration < 0)
                return;

            LatestReturnTime = duration;
            totalReturnTime += duration;
            ReturnCount++;
            returnSamples.Add(duration);
            calculatePercentiles(returnSamples, out returnPercentile5, out returnMedian, out returnPercentile95);
        }

        public bool TryGetPressPercentiles(out double percentile5, out double median, out double percentile95)
        {
            percentile5 = pressPercentile5;
            median = pressMedian;
            percentile95 = pressPercentile95;
            return PressCount > 0;
        }

        public bool TryGetReturnPercentiles(out double percentile5, out double median, out double percentile95)
        {
            percentile5 = returnPercentile5;
            median = returnMedian;
            percentile95 = returnPercentile95;
            return ReturnCount > 0;
        }

        private static void calculatePercentiles(List<double> samples, out double percentile5, out double median, out double percentile95)
        {
            if (samples.Count == 0)
            {
                percentile5 = median = percentile95 = 0;
                return;
            }

            double[] sorted = samples.ToArray();
            Array.Sort(sorted);
            percentile5 = percentile(sorted, 0.05);
            median = percentile(sorted, 0.5);
            percentile95 = percentile(sorted, 0.95);
        }

        private static double percentile(double[] sortedSamples, double proportion)
        {
            double position = (sortedSamples.Length - 1) * proportion;
            int lowerIndex = (int)Math.Floor(position);
            int upperIndex = (int)Math.Ceiling(position);

            if (lowerIndex == upperIndex)
                return sortedSamples[lowerIndex];

            double progress = position - lowerIndex;
            return sortedSamples[lowerIndex] + (sortedSamples[upperIndex] - sortedSamples[lowerIndex]) * progress;
        }

        private enum CrossingBoundary
        {
            Neutral,
            Edge,
        }

        private readonly record struct RadialCrossing(double Progress, CrossingBoundary Boundary, bool Outward);
    }
}
