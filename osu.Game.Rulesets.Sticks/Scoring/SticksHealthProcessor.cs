using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    /// <summary>
    /// Standard-style continuously draining health for Sticks.
    /// </summary>
    public partial class SticksHealthProcessor : DrainingHealthProcessor
    {
        private const double standard_great_health_increase = 0.03;
        private const double standard_ok_health_increase = 0.011;
        private const double standard_meh_health_increase = 0.002;

        // These live-play values include the per-note equivalent of osu!'s section recovery for a
        // representative six-note section. Simulation intentionally retains the standard values;
        // otherwise drain calibration raises passive drain and cancels the added recovery.
        private const double great_health_increase = standard_great_health_increase + 0.07 / 6;
        private const double ok_health_increase = standard_ok_health_increase + 0.05 / 6;
        private const double meh_health_increase = standard_meh_health_increase + 0.03 / 6;

        public SticksHealthProcessor(double drainStartTime, double drainLenience = 0)
            : base(drainStartTime, drainLenience)
        {
        }

        protected override IEnumerable<HitObject> EnumerateHitObjects(IBeatmap beatmap) =>
            base.EnumerateHitObjects(beatmap).OrderBy(hitObject => hitObject.GetEndTime());

        protected override double GetHealthIncreaseFor(JudgementResult result)
        {
            double increase;

            switch (result.Type)
            {
                case HitResult.LargeTickMiss:
                    increase = IBeatmapDifficultyInfo.DifficultyRange(Beatmap.Difficulty.DrainRate, -0.02, -0.075, -0.14);
                    break;

                case HitResult.Miss:
                    increase = IBeatmapDifficultyInfo.DifficultyRange(Beatmap.Difficulty.DrainRate, -0.03, -0.125, -0.2);
                    break;

                case HitResult.LargeTickHit:
                    increase = result.HitObject is SticksSliderTick ? 0.015 : 0.02;
                    break;

                case HitResult.SliderTailHit:
                    increase = 0.02;
                    break;

                case HitResult.Ok:
                    increase = IsSimulating ? standard_ok_health_increase : ok_health_increase;
                    break;

                case HitResult.Meh:
                    increase = IsSimulating ? standard_meh_health_increase : meh_health_increase;
                    break;

                case HitResult.Great:
                    increase = IsSimulating ? standard_great_health_increase : great_health_increase;
                    break;

                default:
                    increase = base.GetHealthIncreaseFor(result);
                    break;
            }

            return result.HitObject is ISticksAccuracyComponent ? increase * 0.5 : increase;
        }
    }
}
