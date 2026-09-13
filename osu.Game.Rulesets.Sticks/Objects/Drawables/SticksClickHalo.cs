using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Sticks.Skinning;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    /// <summary>
    /// Shared click artwork for gameplay and placement. A skin ring uses a 465-square
    /// design canvas, with its stroke centred at radius 230; both presentations reach
    /// the judgement ring with their stroke centre, independently of texture resolution.
    /// </summary>
    public partial class SticksClickHalo : CompositeDrawable
    {
        public const float STROKE_THICKNESS = 5;
        public const float SKIN_DIAMETER = SticksPlayfield.GUIDE_RADIUS * 2 + STROKE_THICKNESS;

        private readonly CircularContainer border;
        private readonly SticksSkinnedSprite skin;

        public SticksClickHalo()
        {
            Origin = Anchor.Centre;
            border = new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
            };
            InternalChild = skin = new SticksSkinnedSprite("sticks-click", border)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            };
        }

        public void SetGeometry(float radius, Color4 colour)
        {
            float thickness = Math.Min(STROKE_THICKNESS, radius);
            // Masked borders draw inward; their centreline stays at the approach radius.
            border.Size = Size = new Vector2(radius * 2 + thickness);
            border.BorderThickness = thickness;
            border.BorderColour = colour;
            skin.Size = new Vector2(SKIN_DIAMETER * radius / SticksPlayfield.GUIDE_RADIUS);
            skin.TextureColour = colour;
        }
    }
}
