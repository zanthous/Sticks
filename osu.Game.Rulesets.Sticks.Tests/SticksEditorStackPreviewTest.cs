#nullable enable

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Sticks.Tests
{
    public partial class SticksEditorStackPreviewTest : EditorTestScene
    {
        private SticksFlick left = null!;
        private SticksFlick right = null!;

        protected override Ruleset CreateEditorRuleset() => new SticksRuleset();

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();
            AddStep("clear and pause", () =>
            {
                EditorClock.Stop();
                EditorBeatmap.Clear();
                EditorClock.Seek(0);
                left = new SticksFlick { StartTime = 2000, Side = StickSide.Left, Angle = 40 };
                right = new SticksFlick { StartTime = 2000, Side = StickSide.Right, Angle = 40 };
                left.EnsureLegacyEditorMarker();
                right.EnsureLegacyEditorMarker();
            });
        }

        [Test]
        public void TestPausedEditingAndSeekingNeverHitsOneSideOfStack()
        {
            AddStep("add left note", () => EditorBeatmap.Add(left));
            AddStep("scrub to its beat", () => EditorClock.Seek(2000));
            AddUntilStep("first note remains unjudged", () => heads().Length == 1 && !heads()[0].Judged);
            AddStep("add right note at same beat", () => EditorBeatmap.Add(right));
            AddUntilStep("stack is purple immediately", () => heads().Length == 2 && visibleOverlapCount() == 1);
            AddAssert("both heads still unjudged", () => heads().All(head => !head.Judged));
            AddAssert("paused preview has no gameplay cursors", cursorsHidden);

            foreach (double time in new[] { 1900d, 2000, 1700, 2000 })
            {
                AddStep($"scrub to {time}", () => EditorClock.Seek(time));
                AddWaitStep("settle", 2);
                AddAssert("both source heads stay visible together", () => visibleOverlapCount() == 1 && heads().All(head => !head.Judged));
                AddAssert("no cursor masks the note", cursorsHidden);
            }

            AddStep("remove right note", () => EditorBeatmap.Remove(right));
            AddUntilStep("purple disappears with partner", () => heads().Length == 1 && visibleOverlapCount() == 0);
            AddStep("restore right note", () => EditorBeatmap.Add(right));
            AddUntilStep("purple returns with partner", () => heads().Length == 2 && visibleOverlapCount() == 1);
            AddStep("move right note apart", () =>
            {
                right.Angle = 130;
                EditorBeatmap.Update(right);
            });
            AddUntilStep("separated notes remain their own colours", () => visibleOverlapCount() == 0);
            AddStep("move right note back", () =>
            {
                right.Angle = 40;
                EditorBeatmap.Update(right);
            });
            AddUntilStep("edited stack is purple", () => visibleOverlapCount() == 1);
        }

        [Test]
        public void TestStackAtExactBeatRemainsPurpleAfterPreviewAlreadyHitIt()
        {
            AddStep("add first note", () =>
            {
                EditorBeatmap.Add(left);
                EditorClock.Seek(1850);
            });
            AddUntilStep("note loaded", () => heads().Length == 1);
            AddStep("play preview", () => EditorClock.Start());
            AddUntilStep("preview plays original note", () => EditorClock.CurrentTime >= 2070 && heads().All(head => head.IsHit));
            AddStep("pause at exact original timestamp and add partner", () =>
            {
                EditorClock.Stop();
                EditorClock.Seek(2000);
                EditorBeatmap.Add(right);
            });
            AddUntilStep("played head and new partner form purple stack", () => heads().Length == 2 && visibleOverlapCount() == 1);
            AddAssert("new partner was not consumed by scrub input", () => !heads().Single(head => head.HitObject.Side == StickSide.Right).Judged);
            AddAssert("cursor does not obscure stack", cursorsHidden);

            AddStep("rewind before both notes", () => EditorClock.Seek(1850));
            AddUntilStep("both heads reset", () => heads().All(head => !head.Judged));
            AddStep("play complete pair", () => EditorClock.Start());
            AddUntilStep("running preview still plays both heads", () => EditorClock.CurrentTime >= 2070 && heads().All(head => head.IsHit));
            AddStep("return to exact played beat", () =>
            {
                EditorClock.Stop();
                EditorClock.Seek(2000);
            });
            AddUntilStep("full purple stack visible at its exact beat", () => visibleOverlapCount() == 1);
            AddAssert("cursors remain hidden while editing", cursorsHidden);
        }

        private SticksPlayfield playfield() => this.ChildrenOfType<SticksPlayfield>().Single();

        private DrawableSticksFlick[] heads() => playfield().AllHitObjects.OfType<DrawableSticksFlick>().ToArray();

        private bool cursorsHidden() => playfield().LeftStickCursor.Alpha == 0 && playfield().RightStickCursor.Alpha == 0;

        private int visibleOverlapCount()
        {
            var layer = this.ChildrenOfType<SticksCenterOutNoteOverlapLayer>().Single();
            Array visuals = (Array)typeof(SticksCenterOutNoteOverlapLayer)
                .GetField("overlaps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(layer)!;
            return visuals.Cast<Drawable>().Count(visual => visual.IsPresent);
        }
    }
}
