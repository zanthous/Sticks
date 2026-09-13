#nullable enable

using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Skinning;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.Sticks.Tests
{
    public partial class SticksEditorSkinningTest : EditorTestScene
    {
        private SwitchingSticksSkinProvider skinSource = null!;
        private readonly List<Texture> ownedTextures = new List<Texture>();

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        protected override Ruleset CreateEditorRuleset() => new SticksRuleset();

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs<ISkinSource>(skinSource = new SwitchingSticksSkinProvider(dependencies.Get<ISkinSource>()));
            return dependencies;
        }

        [Test]
        public void TestExistingNotesAndPlacementPreviewFollowSkinChangesAndSeeking()
        {
            Texture first = null!;
            Texture replacement = null!;
            SticksFlick note = null!;
            SticksPlayfield playfield() => this.ChildrenOfType<SticksPlayfield>().Single();
            DrawableSticksFlick head() => playfield().AllHitObjects.OfType<DrawableSticksFlick>().Single();
            SticksSkinnedSprite headImage() => head().ChildrenOfType<SticksSkinnedSprite>().Single(s => s.Name == "sticks-note-centre");
            SticksFlickPlacementBlueprint placement() => this.ChildrenOfType<SticksFlickPlacementBlueprint>().Single();

            AddStep("skin paused editor and add note", () =>
            {
                EditorClock.Stop();
                EditorBeatmap.Clear();
                EditorBeatmap.ControlPointInfo.Clear();
                EditorBeatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                EditorClock.Seek(2000);
                first = renderer.CreateTexture(32, 32);
                replacement = renderer.CreateTexture(64, 64);
                ownedTextures.Add(first);
                ownedTextures.Add(replacement);
                skinSource.Switch(new SticksTestSkin
                {
                    Textures = { ["sticks-note-centre"] = first, ["sticks-playfield"] = first },
                });
                note = new SticksFlick { StartTime = 2000, Angle = 60, Side = StickSide.Left };
                note.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(note);
            });
            AddUntilStep("editor renders custom head and ring", () => playfield().AllHitObjects.OfType<DrawableSticksFlick>().Count() == 1
                && head().ChildrenOfType<SticksSkinnedSprite>().Any(s => s.Texture == first)
                && playfield().ChildrenOfType<SticksSkinnedSprite>().Any(s => s.Name == "sticks-playfield" && s.Texture == first));
            AddStep("choose flick tool", () =>
            {
                InputManager.PressKey(Key.Number2);
                InputManager.ReleaseKey(Key.Number2);
                InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(120, 260)));
            });
            AddUntilStep("placement preview uses same skin", () => this.ChildrenOfType<SticksFlickPlacementBlueprint>()
                .Any(p => p.ChildrenOfType<SticksSkinnedSprite>().Any(s => s.Texture == first)));
            AddStep("switch while a placement preview is active", () => skinSource.Switch(new SticksTestSkin
            {
                Textures = { ["sticks-note-centre"] = replacement },
            }));
            AddUntilStep("both existing and preview heads refresh", () => headImage().Texture == replacement
                && placement().ChildrenOfType<SticksSkinnedSprite>().Any(s => s.Texture == replacement)
                && !playfield().ChildrenOfType<SticksSkinnedSprite>().Any(s => s.Texture == first));
            AddStep("stop placing and scrub backwards", () =>
            {
                InputManager.PressKey(Key.Number1);
                InputManager.ReleaseKey(Key.Number1);
                EditorClock.Seek(1000);
            });
            AddStep("edit note and return to hit time", () =>
            {
                note.Angle = 90;
                EditorBeatmap.Update(note);
                EditorClock.Seek(2000);
            });
            AddUntilStep("rebuilt preview keeps new skin at correct hit radius", () => headImage().Texture == replacement
                && head().ChildrenOfType<SticksArcMarker>().Single().Angle == 90
                && System.Math.Abs(head().ChildrenOfType<SticksArcMarker>().Single().DisplayedRadialOffset
                                   + SticksPlayfield.RadiusFor(StickSide.Left) - SticksPlayfield.GUIDE_RADIUS) < 0.001);
            AddAssert("editing and skin changes do not judge the note", () => !head().Judged);
            AddStep("restore ordinary skin", () => skinSource.Switch(new SticksTestSkin()));
            AddUntilStep("editor restores built-in artwork", () => !headImage().UsesSkinTexture);
            AddStep("restore custom skin after centre was hidden", () => skinSource.Switch(new SticksTestSkin
            {
                Textures = { ["sticks-note-centre"] = first },
            }));
            AddUntilStep("hidden optional centre reloads and becomes visible", () => headImage().Texture == first && headImage().Alpha == 1);
        }

        [TearDownSteps]
        public override void TearDownSteps()
        {
            base.TearDownSteps();
            AddStep("reset editor skin", () => skinSource.Switch(new SticksTestSkin()));
            AddWaitStep("release old skin references", 3);
            AddStep("dispose test skin textures", disposeTextures);
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
            skinSource?.Dispose();
            disposeTextures();
        }
    }
}
