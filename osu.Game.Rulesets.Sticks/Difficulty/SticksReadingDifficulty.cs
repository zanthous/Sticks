using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Difficulty
{
    /// <summary>
    /// Reading work from each stick's target sequence, accumulated across nearby heads.
    /// </summary>
    internal sealed class SticksReadingDifficulty
    {
        private const double work_scale = 7.5;
        private const double step_change_scale = 3;
        private const double decay_base = 0.3;

        // Preserve the steady work level at a 125 ms interval. Faster sequences add more
        // work before the preceding heads have decayed, including bursts before jumps.
        private static readonly double event_normalization = 1 - Math.Pow(decay_base, 0.125);

        private readonly double clockRate;
        private readonly BreakPeriod[] breaks;
        private readonly Dictionary<(StickSide Side, bool Click), Head> history = new Dictionary<(StickSide Side, bool Click), Head>();
        private bool hasTime;
        private double lastTime;
        private double strain;

        public SticksReadingDifficulty(double clockRate, IEnumerable<BreakPeriod> breaks = null)
        {
            this.clockRate = clockRate;
            this.breaks = breaks as BreakPeriod[] ?? breaks?.ToArray() ?? Array.Empty<BreakPeriod>();
        }

        public double Process(IReadOnlyList<SticksHitObject> group, double time,
                              double previousGroupDistance, double objectTypeBonus,
                              double chordBonus, bool followsActiveSliderArc)
        {
            if (hasTime && breaks.Any(b => lastTime < b.EndTime && time >= b.EndTime))
            {
                history.Clear();
                strain = 0;
            }

            double work = 0;
            var nextHeads = new Dictionary<(StickSide Side, bool Click), Head>();

            // Evaluate every chord member against the preceding group, then commit their
            // endpoints together. A click must not replace a stick's directional history.
            foreach (SticksHitObject current in group)
            {
                var key = (current.Side, current is SticksClick);
                double distance = 0;
                double change = 0;
                double? step = null;

                if (current is not SticksClick && history.TryGetValue(key, out Head previous))
                {
                    step = deltaAngle(previous.EndAngle, current.Angle);
                    distance = Math.Pow(Math.Abs(step.Value) / 180, 0.7);

                    if (previous.Step.HasValue)
                    {
                        double gap = Math.Max(0, (time - previous.Time) / clockRate);
                        double continuity = Math.Pow(decay_base, gap / 1000);
                        change = Math.Abs(Math.Sin((Math.Abs(step.Value) - Math.Abs(previous.Step.Value)) * Math.PI / 360)) * continuity;
                    }
                }

                work += 0.3 + distance * (1 + step_change_scale * change);

                double endTime = current switch
                {
                    SticksSlider slider => slider.EndTime,
                    SticksHold hold => hold.EndTime,
                    _ => current.StartTime,
                };
                double endAngle = current is SticksSlider path ? path.AngleAt(path.EndTime) : current.Angle;
                var next = new Head(time, endTime, endAngle, step);

                if (!nextHeads.TryGetValue(key, out Head existing) || next.EndTime > existing.EndTime)
                    nextHeads[key] = next;
            }

            foreach (var (key, head) in nextHeads)
                history[key] = head;

            // Separation from the other hand is a bounded visual cue, not a multiplier
            // on the full demand of the sequence.
            work += 0.25 * previousGroupDistance + objectTypeBonus + chordBonus;
            if (followsActiveSliderArc)
                work *= 0.85;

            double decay = hasTime ? Math.Pow(decay_base, (time - lastTime) / clockRate / 1000) : 0;
            strain = strain * decay + work_scale * work * event_normalization;
            lastTime = time;
            hasTime = true;
            return strain;
        }

        public SticksReadingDifficulty Clone()
        {
            var copy = new SticksReadingDifficulty(clockRate, breaks);
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(SticksReadingDifficulty other)
        {
            hasTime = other.hasTime;
            lastTime = other.lastTime;
            strain = other.strain;
            history.Clear();
            foreach (var (key, head) in other.history)
                history[key] = head;
        }

        private static double deltaAngle(double from, double to) => ((to - from + 180) % 360 + 360) % 360 - 180;

        private readonly record struct Head(double Time, double EndTime, double EndAngle, double? Step);
    }
}
