// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks
{
    public partial class SticksRulesetIcon : CompositeDrawable
    {
        private static readonly Color4 icon_colour = Color4.White;

        public SticksRulesetIcon()
        {
            // Song select places ruleset icons directly inside an auto-sized horizontal flow.
            // The root must therefore have an intrinsic size rather than relative sizing.
            Size = new Vector2(32);

            InternalChildren = new Drawable[]
            {
                createRing(0.78f),
                createRing(0.56f),
                createCursor(0.24f, -0.39f),
                createCursor(0.20f, 0.28f),
            };
        }

        private static CircularContainer createRing(float diameter) => new CircularContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(diameter),
            Masking = true,
            BorderThickness = 2.4f,
            BorderColour = icon_colour,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
                AlwaysPresent = true,
            },
        };

        private static Circle createCursor(float diameter, float relativeY) => new Circle
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativePositionAxes = Axes.Both,
            RelativeSizeAxes = Axes.Both,
            Position = new Vector2(0, relativeY),
            Size = new Vector2(diameter),
            Colour = icon_colour,
        };
    }
}
