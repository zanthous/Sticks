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

        /// <summary>
        /// Number of top-level objects which award the paired timing and angle judgements.
        /// </summary>
        public int AccuracyObjectCount { get; set; }

        /// <summary>
        /// Number of non-tail tracking checkpoints which may break combo.
        /// </summary>
        public int TrackingObjectCount { get; set; }

        /// <summary>
        /// Number of slider and hold tails. Dropped tails do not break combo.
        /// </summary>
        public int TailObjectCount { get; set; }

        public double OverallDifficulty { get; set; }

        public double ClockRate { get; set; } = 1;

        public double MechanicalDifficultStrainCount { get; set; }

        public double ReadingDifficultStrainCount { get; set; }

        public double ControlDifficultStrainCount { get; set; }

        public double CoordinationDifficultStrainCount { get; set; }
    }
}
