// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksAngleComponent : DrawableHitObject<SticksAngleComponent>
    {
        public override bool DisplayResult => false;

        public override bool HandlePositionalInput => false;

        public DrawableSticksAngleComponent(SticksAngleComponent hitObject)
            : base(hitObject)
        {
            Alpha = 0;
            AlwaysPresent = true;
        }

        internal void ApplyAngleResult(HitResult result) => ApplyResult(result);

        internal void ApplyMiss() => ApplyMinResult();

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            // The owning timing component resolves both halves from the same input attempt.
        }

        protected override void UpdateHitStateTransforms(ArmedState state) => Expire();
    }
}
