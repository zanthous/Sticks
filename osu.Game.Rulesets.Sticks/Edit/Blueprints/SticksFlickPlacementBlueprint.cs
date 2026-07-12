// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Input.Events;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK.Input;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    public partial class SticksFlickPlacementBlueprint : SticksPlacementBlueprint<SticksFlick>
    {
        public SticksFlickPlacementBlueprint()
            : base(new SticksFlick())
        {
        }

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
