// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Threading;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    /// <summary>
    /// The timing half of a slider-head judgement.
    /// </summary>
    public class SticksSliderHead : SticksHitObject, ISticksAccuracyComponent
    {
        public SticksAccuracyComponent AccuracyComponent => SticksAccuracyComponent.Timing;

        protected override void CreateNestedHitObjects(CancellationToken cancellationToken)
        {
            base.CreateNestedHitObjects(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            AddNested(new SticksAngleComponent
            {
                StartTime = StartTime,
                Side = Side,
                Angle = Angle,
            });
        }
    }
}
