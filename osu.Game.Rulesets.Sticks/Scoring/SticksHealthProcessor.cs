// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    /// <summary>
    /// Standard-style continuously draining health for Sticks.
    /// </summary>
    public partial class SticksHealthProcessor : DrainingHealthProcessor
    {
        public SticksHealthProcessor(double drainStartTime, double drainLenience = 0)
            : base(drainStartTime, drainLenience)
        {
        }

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
                    increase = 0.011;
                    break;

                case HitResult.Meh:
                    increase = 0.002;
                    break;

                case HitResult.Great:
                    increase = 0.03;
                    break;

                default:
                    increase = base.GetHealthIncreaseFor(result);
                    break;
            }

            return result.HitObject is ISticksAccuracyComponent ? increase * 0.5 : increase;
        }
    }
}
