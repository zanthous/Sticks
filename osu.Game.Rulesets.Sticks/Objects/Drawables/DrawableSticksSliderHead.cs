// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class DrawableSticksSliderHead : DrawableHitObject<SticksSliderHead>
    {
        private readonly Container nestedContainer;
        private DrawableSticksAngleComponent angleComponent = null!;

        public override bool HandlePositionalInput => false;

        public DrawableSticksSliderHead()
            : this(null!)
        {
        }

        public DrawableSticksSliderHead(SticksSliderHead hitObject)
            : base(hitObject)
        {
            Alpha = 0;
            AlwaysPresent = true;
            AddInternal(nestedContainer = new Container { AlwaysPresent = true });
        }

        internal void ApplyHead(double timeOffset, float angleError)
        {
            HitResult timingResult = HitObject.HitWindows?.ResultFor(timeOffset) ?? HitResult.Great;
            HitResult angleResult = HitObject.ResultForCurrentAngleError(angleError);
            (timingResult, angleResult) = SticksHitObject.ResolveComponentResults(timingResult, angleResult);
            ApplyResult(timingResult);
            angleComponent.ApplyAngleResult(angleResult);
        }

        internal void ApplyMiss()
        {
            ApplyMinResult();
            angleComponent.ApplyMiss();
        }

        internal bool BothComponentsHit => IsHit && angleComponent.IsHit;

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            // The parent slider owns the physical head-acquisition gesture.
        }

        protected override void AddNestedHitObject(DrawableHitObject hitObject)
        {
            base.AddNestedHitObject(hitObject);
            nestedContainer.Add(hitObject);
            angleComponent = (DrawableSticksAngleComponent)hitObject;
        }

        protected override void ClearNestedHitObjects()
        {
            base.ClearNestedHitObjects();
            nestedContainer.Clear(false);
            angleComponent = null!;
        }

        protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject) => hitObject switch
        {
            SticksAngleComponent angle => new DrawableSticksAngleComponent(angle),
            _ => base.CreateNestedHitObject(hitObject),
        };

        protected override void UpdateHitStateTransforms(ArmedState state) => Expire();
    }
}
