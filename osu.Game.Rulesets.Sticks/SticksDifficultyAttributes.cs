// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osu.Game.Rulesets.Difficulty;

namespace osu.Game.Rulesets.Sticks
{
    /// <summary>
    /// Exposes the independent parts of Sticks difficulty so the model can be tuned without
    /// reverse-engineering one final star value.
    /// </summary>
    public class SticksDifficultyAttributes : DifficultyAttributes
    {
        public double MechanicalDifficulty { get; set; }

        public double ReadingDifficulty { get; set; }

        public double ControlDifficulty { get; set; }

        public double CoordinationDifficulty { get; set; }

        public double AngularPrecision { get; set; } = 1;

        public double TimingPrecision { get; set; } = 1;
    }
}
