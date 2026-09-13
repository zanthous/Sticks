using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// Keeps the gameplay cursor's centre independent of custom artwork dimensions.
    /// </summary>
    internal partial class SticksStickCursor : CircularContainer
    {
        private readonly Box fill;
        private readonly SticksSkinnedSprite visual;

        public SticksStickCursor(StickSide side, Color4 colour)
        {
            Anchor = Anchor.TopLeft;
            Origin = Anchor.Centre;
            Size = new Vector2(24);
            Depth = -20;

            Child = visual = new SticksSkinnedSprite(
                side == StickSide.Left ? "sticks-cursor-left" : "sticks-cursor-right",
                fill = new Box { RelativeSizeAxes = Axes.Both, Colour = colour })
            {
                RelativeSizeAxes = Axes.Both,
                NativeSkinSize = true,
            };
            visual.OnSkinChanged += updateAppearance;
            updateAppearance();
        }

        public void SetPaletteColour(Color4 colour)
        {
            fill.Colour = colour;
            updateAppearance();
        }

        private void updateAppearance()
        {
            bool custom = visual.UsesSkinTexture;
            if (!custom)
                Masking = true;

            BorderThickness = custom ? 0 : 3;
            BorderColour = Color4.White;
            EdgeEffect = custom ? default : new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = Color4.Black.Opacity(0.7f),
                Offset = new Vector2(0, 3),
                Radius = 5,
                Hollow = true,
            };
            if (custom)
                Masking = false;
        }
    }
}
