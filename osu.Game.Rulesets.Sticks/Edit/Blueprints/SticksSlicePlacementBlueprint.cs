using osu.Framework.Input.Events;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK.Input;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    public partial class SticksSlicePlacementBlueprint : SticksPlacementBlueprint<SticksSlice>
    {
        public SticksSlicePlacementBlueprint() : base(new SticksSlice()) { }
        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button == MouseButton.Left && HasValidPosition)
            {
                EndPlacement(true);
                return true;
            }
            return base.OnMouseDown(e);
        }
    }
}
