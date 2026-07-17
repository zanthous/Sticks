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

    /// <summary>
    /// Supplies one animated radial displacement to every visual belonging to a duration object.
    /// Gameplay angles and controller thresholds remain on the ruleset's normal lane.
    /// </summary>
    internal interface ISticksVisualRadialOffsetSource
    {
        float VisualRadialOffset { get; }
    }
}
