// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Threading;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects
{
    public class SticksFlick : SticksHitObject, ISticksAccuracyComponent
    {
        public const double EARLY_HIT_WINDOW = SticksHitWindows.MISS_WINDOW;
        public const double LATE_HIT_WINDOW = SticksHitWindows.MISS_WINDOW;

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
