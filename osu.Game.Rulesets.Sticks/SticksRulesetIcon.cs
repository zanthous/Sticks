// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks
{
    public partial class SticksRulesetIcon : CompositeDrawable
    {
        public SticksRulesetIcon()
        {
            RelativeSizeAxes = Axes.Both;
            InternalChildren = new Drawable[]
            {
                circle(0.78f, SticksPlayfield.LEFT_COLOUR),
                circle(0.48f, SticksPlayfield.RIGHT_COLOUR),
            };
        }

        private static CircularContainer circle(float size, osuTK.Graphics.Color4 colour) => new CircularContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(size),
            Masking = true,
            BorderThickness = 4,
            BorderColour = colour,
            Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
        };
    }
}
