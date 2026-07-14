// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    /// <summary>
    /// The angular half of a basic Sticks judgement.
    /// This object is generated from its timing component and is not serialised independently.
    /// </summary>
    public class SticksAngleComponent : SticksHitObject, ISticksAccuracyComponent
    {
        public SticksAccuracyComponent AccuracyComponent => SticksAccuracyComponent.Angle;

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;
    }
}
