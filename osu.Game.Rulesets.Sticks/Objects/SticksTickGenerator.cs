// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps.ControlPoints;

namespace osu.Game.Rulesets.Sticks.Objects
{
    /// <summary>
    /// Generates beat-aligned checkpoints for duration objects. Object heads and slider reversals
    /// begin a new tick phase, while timing points inside that phase immediately adopt the new BPM.
    /// </summary>
    internal static class SticksTickGenerator
    {
        private const double end_leniency = 10;
        private const double time_epsilon = 0.001;

        public static IEnumerable<double> Generate(ControlPointInfo controlPointInfo, double phaseStart, double phaseEnd,
                                                   double tickRate, CancellationToken cancellationToken)
        {
            if (!double.IsFinite(phaseStart) || !double.IsFinite(phaseEnd) || phaseEnd <= phaseStart
                || !double.IsFinite(tickRate) || tickRate <= 0)
                yield break;

            double sectionStart = phaseStart;
            bool timingPointBeginsSection = false;

            while (sectionStart < phaseEnd - end_leniency)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TimingControlPoint timingPoint = controlPointInfo.TimingPointAt(sectionStart);
                double interval = timingPoint.BeatLength / tickRate;
                if (!double.IsFinite(interval) || interval <= time_epsilon)
                    yield break;

                TimingControlPoint nextTimingPoint = controlPointInfo.TimingPointAfter(sectionStart);
                double sectionEnd = nextTimingPoint == null
                    ? phaseEnd
                    : Math.Min(phaseEnd, nextTimingPoint.Time);

                // A timing point is the first beat of its new timing section. Unlike an object
                // head or reversal, it has no separate gameplay checkpoint, so retain that beat.
                if (timingPointBeginsSection && sectionStart < phaseEnd - end_leniency)
                    yield return sectionStart;

                for (double tickTime = sectionStart + interval;
                     tickTime < sectionEnd - time_epsilon && tickTime < phaseEnd - end_leniency;
                     tickTime += interval)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return tickTime;
                }

                if (nextTimingPoint == null || nextTimingPoint.Time >= phaseEnd - time_epsilon
                                            || nextTimingPoint.Time <= sectionStart + time_epsilon)
                    yield break;

                sectionStart = nextTimingPoint.Time;
                timingPointBeginsSection = true;
            }
        }
    }
}
