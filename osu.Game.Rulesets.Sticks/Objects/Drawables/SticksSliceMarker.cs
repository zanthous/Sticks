using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class SticksSliceMarker : CompositeDrawable
    {
        private readonly Circle centre;
        private readonly SmoothPath arrow;

        public SticksSliceMarker()
        {
            Size = new Vector2(SticksSlice.RADIUS * 2);
            Origin = Anchor.Centre;
            AddInternal(new CircularContainer
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                BorderThickness = 2,
                BorderColour = Color4.White,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = new Color4(1, 1, 1, 0.45f),
                    Radius = 8,
                },
                Child = centre = new Circle { RelativeSizeAxes = Axes.Both },
            });
            AddInternal(arrow = new SmoothPath
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.None,
                Size = new Vector2(12, 16),
                PathRadius = 1.8f,
                Colour = Color4.White,
                Vertices = new[] { new Vector2(2, 2), new Vector2(10, 8), new Vector2(2, 14) },
            });
        }

        public void SetState(float radius, float angle, SticksSliceDirection direction, Color4 colour)
        {
            Position = SticksPlayfield.PointAt(angle, radius);
            // CS-independent size at the hit ring, growing with the centre-out approach.
            Scale = new Vector2(Math.Clamp(radius / SticksPlayfield.GUIDE_RADIUS, 0, 1));
            centre.Colour = colour;
            arrow.Alpha = direction == SticksSliceDirection.Neutral ? 0 : 1;
            arrow.Rotation = angle + (int)direction * 90;
        }
    }
}
