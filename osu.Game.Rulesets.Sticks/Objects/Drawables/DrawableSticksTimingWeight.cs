using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
        internal partial class DrawableSticksTimingWeight : DrawableHitObject<SticksClick.TimingWeight>
        {
            public override bool DisplayResult => false;
            public override bool HandlePositionalInput => false;

            public DrawableSticksTimingWeight(SticksClick.TimingWeight hitObject) : base(hitObject)
            {
                Alpha = 0;
                AlwaysPresent = true;
            }

            public void ApplyTimingResult(HitResult result) => ApplyResult(result);

            protected override void CheckForResult(bool userTriggered, double timeOffset)
            {
                // The click resolves both scoring halves from the same button press.
            }

            protected override void UpdateHitStateTransforms(ArmedState state) => Expire();
        }

}
