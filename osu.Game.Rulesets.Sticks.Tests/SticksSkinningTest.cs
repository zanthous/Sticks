#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Skinning;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    public partial class SticksSkinningTest : OsuTestScene
    {
        private readonly List<Texture> ownedTextures = new List<Texture>();

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        [Test]
        public void TestLiveReplacementRestoresFallbackAndUnsubscribesOnDisposal()
        {
            SwitchingSticksSkinProvider provider = null!;
            SticksSkinnedSprite slot = null!;
            Box fallback = null!;
            Texture first = null!;
            Texture second = null!;
            int changes = 0;
            int changesBeforeDisposal = 0;

            AddStep("load procedural fallback", () =>
            {
                Clear();
                first = createTexture(32, 48);
                second = createTexture(96, 64);
                Add(provider = new SwitchingSticksSkinProvider
                {
                    Child = slot = new SticksSkinnedSprite("sticks-cursor-left",
                        fallback = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0.4f })
                    {
                        Size = new Vector2(24),
                    },
                });
                slot.OnSkinChanged += () => changes++;
            });
            AddUntilStep("fallback loaded", () => slot.IsLoaded && !slot.UsesSkinTexture && fallback.IsPresent);
            AddStep("provide left cursor image", () => provider.Switch(new SticksTestSkin
            {
                Textures = { ["sticks-cursor-left"] = first },
            }));
            AddUntilStep("left image loaded", () => slot.Texture == first);
            AddAssert("fallback keeps its own opacity while hidden", () => fallback.Alpha == 0.4f && !fallback.Parent!.IsPresent);
            AddAssert("image dimensions cannot resize the slot", () => slot.Size == new Vector2(24));

            AddStep("switch to replacement cursor skin", () => provider.Switch(new SticksTestSkin
            {
                Textures = { ["sticks-cursor-left"] = second },
            }));
            AddUntilStep("replacement image loaded", () => slot.Texture == second);
            AddStep("switch to skin without sticks images", () => provider.Switch(new SticksTestSkin()));
            AddUntilStep("original fallback restored", () => !slot.UsesSkinTexture && slot.Texture == null && fallback.Parent!.IsPresent);
            AddAssert("fallback opacity is preserved", () => fallback.Alpha == 0.4f);

            AddStep("remove and dispose consumer", () =>
            {
                provider.Remove(slot, true);
                changesBeforeDisposal = changes;
            });
            AddStep("change skin after removal", () => provider.Switch(new SticksTestSkin
            {
                Textures = { ["sticks-cursor-left"] = first },
            }));
            AddWaitStep("allow queued skin refreshes", 3);
            AddAssert("disposed consumer receives no refresh", () => changes == changesBeforeDisposal);
        }

        [Test]
        public void TestGameplaySkinAndPaletteSwitchWithoutMovingHitGeometry()
        {
            SwitchingSticksSkinProvider provider = null!;
            DrawableSticksRuleset drawable = null!;
            SticksTestSkin firstSkin = null!;
            SticksTestSkin secondSkin = null!;
            Texture first = null!;
            Texture second = null!;
            ManualClock manual = null!;
            FramedClock clock = null!;
            float originalHitAngle = 0;
            var customLeft = Color4.Cyan;
            var customRight = Color4.Yellow;
            var personalLeft = Color4.Orange;
            var personalRight = Color4.Green;
            var personalOverlap = Color4.Pink;

            SticksPlayfield playfield() => (SticksPlayfield)drawable.Playfield;
            DrawableSticksFlick flick() => drawable.ChildrenOfType<DrawableSticksFlick>().Single();
            DrawableSticksClick click() => drawable.ChildrenOfType<DrawableSticksClick>().Single();
            DrawableSticksSliderRepeat repeat() => drawable.ChildrenOfType<DrawableSticksSliderRepeat>().Single(r => r.HitObject != null && r.IsAlive);
            SticksSkinnedSprite slot(string name) => (name == "sticks-note-centre" ? flick() : (Drawable)drawable)
                .ChildrenOfType<SticksSkinnedSprite>().Single(s => s.Name == name);

            AddStep("load actual gameplay under custom skin", () =>
            {
                Clear();
                first = createTexture(640, 640);
                second = createTexture(1280, 1280);
                firstSkin = new SticksTestSkin
                {
                    Textures =
                    {
                        ["sticks-playfield"] = first,
                        ["sticks-playfield-background"] = first,
                        ["sticks-cursor-left"] = first,
                        ["sticks-cursor-right"] = second,
                        ["sticks-cursortrail-left"] = first,
                        ["sticks-cursortrail-right"] = second,
                        ["sticks-note-centre"] = first,
                        ["sticks-click"] = first,
                        ["sticks-slider-head"] = first,
                        ["sticks-slider-reversal"] = first,
                    },
                    Colours = { ["SticksLeft"] = customLeft, ["SticksRight"] = customRight, ["SticksOverlap"] = Color4.Violet },
                };
                secondSkin = new SticksTestSkin
                {
                    Textures =
                    {
                        ["sticks-playfield"] = second,
                        ["sticks-cursor-right"] = second,
                        ["sticks-note-centre"] = second,
                    },
                    Colours = { ["SticksLeft"] = Color4.Lime, ["SticksRight"] = Color4.Magenta },
                };
                var ruleset = new SticksRuleset();
                var beatmap = new Beatmap<SticksHitObject>
                {
                    BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, new BeatmapDifficulty()),
                };
                var slider = new SticksSlider { StartTime = 2000, Side = StickSide.Right, Angle = 180, Duration = 800 };
                slider.SetCustomSegments(new[] { 90f, -90f });
                foreach (SticksHitObject note in new SticksHitObject[]
                         {
                             new SticksFlick { StartTime = 2000, Side = StickSide.Left, Angle = 60 },
                             new SticksClick { StartTime = 2000, Side = StickSide.Right },
                             slider,
                         })
                {
                    note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
                    beatmap.HitObjects.Add(note);
                }
                originalHitAngle = beatmap.HitObjects[0].PrimaryHitAngle;
                manual = new ManualClock { CurrentTime = 2000 };
                clock = new FramedClock(manual);
                clock.ProcessFrame();
                provider = new SwitchingSticksSkinProvider { Child = drawable = new DrawableSticksRuleset(ruleset, beatmap) { Clock = clock } };
                provider.Switch(firstSkin);
                Add(provider);
            });
            AddUntilStep("gameplay assets loaded", () => drawable.ChildrenOfType<DrawableSticksFlick>().Any()
                                                        && drawable.ChildrenOfType<DrawableSticksClick>().Any()
                                                        && drawable.ChildrenOfType<SticksSkinnedSprite>().Count(s => s.Texture == first) >= 5);
            AddStep("use skin colours with personal fallback", () =>
            {
                playfield().SetColours(personalLeft, personalRight, personalOverlap);
                playfield().UseSkinColours = true;
            });
            AddUntilStep("skin palette active", () => playfield().ColourFor(StickSide.Left) == customLeft
                                                      && playfield().ColourFor(StickSide.Right) == customRight);
            AddStep("verify image roles and real hit geometry", () =>
            {
                NUnitCompatibility.Multiple(() =>
                {
                    Assert.That(slot("sticks-playfield").Size, Is.EqualTo(new Vector2(SticksPlayfield.SIZE)));
                    Assert.That(slot("sticks-cursor-left").Texture, Is.SameAs(first));
                    Assert.That(slot("sticks-cursor-right").Texture, Is.SameAs(second));
                    Assert.That(playfield().LeftStickCursor.Size, Is.EqualTo(new Vector2(24)));
                    Assert.That(playfield().RightStickCursor.Size, Is.EqualTo(new Vector2(24)));
                    Assert.That(drawable.ChildrenOfType<SticksCursorTrail>().Count(t => t.UsesSkinTexture), Is.EqualTo(2));
                    Assert.That(flick().HitObject.PrimaryHitAngle, Is.EqualTo(originalHitAngle));
                    Assert.That(flick().ChildrenOfType<SticksArcMarker>().Single().DisplayedRadialOffset
                                + SticksPlayfield.RadiusFor(StickSide.Left), Is.EqualTo(SticksPlayfield.GUIDE_RADIUS).Within(0.001));
                    Assert.That(click().ChildrenOfType<SticksSkinnedSprite>().Single().Texture, Is.SameAs(first));
                    Assert.That((playfield().ToLocalSpace(slot("sticks-note-centre").ScreenSpaceDrawQuad.Centre)
                                 - new Vector2(SticksPlayfield.SIZE / 2)).Length, Is.EqualTo(SticksPlayfield.GUIDE_RADIUS).Within(0.001));
                    Assert.That(slot("sticks-click").DrawWidth / SticksClickHalo.SKIN_DIAMETER * SticksPlayfield.GUIDE_RADIUS,
                        Is.EqualTo(SticksPlayfield.GUIDE_RADIUS).Within(0.001));
                });
            });
            AddStep("advance into reversal approach", () =>
            {
                manual.CurrentTime = 2050;
                clock.ProcessFrame();
            });
            AddUntilStep("reversal skin is actually visible", () => drawable.ChildrenOfType<DrawableSticksSliderRepeat>()
                .Any(r => r.Alpha > 0 && r.ChildrenOfType<SticksSliderHeadMarker>().Any(m => m.Alpha > 0)
                                     && r.ChildrenOfType<SticksSkinnedSprite>().Any(s => s.Texture == first && s.Alpha > 0)));
            AddStep("reversal art follows its own timestamp and turn direction", () =>
            {
                var marker = repeat().ChildrenOfType<SticksSliderHeadMarker>().Single();
                var image = repeat().ChildrenOfType<SticksSkinnedSprite>().Single();
                float radius = SticksPlayfield.GUIDE_RADIUS * SticksPlayfield.CenterOutProgressAt(
                    manual.CurrentTime, repeat().HitObject.StartTime, repeat().HitObject.ApproachDuration);
                NUnitCompatibility.Multiple(() =>
                {
                    Assert.That(slot("sticks-slider-head").Texture, Is.SameAs(first));
                    Assert.That(repeat().HitObject.StartTime, Is.EqualTo(2400));
                    Assert.That(marker.Angle, Is.EqualTo(270));
                    Assert.That(image.Rotation, Is.EqualTo(-90));
                    Assert.That((playfield().ToLocalSpace(image.ScreenSpaceDrawQuad.Centre)
                                 - new Vector2(SticksPlayfield.SIZE / 2)).Length, Is.EqualTo(radius).Within(0.001));
                });
            });
            AddStep("use personal colours", () => playfield().UseSkinColours = false);
            AddUntilStep("personal palette restored", () => playfield().ColourFor(StickSide.Left) == personalLeft
                                                           && playfield().ColourFor(StickSide.Right) == personalRight);
            AddStep("switch skin while personal palette is selected", () => provider.Switch(secondSkin));
            AddUntilStep("new images replace all old images", () => slot("sticks-playfield").Texture == second
                                                                 && !slot("sticks-cursor-left").UsesSkinTexture
                                                                 && !drawable.ChildrenOfType<SticksSkinnedSprite>().Any(s => s.Texture == first));
            AddAssert("skin switch respects personal palette", () => playfield().ColourFor(StickSide.Left) == personalLeft);
            AddAssert("missing click art falls back", () => !click().ChildrenOfType<SticksSkinnedSprite>().Single().UsesSkinTexture);
            AddAssert("missing trail images restore built-in trails", () => drawable.ChildrenOfType<SticksCursorTrail>().All(t => !t.UsesSkinTexture));
            AddAssert("missing reversal art restores the default hidden marker", () =>
                repeat().ChildrenOfType<SticksSliderHeadMarker>().Single().Alpha == 0);
            AddStep("re-enable skin palette", () => playfield().UseSkinColours = true);
            AddUntilStep("new palette and missing-colour fallback used", () => playfield().ColourFor(StickSide.Left) == Color4.Lime
                                                                              && playfield().OverlapColour == personalOverlap);
            AddAssert("hit radius and tolerance unchanged", () => flick().HitObject.PrimaryHitAngle == originalHitAngle
                && Math.Abs(flick().ChildrenOfType<SticksArcMarker>().Single().DisplayedRadialOffset
                            + SticksPlayfield.RadiusFor(StickSide.Left) - SticksPlayfield.GUIDE_RADIUS) < 0.001);
        }

        [TearDownSteps]
        public void TearDownSkinResources() => AddStep("dispose skin resources", () =>
        {
            Clear();
            disposeTextures();
        });

        private Texture createTexture(int width, int height)
        {
            Texture texture = renderer.CreateTexture(width, height);
            ownedTextures.Add(texture);
            return texture;
        }

        private void disposeTextures()
        {
            foreach (Texture texture in ownedTextures)
                texture.Dispose();
            ownedTextures.Clear();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            disposeTextures();
        }
    }

    internal partial class SwitchingSticksSkinProvider : SkinProvidingContainer
    {
        private readonly ISkinSource? fallback;

        public SwitchingSticksSkinProvider(ISkinSource? fallback = null)
        {
            this.fallback = fallback;
            Switch(new SticksTestSkin());
        }

        public void Switch(ISkin skin)
        {
            SetSources(fallback == null ? new[] { skin } : new ISkin[] { skin, fallback });
            TriggerSourceChanged();
        }
    }

    internal sealed class SticksTestSkin : ISkin
    {
        public Dictionary<string, Texture> Textures { get; } = new Dictionary<string, Texture>();
        public Dictionary<string, Color4> Colours { get; } = new Dictionary<string, Color4>();

        public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => null;
        public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => Textures.GetValueOrDefault(componentName);
        public ISample? GetSample(ISampleInfo sampleInfo) => null;

        public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
            where TLookup : notnull
            where TValue : notnull
        {
            if (typeof(TValue) == typeof(Color4) && lookup is SkinCustomColourLookup custom
                && Colours.TryGetValue(custom.Lookup.ToString()!, out Color4 colour))
                return (IBindable<TValue>)(object)new Bindable<Color4>(colour);

            return null;
        }
    }
}
