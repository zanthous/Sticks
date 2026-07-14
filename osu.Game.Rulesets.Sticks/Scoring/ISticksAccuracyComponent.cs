// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

namespace osu.Game.Rulesets.Sticks.Scoring
{
    /// <summary>
    /// Identifies one half of a Sticks basic-object judgement.
    /// Timing and angular accuracy are emitted as equally-weighted native judgements.
    /// </summary>
    public interface ISticksAccuracyComponent
    {
        SticksAccuracyComponent AccuracyComponent { get; }
    }

    public enum SticksAccuracyComponent
    {
        Timing,
        Angle,
    }
}
