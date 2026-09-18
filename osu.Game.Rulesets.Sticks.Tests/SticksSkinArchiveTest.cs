#nullable enable

using System.IO;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.IO;
using osu.Game.IO.Archives;
using osu.Game.Rulesets.Sticks.Skinning;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    public partial class SticksSkinArchiveTest : OsuTestScene
    {
        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [Test]
        public void TestExampleArchiveLoadsThroughLegacySkin()
        {
            AddStep("load example archive with real skin and texture decoders", () =>
            {
                using Stream stream = typeof(SticksSkinArchiveTest).Assembly.GetManifestResourceStream(
                    "osu.Game.Rulesets.Sticks.Tests.Resources.SticksMinimal.osk")!;
                Assert.That(stream, Is.Not.Null);
                using var archive = new ZipArchiveReader(stream, "Sticks Minimal.osk");
                using var skin = new ArchiveSkin(skins, archive);

                foreach (var (name, size) in new[]
                         {
                             ("sticks-playfield", new Vector2(640)),
                             ("sticks-playfield-background", new Vector2(640)),
                             ("sticks-cursor-left", new Vector2(28)),
                             ("sticks-cursor-right", new Vector2(28)),
                             ("sticks-cursortrail-left", new Vector2(18)),
                             ("sticks-cursortrail-right", new Vector2(18)),
                             ("sticks-note-centre", new Vector2(22)),
                             ("sticks-slider-head", new Vector2(22)),
                             ("sticks-slider-reversal", new Vector2(22)),
                             ("sticks-double-note", new Vector2(36, 40)),
                             ("sticks-judgement", new Vector2(10)),
                             ("sticks-click", new Vector2(465)),
                         })
                {
                    Assert.That(archive.Filenames, Does.Contain($"{name}@2x.png"));
                    Texture? texture = SticksSkinTextureLookup.Get(skin, name);
                    Assert.That(texture, Is.Not.Null, name);
                    NUnitCompatibility.Multiple(() =>
                    {
                        Assert.That(texture!.DisplaySize, Is.EqualTo(size), $"{name} logical size");
                        Assert.That(texture.Width, Is.EqualTo(size.X * 2), $"{name} image width");
                        Assert.That(texture.Height, Is.EqualTo(size.Y * 2), $"{name} image height");
                    });
                }

                NUnitCompatibility.Multiple(() =>
                {
                    Assert.That(SticksSkinTextureLookup.Get(skin, "sticks-missing"), Is.Null);
                    Assert.That(colour(skin, "SticksLeft"), Is.EqualTo(new Color4((byte)51, (byte)190, (byte)234, byte.MaxValue)));
                    Assert.That(colour(skin, "SticksRight"), Is.EqualTo(new Color4((byte)255, (byte)103, (byte)139, byte.MaxValue)));
                    Assert.That(colour(skin, "SticksOverlap"), Is.EqualTo(new Color4((byte)192, (byte)92, (byte)255, byte.MaxValue)));
                });
            });
        }

        private static Color4? colour(ISkin skin, string name) =>
            skin.GetConfig<SkinCustomColourLookup, Color4>(new SkinCustomColourLookup(name))?.Value;

        private sealed class ArchiveSkin : LegacySkin
        {
            public ArchiveSkin(IStorageResourceProvider resources, ArchiveReader archive)
                : base(new SkinInfo { Name = "Sticks Minimal" }, resources, archive)
            {
            }
        }
    }
}
