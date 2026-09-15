#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Input;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Framework.Testing.Input;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Skinning;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Tests
{
    public partial class SticksGameplayResourceTest : OsuTestScene
    {
        private SwitchingSticksSkinProvider provider = null!;
        private ManualInputManager controller = null!;
        private DrawableSticksRuleset? gameplay;
        private ManualClock manual = null!;
        private FramedClock clock = null!;
        private readonly List<(string Name, WeakReference Reference)> retired = new List<(string, WeakReference)>();
        private readonly List<Texture> ownedTextures = new List<Texture>();
        private readonly List<Score> retainedScores = new List<Score>();
        private readonly SticksRuleset ruleset = new SticksRuleset();
        private IBeatmap source = null!;
        private readonly List<WeakReference<INativeTexture>> retiredNativeTextures = new List<WeakReference<INativeTexture>>();
        public bool RequireGraphicsRendering { get; set; }

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        private SticksPlayfield playfield => (SticksPlayfield)gameplay!.Playfield;

        [SetUpSteps]
        public void SetUpResources()
        {
            AddStep("create shared gameplay resources", () =>
            {
                Clear();
                disposeTextures();
                retired.Clear();
                retiredNativeTextures.Clear();
                retainedScores.Clear();
                gameplay = null;
                Add(controller = new ManualInputManager { Child = provider = new SwitchingSticksSkinProvider() });
                var beatmap = new Beatmap<SticksHitObject>
                {
                    BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, new BeatmapDifficulty()),
                };
                for (int i = 0; i < 12; i++)
                {
                    var slider = new SticksSlider
                    {
                        StartTime = 1000 + i * 750,
                        Duration = 600,
                        Side = i % 2 == 0 ? StickSide.Left : StickSide.Right,
                        Angle = i * 30,
                    };
                    slider.SetTimedSegments(new[] { 60f, -30f }, new[] { 400d, 200d });
                    beatmap.HitObjects.Add(slider);
                    beatmap.HitObjects.Add(new SticksFlick
                    {
                        StartTime = slider.StartTime + 200,
                        Side = slider.Side == StickSide.Left ? StickSide.Right : StickSide.Left,
                        Angle = i * 30 + 90,
                    });
                    beatmap.HitObjects.Add(new SticksClick { StartTime = slider.StartTime + 650, Side = slider.Side });
                }
                foreach (var note in beatmap.HitObjects)
                    note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
                source = SticksEditorCarrierBeatmap.Create(beatmap, new OsuRuleset().RulesetInfo);
            });
        }

        [Test]
        public void TestRepeatedGameplayExitReleasesPlayfieldsAndReplayRecorders()
        {
            // Keep the cached source, settings, skin provider and completed score objects alive:
            // collecting those too would conceal subscriptions retaining abandoned gameplay.
            for (int i = 0; i < 24; i++)
            {
                int cycle = i;
                AddStep($"load gameplay {i + 1}", createGameplay);
                AddUntilStep("gameplay and note pools loaded", gameplayLoaded);
                AddStep("start recording", () =>
                {
                    var score = new Score { Replay = new Replay() };
                    retainedScores.Add(score);
                    gameplay!.SetRecordTarget(score);
                });
                AddUntilStep("recorder loaded and capturing frames", () => gameplay!.ChildrenOfType<SticksReplayRecorder>().Any(r => r.IsLoaded)
                    && retainedScores[^1].Replay.Frames.Count > 0);
                AddStep("exercise gameplay effects and advance", () =>
                {
                    controller.PressJoystickButton(JoystickButton.GamePadLeftStick);
                    controller.PressJoystickButton(JoystickButton.GamePadRightShoulder);
                    exerciseEffects(100);
                    // Mix early retries with leaving after the last object.
                    manual.CurrentTime = cycle % 2 == 0 ? 1500 : 12000;
                    clock.ProcessFrame();
                });
                AddWaitStep("process gameplay before exit", 3);
                AddStep("exit with a pending skin refresh", () =>
                {
                    controller.ReleaseJoystickButton(JoystickButton.GamePadLeftStick);
                    controller.ReleaseJoystickButton(JoystickButton.GamePadRightShoulder);
                    provider.Switch(new SticksTestSkin());
                    retireGameplay();
                });
                AddWaitStep("drain disposal and draw queues", 5);
            }

            AddStep("change shared skin after all exits", () => provider.Switch(new SticksTestSkin()));
            AddWaitStep("drain final callbacks", 10);
            AddStep("all gameplay instances and recorders are collectible", assertRetiredCollected);
            AddAssert("shared score history remains intact", () => retainedScores.Count == 24
                && retainedScores.All(score => score.Replay.Frames.Count > 0));
        }

        [Test]
        public void TestSustainedGameplayTrailsAndEffectsHaveBoundedMemory()
        {
            long before = 0;
            Drawable[] effectDrawables = Array.Empty<Drawable>();
            AddStep("load gameplay", createGameplay);
            AddUntilStep("gameplay loaded", gameplayLoaded);
            AddRepeatStep("warm trail stamps, particles and contact effects", () => exerciseEffects(500), 5);
            AddStep("measure warmed gameplay", () =>
            {
                effectDrawables = effectChildren();
                collectGarbage();
                before = GC.GetTotalMemory(false);
            });
            AddRepeatStep("exercise both trails and hit effects", () => exerciseEffects(1000), 60);
            AddStep("check stable resources after 60,000 hits", () =>
            {
                collectGarbage();
                long growth = GC.GetTotalMemory(false) - before;
                TestContext.Progress.WriteLine($"Gameplay effects retained {growth / 1048576d:0.00} MiB after 60,000 hits and 120,000 trail positions.");
                Assert.That(effectChildren(), Is.EqualTo(effectDrawables), "Sustained gameplay must reuse its effect drawables.");
                Assert.That(growth, Is.LessThan(8L * 1024 * 1024));
            });
            AddStep("let hit effects expire", () =>
            {
                manual.CurrentTime += 2000;
                clock.ProcessFrame();
            });
            AddUntilStep("perfect contacts expired", () => gameplay!.ChildrenOfType<SticksPerfectContactLayer>().Single().ActiveCount == 0);
            AddStep("exit stressed gameplay", () =>
            {
                effectDrawables = Array.Empty<Drawable>();
                retireGameplay();
            });
            AddWaitStep("settle teardown", 10);
            AddStep("stressed gameplay is collectible", assertRetiredCollected);
        }

        [Test]
        public void TestRepeatedGameplaySkinSwitchReleasesOldTextures()
        {
            WeakReference? cachedLastTexture = null;
            AddStep("load gameplay", createGameplay);
            AddUntilStep("gameplay loaded", gameplayLoaded);
            AddStep("report renderer", () =>
            {
                TestContext.Progress.WriteLine($"Gameplay skin resource test renderer: {renderer.GetType().FullName}");
                if (RequireGraphicsRendering)
                    Assert.That(renderer, Is.Not.InstanceOf<DummyRenderer>(), "The graphics run must actually render textures.");
            });
            for (int i = 0; i < 32; i++)
            {
                int cycle = i;
                AddStep($"replace gameplay skin {i + 1}", () => replaceSkin(cycle));
                AddUntilStep("new cursor and trail assets active", () => gameplay!.ChildrenOfType<SticksSkinnedSprite>()
                    .Where(s => s.Name == "sticks-cursor-left" || s.Name == "sticks-cursor-right")
                    .All(s => s.Texture == ownedTextures[^1])
                    && gameplay.ChildrenOfType<SticksCursorTrail>().All(t => t.UsesSkinTexture));
                AddStep("draw new trail and hit effects", () => exerciseEffects(100));
                AddUntilStep("texture uploaded and used by renderer", () => renderer is DummyRenderer
                    || nativeTexture(ownedTextures[^1]).TotalBindCount > 0);
                AddWaitStep("allow draw buffers to stop using previous textures", 5);
                AddStep("release previous skin resources", releaseOldTextures);
            }
            AddStep("restore built-in gameplay skin", () => provider.Switch(new SticksTestSkin()));
            AddUntilStep("custom trails no longer active", () => gameplay!.ChildrenOfType<SticksCursorTrail>().All(t => !t.UsesSkinTexture));
            AddWaitStep("drain old draw states", 10);
            AddStep("release last custom texture", () =>
            {
                foreach (var texture in ownedTextures)
                {
                    cachedLastTexture = track(texture, "skin texture");
                    retiredNativeTextures.Add(new WeakReference<INativeTexture>(nativeTexture(texture)));
                }
                disposeTextures();
            });
            AddWaitStep("drain texture disposal", 10);
            // Hidden Sprite draw nodes can retain their last Texture wrapper until reused or
            // disposed. It must be only that last wrapper, with no live native allocation.
            AddStep("obsolete skins and textures are collectible", () => assertRetiredCollected(cachedLastTexture));
            AddUntilStep("all custom native textures released", () => renderer is DummyRenderer
                || retiredNativeTextures.All(reference => !reference.TryGetTarget(out var texture) || !texture.Available));
            AddStep("report native resource release", () => TestContext.Progress.WriteLine(renderer is DummyRenderer
                ? "Native graphics allocations not exercised by headless renderer."
                : $"All {retiredNativeTextures.Count} uploaded skin textures released their native resources."));
            AddStep("exit after skin switching", retireGameplay);
            AddWaitStep("settle gameplay teardown", 10);
            AddStep("skin consumers are collectible", assertRetiredCollected);
        }

        private void createGameplay()
        {
            manual = new ManualClock { CurrentTime = 1200 };
            clock = new FramedClock(manual);
            clock.ProcessFrame();
            // Player reconverts the cached source for each attempt. Reusing played objects
            // would retain upstream lifetime entries attached to those objects.
            var playable = new SticksBeatmapConverter(source, ruleset).Convert();
            foreach (var note in playable.HitObjects)
                note.ApplyDefaults(playable.ControlPointInfo, playable.Difficulty);
            gameplay = new DrawableSticksRuleset(ruleset, playable) { Clock = clock };
            provider.Add(gameplay);
        }

        private bool gameplayLoaded() => gameplay!.IsLoaded && gameplay.ChildrenOfType<DrawableSticksSlider>().Any(s => s.IsLoaded)
            && gameplay.ChildrenOfType<SticksCursorTrail>().Count(t => t.IsLoaded) == 2;

        private void exerciseEffects(int count)
        {
            // Keep the actual gameplay cursors active so trail draw nodes are exercised too.
            var input = (SticksReplayInputProvider)typeof(DrawableSticksRuleset)
                .GetField("replayInputProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gameplay)!;
            input.Update(Vector2.UnitX, -Vector2.UnitX);
            playfield.ShowCursorTrails = true;
            playfield.HitEffects = SticksHitEffectMode.Always;
            var trails = gameplay!.ChildrenOfType<SticksCursorTrail>().ToArray();
            var contacts = gameplay.ChildrenOfType<SticksPerfectContactLayer>().Single();
            for (int i = 0; i < count; i++)
            {
                float angle = i * 37 % 360;
                foreach (var trail in trails)
                {
                    trail.Alpha = 1;
                    trail.AddPosition(playfield.ToScreenSpace(SticksPlayfield.PointAt(angle, SticksPlayfield.GUIDE_RADIUS)));
                }
                contacts.Trigger(angle, 27.5f, Color4.White);
                playfield.TriggerContactBurst(i % 2 == 0 ? StickSide.Left : StickSide.Right, angle, 27.5f, i % 3 == 0);
            }
        }

        private Drawable[] effectChildren() => gameplay!.ChildrenOfType<SticksContactBurstEffect>().Cast<Drawable>()
            .Concat(gameplay.ChildrenOfType<SticksContactParticleEmitter>())
            .Concat(gameplay.ChildrenOfType<SticksPerfectContactLayer>())
            .Concat(gameplay.ChildrenOfType<SticksCursorTrail>()).ToArray();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void retireGameplay()
        {
            track(gameplay!, "drawable ruleset");
            track(playfield, "playfield");
            track(gameplay!.Beatmap, "playable beatmap");
            foreach (var child in gameplay!.ChildrenOfType<SticksReplayRecorder>())
                track(child, "replay recorder");
            foreach (var child in effectChildren())
                track(child, child.GetType().Name);
            Assert.That(provider.Remove(gameplay, true), Is.True);
            gameplay = null;
        }

        private void replaceSkin(int cycle)
        {
            var texture = new DisposableTexture(renderer.CreateTexture(cycle % 2 == 0 ? 128 : 256, 128));
            texture.SetData(new ArrayPoolTextureUpload(texture.Width, texture.Height));
            ownedTextures.Add(texture);
            var skin = new SticksTestSkin();
            foreach (string name in new[] { "sticks-playfield", "sticks-playfield-background", "sticks-cursor-left", "sticks-cursor-right",
                         "sticks-cursortrail-left", "sticks-cursortrail-right", "sticks-note-centre", "sticks-click", "sticks-slider-head", "sticks-slider-reversal" })
                skin.Textures[name] = texture;
            track(skin, "skin");
            provider.Switch(skin);
        }

        // Native handles are internal in the framework; inspect them only to verify
        // uploads really occurred and disposal released their backing allocations.
        private static INativeTexture nativeTexture(Texture texture) => (INativeTexture)typeof(Texture)
            .GetProperty("NativeTexture", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(texture)!;

        private void releaseOldTextures()
        {
            while (ownedTextures.Count > 1)
            {
                var texture = ownedTextures[0];
                track(texture, "skin texture");
                retiredNativeTextures.Add(new WeakReference<INativeTexture>(nativeTexture(texture)));
                texture.Dispose();
                ownedTextures.RemoveAt(0);
            }
        }

        private WeakReference track(object value, string name)
        {
            var reference = new WeakReference(value);
            retired.Add((name, reference));
            return reference;
        }

        private void assertRetiredCollected() => assertRetiredCollected(null);

        private void assertRetiredCollected(WeakReference? cachedTexture)
        {
            collectGarbage();
            var survivors = retired.Where(item => item.Reference.IsAlive && item.Reference != cachedTexture).Select(item => item.Name).ToArray();
            TestContext.Progress.WriteLine($"Retained {survivors.Length}/{retired.Count} retired gameplay resources" +
                (cachedTexture?.IsAlive == true ? " (plus the last texture wrapper cached by hidden draw nodes)." : "."));
            Assert.That(survivors, Is.Empty, string.Join(", ", survivors.GroupBy(name => name).Select(group => $"{group.Key}: {group.Count()}")));
        }

        private static void collectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        [TearDownSteps]
        public void TearDownResources() => AddStep("dispose remaining gameplay and test textures", () =>
        {
            Clear();
            gameplay = null;
            disposeTextures();
            retainedScores.Clear();
        });

        private void disposeTextures()
        {
            foreach (var texture in ownedTextures)
                texture.Dispose();
            ownedTextures.Clear();
        }
    }
}
