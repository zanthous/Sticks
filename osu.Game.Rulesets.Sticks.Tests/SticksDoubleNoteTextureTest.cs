using NUnit.Framework;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksDoubleNoteTextureTest
    {
        [Test]
        public void TestDefaultPaletteHasTwoFlatFacetsAndOpaqueRecessedCentre()
        {
            using Image<Rgba32> image = SticksDoubleNoteTexture.CreateImage(SticksPlayfield.OVERLAP_COLOUR);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(pixelAt(image, 12, 0), Is.EqualTo(new Rgba32(0x68, 0x2d, 0x93)));
                Assert.That(pixelAt(image, -12, 0), Is.EqualTo(new Rgba32(0x50, 0x24, 0x77)));
                Assert.That(pixelAt(image, 0, 0), Is.EqualTo(new Rgba32(0x2b, 0x19, 0x3d)), "The white tick is drawn separately.");
                Assert.That(pixelAt(image, 15, 0), Is.EqualTo(new Rgba32(0xc0, 0x5c, 0xff)));
                Assert.That(pixelAt(image, -15, 0), Is.EqualTo(new Rgba32(0xc0, 0x5c, 0xff)));
                Assert.That(pixelAt(image, 0, 17), Is.EqualTo(new Rgba32(0xc0, 0x5c, 0xff)));
            });
        }

        [Test]
        public void TestTextureDimensionsAndTransparentPaddingContainTheOutline()
        {
            using Image<Rgba32> image = SticksDoubleNoteTexture.CreateImage(SticksPlayfield.OVERLAP_COLOUR);
            bool transparentPadding = true;
            bool hasAntialiasedEdge = false;

            for (int y = 0; y < image.Height; y++)
            {
                transparentPadding &= image[0, y].A == 0 && image[image.Width - 1, y].A == 0;
                for (int x = 0; x < image.Width; x++)
                    hasAntialiasedEdge |= image[x, y].A is > 0 and < byte.MaxValue;
            }

            for (int x = 0; x < image.Width; x++)
                transparentPadding &= image[x, 0].A == 0 && image[x, image.Height - 1].A == 0;

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(image.Width, Is.EqualTo(144));
                Assert.That(image.Height, Is.EqualTo(160));
                Assert.That(image.Width / SticksDoubleNoteTexture.DrawWidth, Is.EqualTo(SticksDoubleNoteTexture.Scale));
                Assert.That(image.Height / SticksDoubleNoteTexture.DrawHeight, Is.EqualTo(SticksDoubleNoteTexture.Scale));
                Assert.That(transparentPadding, Is.True);
                Assert.That(hasAntialiasedEdge, Is.True);
                Assert.That(pixelAt(image, 17, 0).A, Is.Zero);
                Assert.That(pixelAt(image, 0, 19).A, Is.Zero);
            });
        }

        [Test]
        public void TestGeometryAndTangentialColoursAreSymmetric()
        {
            using Image<Rgba32> image = SticksDoubleNoteTexture.CreateImage(SticksPlayfield.OVERLAP_COLOUR);
            bool symmetricColour = true;
            bool symmetricAlpha = true;

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    symmetricColour &= image[x, y].Equals(image[x, image.Height - 1 - y]);
                    symmetricAlpha &= image[x, y].A == image[image.Width - 1 - x, y].A;
                }
            }

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(symmetricColour, Is.True, "Each radial-side facet has one flat colour on both tangential halves.");
                Assert.That(symmetricAlpha, Is.True, "The two facet colours share a symmetric diamond silhouette.");
            });
        }

        [Test]
        public void TestPaletteChangesRecolourEveryLayerWithoutChangingOpacity()
        {
            Color4 original = SticksPlayfield.OVERLAP_COLOUR;
            var adjusted = new Color4(original.R * 0.5f, original.G * 0.5f, original.B * 0.5f, 0.1f);
            using Image<Rgba32> normal = SticksDoubleNoteTexture.CreateImage(original);
            using Image<Rgba32> recoloured = SticksDoubleNoteTexture.CreateImage(adjusted);
            bool sameAlpha = true;

            for (int y = 0; y < normal.Height; y++)
            {
                for (int x = 0; x < normal.Width; x++)
                    sameAlpha &= normal[x, y].A == recoloured[x, y].A;
            }

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(pixelAt(recoloured, 15, 0), Is.EqualTo(new Rgba32(96, 46, 128)));
                Assert.That(pixelAt(recoloured, 12, 0), Is.EqualTo(new Rgba32(52, 23, 74)));
                Assert.That(pixelAt(recoloured, -12, 0), Is.EqualTo(new Rgba32(40, 18, 60)));
                Assert.That(pixelAt(recoloured, 0, 0), Is.EqualTo(new Rgba32(21, 12, 30)));
                Assert.That(sameAlpha, Is.True, "Palette alpha must not make the collar translucent.");
            });
        }

        private static Rgba32 pixelAt(Image<Rgba32> image, float radial, float tangent) => image[
            (int)((radial + SticksDoubleNoteTexture.DrawWidth / 2) * SticksDoubleNoteTexture.Scale),
            (int)((tangent + SticksDoubleNoteTexture.DrawHeight / 2) * SticksDoubleNoteTexture.Scale)];
    }
}
