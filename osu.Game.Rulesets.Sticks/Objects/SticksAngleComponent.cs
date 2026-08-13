// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using Newtonsoft.Json;
using osu.Game.Rulesets.Judgements;
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

        /// <summary>
        /// The absolute angular error measured for the input which judged this component.
        /// A null value means that no input attempt was measured (for example, a timeout miss).
        /// </summary>
        [JsonIgnore]
        public float? HitError { get; set; }

        public override Judgement CreateJudgement() => new SticksAngleJudgement();

        protected override HitWindows CreateHitWindows() => HitWindows.Empty;
    }
}
