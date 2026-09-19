using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Shares opportunities for introduced simultaneous play across the arrangement passes.
    /// Ordinary source objects and sequential hand changes do not consume this allowance.
    /// </summary>
    internal sealed class SticksConversionCoordinationAllowance
    {
        private readonly IBeatmap beatmap;
        private readonly SortedSet<double> available = new SortedSet<double>();

        public bool IsRestricted { get; }

        public SticksConversionCoordinationAllowance(IBeatmap beatmap, IEnumerable<HitObject> objects, double sourceStars)
        {
            this.beatmap = beatmap;
            IsRestricted = sourceStars < 3;
            if (!IsRestricted)
                return;

            // Linearly connect the source-star anchors: 0% through 1.6★,
            // 30% at 2★, and the full arrangement at 3★.
            double allowance = sourceStars <= 2
                ? Math.Clamp((sourceStars - 1.6) / 0.4, 0, 1) * 0.3
                : 0.3 + (sourceStars - 2) * 0.7;
            double accumulated = 0.5;
            foreach (double window in objects.Select(note => WindowAt(note.StartTime)).Distinct().OrderBy(time => time))
            {
                accumulated += allowance;
                if (accumulated < 1 - 1e-9)
                    continue;

                available.Add(window);
                accumulated -= 1;
            }
        }

        public static double CalculateSourceStars(IBeatmap beatmap, CancellationToken cancellationToken)
        {
            // Calculate osu!standard directly, with no conversion or difficulty mods.
            // Neither cached metadata, OD, nor Sticks' resulting rating determines the ramp.
            // Non-standard diagnostic inputs without positions retain their previous path.
            if (beatmap.HitObjects.Any(note => note is not IHasPosition))
                return double.PositiveInfinity;

            var ruleset = new OsuRuleset();
            return ruleset.CreateDifficultyCalculator(new FlatWorkingBeatmap(beatmap)).Calculate(cancellationToken).StarRating;
        }

        public bool CanIntroduce(double start) => !IsRestricted || available.Contains(WindowAt(start));

        private double WindowAt(double time)
        {
            var timing = beatmap.ControlPointInfo.TimingPointAt(time);
            double beat = double.IsFinite(timing.BeatLength) && timing.BeatLength > 0 ? timing.BeatLength : 500;
            // Use the same eight-beat grouping as the default arrangement planner. Count only
            // occupied windows, so silence cannot accumulate extra coordination slots.
            return timing.Time + Math.Floor((time - timing.Time) / (beat * 8)) * beat * 8;
        }
    }
}
