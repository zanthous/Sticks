using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Mods;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    /// <summary>
    /// Uses the same score multipliers as osu!standard for equivalent difficulty mods.
    /// </summary>
    public class SticksScoreMultiplierCalculator : ScoreMultiplierCalculator
    {
        public SticksScoreMultiplierCalculator(ScoreMultiplierContext context)
            : base(context)
        {
            Single<SticksModEasy>(0.8);
            Single<SticksModNoFail>(0.5);
            Single<SticksModHalfTime>(mod => halfTimeMultiplier(mod.SpeedChange.Value));

            Single<SticksModHardRock>(1.09);
            Single<SticksModDoubleTime>(mod => doubleTimeMultiplier(mod.SpeedChange.Value));
        }

        private static double halfTimeMultiplier(double speedChange) =>
            (int)(speedChange * 20) / 20.0 * 1.4 - 0.5;

        private static double doubleTimeMultiplier(double speedChange)
        {
            double flooredRate = (int)(speedChange * 10) / 10.0;
            double nonDefaultPenalty = flooredRate != 1.5 && flooredRate != 1 ? 0.01 : 0;
            return (flooredRate - 1) * 0.46 + 1 - nonDefaultPenalty;
        }
    }
}
