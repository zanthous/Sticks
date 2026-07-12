// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.Sticks.Objects
{
    public class SticksHold : SticksHitObject, IHasDuration
    {
        public const double REQUIRED_TRACKING_FRACTION = 0.65;

        public double Duration { get; set; }

        public double EndTime => StartTime + Duration;
    }
}
