using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    /// <summary>
    /// Rasterises the double-note collar once per palette. Local X points radially outward and
    /// local Y runs along the judgement ring; the separate white aim tick is not part of this image.
    /// </summary>
    internal static class SticksDoubleNoteTexture
    {
        public const int Scale = 4;
        public const int Width = 144;
        public const int Height = 160;
        public const float DrawWidth = 36;
        public const float DrawHeight = 40;

        private const float outer_radial_radius = 15;
        private const float outer_tangent_radius = 17;
        private const float inner_radial_radius = 10;
        private const float inner_tangent_radius = 11;
        private const float outline_half_width = 1.5f;

        public static Image<Rgba32> CreateImage(Color4 overlapColour)
        {
            Rgba32 outline = paletteColour(overlapColour, 0xb8, 0x47, 0xff);
            Rgba32 outwardFacet = paletteColour(overlapColour, 0x64, 0x23, 0x93);
            Rgba32 inwardFacet = paletteColour(overlapColour, 0x4d, 0x1c, 0x77);
            Rgba32 centre = paletteColour(overlapColour, 0x29, 0x13, 0x3d);
            var image = new Image<Rgba32>(Width, Height);

            image.ProcessPixelRows(accessor =>
            {
                for (int pixelY = 0; pixelY < Height; pixelY++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(pixelY);
                    float y = (pixelY + 0.5f) / Scale - DrawHeight / 2;

                    for (int pixelX = 0; pixelX < Width; pixelX++)
                    {
                        float x = (pixelX + 0.5f) / Scale - DrawWidth / 2;
                        float outerDistance = diamondDistance(x, y, outer_radial_radius, outer_tangent_radius);
                        float alpha = coverage(outerDistance, outline_half_width);
                        if (alpha <= 0)
                            continue;

                        Rgba32 facet = x > 0 ? outwardFacet : inwardFacet;
                        float facetCoverage = coverage(outerDistance, -outline_half_width);
                        Rgba32 colour = mix(outline, facet, facetCoverage);
                        float centreCoverage = coverage(diamondDistance(x, y, inner_radial_radius, inner_tangent_radius), 0);
                        colour = mix(colour, centre, centreCoverage);
                        colour.A = toByte(alpha * byte.MaxValue);
                        row[pixelX] = colour;
                    }
                }
            });

            return image;
        }

        // The original artwork used #B847FF. Keep that reference fixed so changing
        // the default overlap colour also recolours the collar outline and facets.
        private static Rgba32 paletteColour(Color4 overlap, byte red, byte green, byte blue) => new Rgba32(
            toByte(overlap.R * byte.MaxValue * red / 0xb8),
            toByte(overlap.G * byte.MaxValue * green / 0x47),
            toByte(overlap.B * blue),
            byte.MaxValue);

        private static byte toByte(float value) => (byte)Math.Clamp(MathF.Round(value), byte.MinValue, byte.MaxValue);

        private static Rgba32 mix(Rgba32 first, Rgba32 second, float amount) => new Rgba32(
            toByte(first.R + (second.R - first.R) * amount),
            toByte(first.G + (second.G - first.G) * amount),
            toByte(first.B + (second.B - first.B) * amount),
            byte.MaxValue);

        // One texture pixel of coverage at an edge. The four-times resolution retains a crisp
        // silhouette when the shared texture is reduced to its playfield dimensions.
        private static float coverage(float signedDistance, float boundary) => Math.Clamp(0.5f + (boundary - signedDistance) * Scale, 0, 1);

        private static float diamondDistance(float x, float y, float radialRadius, float tangentRadius)
        {
            x = Math.Abs(x);
            y = Math.Abs(y);

            // Reflect into one quadrant and project onto its edge. Clamping the projection
            // makes the exterior distance circular at vertices, giving the outline round joins.
            float projection = Math.Clamp(((radialRadius - x) * radialRadius + y * tangentRadius)
                                          / (radialRadius * radialRadius + tangentRadius * tangentRadius), 0, 1);
            float deltaX = x - radialRadius * (1 - projection);
            float deltaY = y - tangentRadius * projection;
            float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
            return x / radialRadius + y / tangentRadius <= 1 ? -distance : distance;
        }
    }
}
