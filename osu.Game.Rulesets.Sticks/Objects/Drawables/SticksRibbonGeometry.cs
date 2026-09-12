using System;
using osu.Framework.Graphics.Primitives;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    internal readonly record struct SticksRibbonPoint(float Radius, float Angle, float HalfSpan)
    {
        public Vector2 LeftEdge => SticksPlayfield.PointAt(Angle - HalfSpan, Radius);
        public Vector2 RightEdge => SticksPlayfield.PointAt(Angle + HalfSpan, Radius);
    }

    // EdgeGlow follows the quad's top-left, top-right, bottom-left, bottom-right order.
    internal readonly record struct SticksRibbonQuad(Quad Geometry, Vector4 EdgeGlow);

    internal static class SticksRibbonGeometry
    {
        public const int MAX_ANGULAR_SEGMENTS = 72;
        private const float max_segment_degrees = 5;
        private const float edge_glow_width = 24;

        public static int AngularSegmentsFor(float halfSpan)
        {
            // An even count includes the centre of narrow ribbons, keeping their
            // dark interior between the two edge gradients. At the guide radius,
            // five-degree arc segments have less than a quarter pixel of sag.
            int halfSegments = Math.Clamp((int)MathF.Ceiling(halfSpan / max_segment_degrees), 1, MAX_ANGULAR_SEGMENTS / 2);
            return halfSegments * 2;
        }

        /// <summary>
        /// Connects two circular time slices without replacing the angle window
        /// with one straight chord. The caller reuses the destination each strip.
        /// </summary>
        public static int FillStrip(SticksRibbonPoint from, SticksRibbonPoint to, float leadingRadius, Span<SticksRibbonQuad> destination)
        {
            int count = AngularSegmentsFor(Math.Max(from.HalfSpan, to.HalfSpan));
            Vector2 previousFrom = from.LeftEdge;
            Vector2 previousTo = to.LeftEdge;
            float previousFromGlow = 1;
            float previousToGlow = 1;

            for (int i = 0; i < count; i++)
            {
                float fraction = (i + 1f) / count;
                Vector2 nextFrom = pointAcross(from, fraction);
                Vector2 nextTo = pointAcross(to, fraction);
                float fromGlow = glowAt(from, fraction, leadingRadius);
                float toGlow = glowAt(to, fraction, leadingRadius);

                destination[i] = new SticksRibbonQuad(
                    new Quad(previousFrom, previousTo, nextFrom, nextTo),
                    new Vector4(previousFromGlow, previousToGlow, fromGlow, toGlow));

                previousFrom = nextFrom;
                previousTo = nextTo;
                previousFromGlow = fromGlow;
                previousToGlow = toGlow;
            }

            return count;
        }

        private static Vector2 pointAcross(SticksRibbonPoint point, float fraction)
            => SticksPlayfield.PointAt(point.Angle + point.HalfSpan * (2 * fraction - 1), point.Radius);

        private static float glowAt(SticksRibbonPoint point, float fraction, float leadingRadius)
        {
            float halfWidth = point.Radius * point.HalfSpan * MathF.PI / 180;
            float sideDistance = halfWidth * 2 * Math.Min(fraction, 1 - fraction);
            float sideGlow = 1 - sideDistance / Math.Max(0.001f, Math.Min(edge_glow_width, halfWidth));
            float leadingGlow = 1 - (leadingRadius - point.Radius) / edge_glow_width;

            // Lighting is part of the fill, so neither the side glow nor the head
            // glow can paint outside the actual ribbon or beyond a short tail.
            return Math.Clamp(Math.Max(sideGlow, leadingGlow), 0, 1);
        }
    }
}
