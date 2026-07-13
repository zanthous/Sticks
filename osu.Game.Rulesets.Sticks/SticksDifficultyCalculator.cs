// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Sticks
{
    public class SticksDifficultyCalculator : DifficultyCalculator
    {
        public SticksDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
        {
            SticksHitObject[] objects = beatmap.HitObjects.OfType<SticksHitObject>().OrderBy(hitObject => hitObject.StartTime).ToArray();
            if (objects.Length == 0)
                return new DifficultyAttributes(mods, 0);

            double stars = CalculateStarRating(objects, ModUtils.CalculateRateWithMods(mods));
            return new DifficultyAttributes(mods, stars) { MaxCombo = objects.Length };
        }

        public static double CalculateStarRating(IEnumerable<SticksHitObject> hitObjects, double clockRate = 1)
        {
            SticksHitObject[] objects = hitObjects.OrderBy(hitObject => hitObject.StartTime).ToArray();
            if (objects.Length == 0)
                return 0;

            clockRate = double.IsFinite(clockRate) && clockRate > 0 ? clockRate : 1;
            var previousBySide = new Dictionary<StickSide, PreviousObject>();
            var activeSliders = new List<SticksSlider>();
            var sectionPeaks = new Dictionary<int, double>();
            SticksHitObject[] previousPattern = Array.Empty<SticksHitObject>();
            double previousTimestamp = double.NaN;

            foreach (IGrouping<double, SticksHitObject> timestampGroup in objects.GroupBy(hitObject => Math.Round(hitObject.StartTime, 2)))
            {
                SticksHitObject[] simultaneous = timestampGroup.ToArray();
                double timestamp = simultaneous[0].StartTime;
                activeSliders.RemoveAll(slider => slider.EndTime < timestamp - 0.01);
                double globalInterval = double.IsNaN(previousTimestamp)
                    ? double.PositiveInfinity
                    : effectiveInterval(timestamp - previousTimestamp, clockRate);
                double chordSpread = angularSpread(simultaneous.Select(hitObject => hitObject.Angle));
                double chordSpreadDemand = simultaneous.Length > 1
                    ? Math.Pow(chordSpread / 90, 0.9) * 0.35
                    : 0;

                foreach (SticksHitObject current in simultaneous)
                {
                    bool hasPrevious = previousBySide.TryGetValue(current.Side, out PreviousObject previous);
                    double sameSideInterval = hasPrevious
                        ? effectiveInterval(current.StartTime - previous.EndTime, clockRate)
                        : double.PositiveInfinity;
                    double sameSideAngleTravel = hasPrevious
                        ? Math.Abs(SticksHitObject.DeltaAngle(previous.ExitAngle, current.Angle))
                        : 0;
                    double patternAngleTravel = previousPattern.Length > 0
                        ? previousPattern.Min(previousObject => Math.Abs(SticksHitObject.DeltaAngle(angleAt(previousObject, timestamp), current.Angle)))
                        : 0;

                    double globalDemand = double.IsFinite(globalInterval)
                        ? Math.Pow(140 / Math.Max(25, globalInterval), 1.25)
                        : 0;
                    double sameSideDemand = double.IsFinite(sameSideInterval)
                        ? Math.Pow(320 / Math.Max(25, sameSideInterval), 1.35)
                        : 0;
                    double sameSideAngleDemand = double.IsFinite(sameSideInterval)
                        ? sameSideAngleTravel / 90 * Math.Pow(180 / Math.Max(40, sameSideInterval), 0.75)
                        : 0;
                    double patternAngleDemand = double.IsFinite(globalInterval) && previousPattern.Length > 0
                        ? patternAngleTravel / 90 * Math.Pow(180 / Math.Max(40, globalInterval), 0.75)
                        : 0;

                    double demand;

                    if (current is SticksSlider slider)
                    {
                        double effectiveDurationSeconds = Math.Max(0.025, slider.Duration / 1000 / clockRate);
                        double angularVelocity = slider.TotalAngularDistance / effectiveDurationSeconds;
                        double motionDemand = Math.Pow(angularVelocity / 100, 1.25);
                        double shortestSegmentSeconds = Enumerable.Range(0, slider.SegmentCount)
                                                                  .Select(slider.SegmentDurationAt)
                                                                  .DefaultIfEmpty(slider.Duration)
                                                                  .Min() / 1000 / clockRate;
                        double reversalDemand = slider.SegmentCount == 1
                            ? 0
                            : Math.Log2(slider.SegmentCount + 1) * Math.Pow(0.4 / Math.Max(0.025, shortestSegmentSeconds), 1.1) * 0.6;

                        demand = 0.6 + sameSideDemand * 0.3 + sameSideAngleDemand * 0.45 + patternAngleDemand * 0.2
                                 + globalDemand * 0.35 + motionDemand + reversalDemand;
                    }
                    else if (current is SticksHold)
                    {
                        demand = 0.5 + sameSideDemand * 0.25 + globalDemand * 0.3
                                 + sameSideAngleDemand * 0.4 + patternAngleDemand * 0.2;
                    }
                    else
                    {
                        // Flicks must return to neutral. Same-stick timing therefore dominates their physical demand.
                        demand = 0.45 + sameSideDemand + globalDemand * 0.55
                                 + sameSideAngleDemand * 0.8 + patternAngleDemand * 0.35;
                    }

                    if (simultaneous.Length > 1)
                        demand += (simultaneous.Length - 1) * 0.65 + chordSpreadDemand;

                    if (activeSliders.Any(slider => slider.Side != current.Side && slider.StartTime < timestamp && slider.EndTime > timestamp))
                        demand *= 1.15;

                    int section = (int)Math.Floor(timestamp / clockRate / 400);
                    sectionPeaks[section] = Math.Max(sectionPeaks.GetValueOrDefault(section), demand);
                }

                foreach (SticksHitObject current in simultaneous)
                {
                    double endTime = current switch
                    {
                        SticksSlider slider => slider.EndTime,
                        SticksHold hold => hold.EndTime,
                        _ => current.StartTime,
                    };
                    float exitAngle = current is SticksSlider exitingSlider ? exitingSlider.AngleAt(exitingSlider.EndTime) : current.Angle;
                    previousBySide[current.Side] = new PreviousObject(endTime, exitAngle);

                    if (current is SticksSlider activeSlider)
                        activeSliders.Add(activeSlider);
                }

                previousPattern = simultaneous;
                previousTimestamp = timestamp;
            }

            double weightedPeak = 0;
            double totalWeight = 0;
            double weight = 1;

            foreach (double peak in sectionPeaks.Values.OrderByDescending(value => value).Take(12))
            {
                weightedPeak += peak * weight;
                totalWeight += weight;
                weight *= 0.9;
            }

            double stars = 0.4 + 1.45 * weightedPeak / Math.Max(1, totalWeight);
            return Math.Clamp(stars, 0, 30);
        }

        private static double effectiveInterval(double interval, double clockRate) => interval / clockRate;

        private static float angleAt(SticksHitObject hitObject, double time) =>
            hitObject is SticksSlider slider ? slider.AngleAt(time) : hitObject.Angle;

        private static double angularSpread(IEnumerable<float> angles)
        {
            float[] values = angles.ToArray();
            double maximum = 0;

            for (int i = 0; i < values.Length; i++)
            {
                for (int j = i + 1; j < values.Length; j++)
                    maximum = Math.Max(maximum, Math.Abs(SticksHitObject.DeltaAngle(values[i], values[j])));
            }

            return maximum;
        }

        private readonly record struct PreviousObject(double EndTime, float ExitAngle);

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods) => Enumerable.Empty<DifficultyHitObject>();

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods) => Array.Empty<Skill>();
    }
}
