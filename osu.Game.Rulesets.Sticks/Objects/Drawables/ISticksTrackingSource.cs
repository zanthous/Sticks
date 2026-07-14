// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    /// <summary>
    /// Exposes whether a duration object has received a fresh neutral-to-edge gesture.
    /// Its nested checkpoints must not award tracking credit before this becomes true.
    /// </summary>
    internal interface ISticksTrackingSource
    {
        bool TrackingAuthorised { get; }
    }
}
