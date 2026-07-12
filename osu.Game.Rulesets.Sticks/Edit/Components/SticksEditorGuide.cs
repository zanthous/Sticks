// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Edit.Components
{
    public partial class SticksEditorGuide : CompositeDrawable
    {
        public SticksEditorGuide()
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            Depth = float.MaxValue;

            InternalChildren = new Drawable[]
            {
                ring(SticksPlayfield.OUTER_RADIUS, SticksPlayfield.LEFT_COLOUR.Opacity(0.38f)),
                ring(SticksPlayfield.INNER_RADIUS, SticksPlayfield.RIGHT_COLOUR.Opacity(0.38f)),
            };
        }

        private static CircularContainer ring(float radius, Color4 colour) => new CircularContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Position = new Vector2(SticksPlayfield.SIZE / 2),
            Size = new Vector2(radius * 2),
            Masking = true,
            BorderThickness = 2,
            BorderColour = colour,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
                AlwaysPresent = true,
            },
        };
    }
}
