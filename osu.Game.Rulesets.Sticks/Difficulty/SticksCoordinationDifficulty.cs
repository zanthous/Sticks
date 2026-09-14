using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Difficulty
{
    /// <summary>
    /// Integrates work shared by both hands. Head and reversal windows follow neighbouring
    /// gameplay events; tracking work follows the actual segments. Completed prefixes are
    /// retained, with only neighbouring windows updated when another event is inserted.
    /// </summary>
    internal sealed class SticksCoordinationDifficulty
    {
        private const double simultaneous_epsilon = 0.01;
        private const double reading_scale = 2.5 * 0.85 * 0.85;
        private const double control_scale = 1.55 * 1.55;

        // Frozen demand reference from the approved Want You Gone comparison. It does
        // not depend on the current map's solo difficulty, identity, or the corpus.
        internal const double REFERENCE_WORK_PER_SECOND = 7.449578513114503;

        private readonly double clockRate;
        private readonly SortedSet<Boundary> boundaries = new SortedSet<Boundary>(Comparer<Boundary>.Create((a, b) => a.Time.CompareTo(b.Time)));
        private readonly WorkTimeline timeline = new WorkTimeline();
        private readonly List<Action> undoGroup = new List<Action>();
        private readonly (double Start, double End, double Before)[] breaks;

        public double CoordinatedWork => Math.Max(0, timeline.Work);

        public double PlayableSeconds => boundaries.Count < 2 ? 0 : Math.Max(0,
            boundaries.Max.Time - boundaries.Min.Time - breakTimeUntil(boundaries.Max.Time) + breakTimeUntil(boundaries.Min.Time)) / 1000 / clockRate;

        public double NormalizedDemand => Normalize(CoordinatedWork, PlayableSeconds);

        public double Participation => Response(NormalizedDemand);

        internal long IntervalUpdateCount => timeline.UpdateCount;

        public SticksCoordinationDifficulty(double clockRate, IEnumerable<BreakPeriod> breakPeriods)
        {
            this.clockRate = clockRate;
            var merged = new List<(double Start, double End)>();
            foreach (BreakPeriod period in (breakPeriods ?? Array.Empty<BreakPeriod>()).OrderBy(b => b.StartTime))
            {
                if (!double.IsFinite(period.StartTime) || !double.IsFinite(period.EndTime) || period.EndTime <= period.StartTime)
                    continue;

                if (merged.Count > 0 && period.StartTime <= merged[^1].End)
                    merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, period.EndTime));
                else
                    merged.Add((period.StartTime, period.EndTime));
            }

            double before = 0;
            breaks = merged.Select(b =>
            {
                var result = (b.Start, b.End, before);
                before += b.End - b.Start;
                timeline.Change(b.Start, b.End, new WorkChange(Breaks: 1));
                return result;
            }).ToArray();
        }

        internal static double Normalize(double work, double playableSeconds)
        {
            if (work <= 0 || playableSeconds <= 0)
                return 0;

            return 1 / (1 + REFERENCE_WORK_PER_SECOND * playableSeconds / work);
        }

        internal static double Response(double demand)
        {
            double p = Math.Clamp(demand, 0, 1);
            double numerator = 6 * p * p;
            return numerator / (numerator + 1 - p);
        }

        public void BeginGroup() => undoGroup.Clear();

        public void RollbackGroup()
        {
            for (int i = undoGroup.Count - 1; i >= 0; i--)
                undoGroup[i]();
            undoGroup.Clear();
        }

        public void AddGroup(IReadOnlyList<SticksHitObject> group, double timestamp,
                             IReadOnlyDictionary<StickSide, double> mechanical, double reading,
                             IReadOnlyDictionary<StickSide, double> control, bool coordinatedHead)
        {
            Boundary head = addBoundary(timestamp);
            var sustains = new List<(SticksHitObject Object, Segment[] Segments)>();
            foreach (SticksHitObject obj in group)
            {
                if (obj.StartTime != timestamp)
                    addBoundary(obj.StartTime);

                Segment[] segments = segmentsOf(obj);
                if (segments.Length == 0)
                    continue;

                sustains.Add((obj, segments));
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].Reversal || i == segments.Length - 1)
                        addBoundary(segments[i].End);
                }
            }

            foreach (var side in group.GroupBy(n => n.Side))
            {
                double precision = side.Average(precisionOf);
                double mass = (mechanical[side.Key] + reading_scale * reading / mechanical.Count) * precision;
                addEvent(head, side.Key, mass, coordinatedHead, timestamp);
            }

            foreach (var side in sustains.GroupBy(s => s.Object.Side))
            {
                // The ordinary model takes the maximum control impulse per hand at a chord.
                // Share that budget if an authored map contains duplicate same-hand sustains.
                foreach (var sustain in side)
                    addSustain(sustain.Object, sustain.Segments, control.GetValueOrDefault(side.Key) / side.Count());
            }
        }

        private void addSustain(SticksHitObject obj, Segment[] segments, double impulse)
        {
            double duration = segments.Sum(s => s.Duration);
            if (duration <= 0)
                return;

            change(segments[0].Start, segments[^1].End,
                obj.Side == StickSide.Left ? new WorkChange(LeftTracking: 1) : new WorkChange(RightTracking: 1));

            bool stationary = segments.All(s => Math.Abs(s.Arc) <= 1e-7);
            int[] turns = Enumerable.Range(0, segments.Length - 1).Where(i => segments[i].Reversal).ToArray();
            double velocity = segments.Sum(s => Math.Abs(s.Arc)) / Math.Max(0.025, duration / 1000 / clockRate);
            double motion = Math.Pow(velocity / 120, 2.2);
            double shortest = segments.Min(s => s.Duration) / 1000 / clockRate;
            double reversal = turns.Length == 0 ? 0 : 0.3 * Math.Log2(turns.Length + 1)
                                                    * Math.Pow(0.4 / Math.Max(0.1, shortest), 0.6)
                                                    * Math.Pow(Math.Max(velocity, 60) / 120, 0.35);
            double total = impulse * control_scale * precisionOf(obj);
            double reverseMass = stationary ? 0 : total * reversal / (0.3 + motion + reversal);
            double[] weights = segments.Select(s => (stationary ? 0.15 : 0.3
                + Math.Pow(Math.Abs(s.Arc) / Math.Max(0.025, s.Duration / 1000 / clockRate) / 120, 2.2)) * s.Duration).ToArray();
            double weightSum = weights.Sum();
            for (int i = 0; i < segments.Length; i++)
            {
                Segment segment = segments[i];
                if (segment.Duration > 0 && weightSum > 0)
                    change(segment.Start, segment.End, workFor(obj.Side, (total - reverseMass) * weights[i] / weightSum / segment.Duration));
            }

            double[] strengths = turns.Select(i =>
            {
                Segment a = segments[i];
                Segment b = segments[i + 1];
                double localTime = Math.Min(a.Duration, b.Duration) / 1000 / clockRate;
                double localSpeed = (Math.Abs(a.Arc) + Math.Abs(b.Arc)) / Math.Max(0.001, (a.Duration + b.Duration) / 1000 / clockRate);
                return Math.Pow(0.4 / Math.Max(0.1, localTime), 0.6) * Math.Pow(Math.Max(localSpeed, 60) / 120, 0.35);
            }).ToArray();
            double strengthSum = strengths.Sum();
            for (int i = 0; i < turns.Length; i++)
            {
                double time = segments[turns[i]].End;
                addEvent(findBoundary(time), obj.Side, reverseMass * strengths[i] / strengthSum, false, time);
            }
        }

        private static double precisionOf(SticksHitObject obj) => obj is SticksClick ? 1
            : SticksDifficultyScaling.AngularPrecisionMultiplier(obj.PrimaryHitAngle, obj.SecondaryHitAngle);

        private static Segment[] segmentsOf(SticksHitObject obj)
        {
            if (obj is SticksHold hold)
                return hold.Duration > 0 ? new[] { new Segment(hold.StartTime, hold.Duration, 0, false) } : Array.Empty<Segment>();
            if (obj is not SticksSlider slider || slider.Duration <= 0)
                return Array.Empty<Segment>();

            // Stationary path subdivisions carry no movement or reversal. Use the same
            // interval as a legacy hold, including identical floating-point arithmetic.
            if (slider.IsStationary)
                return new[] { new Segment(slider.StartTime, slider.Duration, 0, false) };

            double time = slider.StartTime;
            var segments = new Segment[slider.SegmentCount];
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = new Segment(time, slider.SegmentDurationAt(i), slider.SegmentArcAngleAt(i), slider.SegmentEndsWithReversal(i));
                time += segments[i].Duration;
            }
            return segments;
        }

        private readonly record struct Segment(double Start, double Duration, float Arc, bool Reversal)
        {
            public double End => Start + Duration;
        }

        private void change(double start, double end, WorkChange work)
        {
            timeline.Change(start, end, work);
            undoGroup.Add(() => timeline.Change(start, end, work.Negated()));
        }

        private void addEvent(Boundary boundary, StickSide side, double mass, bool coordinated, double time)
        {
            // An event inside a break does not move its work onto adjacent playable time.
            if (insideBreak(time))
                return;

            WorkChange work = workFor(side, mass) with { Heads = coordinated ? 1 : 0 };
            boundary.Mass += work;
            refresh(boundary);
            undoGroup.Add(() =>
            {
                boundary.Mass += work.Negated();
                refresh(boundary);
            });
        }

        private Boundary findBoundary(double time) => boundaries.GetViewBetween(
            new Boundary(time - simultaneous_epsilon), new Boundary(time + simultaneous_epsilon)).Min;

        private Boundary addBoundary(double time)
        {
            Boundary boundary = findBoundary(time);
            if (boundary == null)
            {
                boundary = new Boundary(time);
                boundaries.Add(boundary);
                refreshNeighbours(boundary);
            }
            boundary.References++;
            undoGroup.Add(() =>
            {
                if (--boundary.References > 0)
                    return;
                clearWindow(boundary);
                boundaries.Remove(boundary);
                refreshNeighbours(boundary);
            });
            return boundary;
        }

        private Boundary previous(Boundary b) => boundaries.GetViewBetween(new Boundary(double.NegativeInfinity), new Boundary(Math.BitDecrement(b.Time))).Max;
        private Boundary next(Boundary b) => boundaries.GetViewBetween(new Boundary(Math.BitIncrement(b.Time)), new Boundary(double.PositiveInfinity)).Min;

        private void refreshNeighbours(Boundary b)
        {
            refresh(previous(b));
            refresh(next(b));
        }

        private void clearWindow(Boundary b)
        {
            timeline.Change(b.Start, b.End, b.Applied.Negated());
            b.Applied = default;
        }

        private void refresh(Boundary b)
        {
            if (b == null)
                return;
            clearWindow(b);
            Boundary before = previous(b);
            Boundary after = next(b);
            double leftGap = before != null ? b.Time - before.Time : after != null ? after.Time - b.Time : 0;
            double rightGap = after != null ? after.Time - b.Time : leftGap;
            double half = Math.Min(leftGap, rightGap) / 2;
            b.Start = Math.Max(boundaries.Min.Time, b.Time - half);
            b.End = Math.Min(boundaries.Max.Time, b.Time + half);
            if (b.End <= b.Start)
                return;
            b.Applied = b.Mass with { Left = b.Mass.Left / (b.End - b.Start), Right = b.Mass.Right / (b.End - b.Start) };
            timeline.Change(b.Start, b.End, b.Applied);
        }

        private int breakIndex(double time)
        {
            int low = 0;
            int high = breaks.Length;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (breaks[middle].Start <= time)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low - 1;
        }

        private bool insideBreak(double time)
        {
            int i = breakIndex(time);
            return i >= 0 && time < breaks[i].End;
        }

        private double breakTimeUntil(double time)
        {
            int i = breakIndex(time);
            return i < 0 ? 0 : breaks[i].Before + Math.Min(time, breaks[i].End) - breaks[i].Start;
        }

        private sealed class Boundary
        {
            public readonly double Time;
            public int References;
            public WorkChange Mass;
            public WorkChange Applied;
            public double Start;
            public double End;

            public Boundary(double time) => Time = time;
        }

        private static WorkChange workFor(StickSide side, double amount) =>
            side == StickSide.Left ? new WorkChange(Left: amount) : new WorkChange(Right: amount);

        private readonly record struct WorkChange(double Left = 0, double Right = 0, int LeftTracking = 0, int RightTracking = 0, int Heads = 0, int Breaks = 0)
        {
            public static WorkChange operator +(WorkChange a, WorkChange b) => new WorkChange(
                a.Left + b.Left, a.Right + b.Right, a.LeftTracking + b.LeftTracking, a.RightTracking + b.RightTracking, a.Heads + b.Heads, a.Breaks + b.Breaks);

            public WorkChange Negated() => new WorkChange(-Left, -Right, -LeftTracking, -RightTracking, -Heads, -Breaks);
        }

        /// <summary>
        /// Piecewise-constant work rates. Range edits split only touched intervals, and the
        /// cached integral makes per-object timed difficulty queries independent of map length.
        /// </summary>
        private sealed class WorkTimeline
        {
            private readonly SortedSet<Cell> cells = new SortedSet<Cell>(Comparer<Cell>.Create((a, b) => a.Start.CompareTo(b.Start)))
            {
                new Cell(double.NegativeInfinity, double.PositiveInfinity),
            };

            private double sum;
            private double compensation;
            public double Work => sum + compensation;
            public long UpdateCount { get; private set; }

            public void Change(double start, double end, WorkChange change)
            {
                if (end <= start || change == default)
                    return;
                split(start);
                split(end);
                foreach (Cell cell in cells.GetViewBetween(new Cell(start), new Cell(end)))
                {
                    if (cell.Start >= end)
                        break;
                    add(-cell.Contribution);
                    cell.Work += change;
                    cell.Contribution = contribution(cell);
                    add(cell.Contribution);
                    UpdateCount++;
                }
            }

            private void split(double time)
            {
                Cell cell = cells.GetViewBetween(new Cell(double.NegativeInfinity), new Cell(time)).Max;
                if (cell.Start == time)
                    return;
                var following = new Cell(time, cell.End) { Work = cell.Work };
                add(-cell.Contribution);
                cell.End = time;
                cell.Contribution = contribution(cell);
                following.Contribution = contribution(following);
                cells.Add(following);
                add(cell.Contribution);
                add(following.Contribution);
            }

            private static double contribution(Cell cell)
            {
                WorkChange work = cell.Work;
                if (work.Breaks > 0 || (work.Heads <= 0 && (work.LeftTracking <= 0 || work.RightTracking <= 0))
                    || work.Left <= 0 || work.Right <= 0)
                    return 0;
                return (cell.End - cell.Start) * 4 * work.Left * work.Right / (work.Left + work.Right);
            }

            private void add(double value)
            {
                // Neumaier summation avoids accumulating drift as old windows are removed
                // and replaced during a long editor/timed-difficulty traversal.
                double nextSum = sum + value;
                compensation += Math.Abs(sum) >= Math.Abs(value) ? (sum - nextSum) + value : (value - nextSum) + sum;
                sum = nextSum;
            }

            private sealed class Cell
            {
                public readonly double Start;
                public double End;
                public WorkChange Work;
                public double Contribution;

                public Cell(double start, double end = 0)
                {
                    Start = start;
                    End = end;
                }
            }
        }
    }
}
