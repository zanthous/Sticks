#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit.Compose.Components.Timeline;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    public partial class SticksTimelineTurnMarkersTest : EditorTestScene
    {
        private readonly float[] arcs = { 90, 45, -60, 0, 90 };

        protected override Ruleset CreateEditorRuleset() => new SticksRuleset();

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();
            AddStep("clear and pause editor", () =>
            {
                EditorClock.Stop();
                EditorBeatmap.Clear();
                EditorBeatmap.ControlPointInfo.Clear();
                EditorBeatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                EditorClock.Seek(2000);
            });
        }

        [Test]
        public void TestTurnsFollowAuthoredTimingThroughEditsUndoAndSeeking()
        {
            AddStep("add uneven timed slider with a dwell", () =>
            {
                var slider = new SticksSlider
                {
                    StartTime = 2000,
                    Duration = 1000,
                    Angle = 15,
                    Side = StickSide.Right,
                };
                slider.SetTimedSegments(arcs, new double[] { 100, 200, 300, 100, 300 });
                slider.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(slider);
            });
            AddUntilStep("only opposite turns receive markers", () => markersAt(0.3f, 0.7f));
            AddStep("markers align to time between endcap centres", assertScreenPositions);

            AddStep("move segment boundaries without changing duration", () =>
            {
                EditorBeatmap.BeginChange();
                slider().SetTimedSegments(arcs, new double[] { 200, 200, 100, 300, 200 });
                EditorBeatmap.Update(slider());
                EditorBeatmap.EndChange();
            });
            AddUntilStep("markers follow new boundaries", () => markersAt(0.4f, 0.8f));
            AddAssert("overall duration remains unchanged", () => slider().Duration == 1000);

            AddStep("stretch overall duration", () =>
            {
                EditorBeatmap.BeginChange();
                slider().Duration = 2000;
                EditorBeatmap.Update(slider());
                EditorBeatmap.EndChange();
            });
            AddUntilStep("bar and markers stretch together", () => blueprint()?.Width == 2000 && markersAt(0.4f, 0.8f));
            AddStep("stretched markers retain actual time positions", assertScreenPositions);
            AddStep("undo duration", () => Editor.Undo());
            AddUntilStep("old duration restored with edited boundaries", () => slider().Duration == 1000 && markersAt(0.4f, 0.8f));
            AddStep("undo boundary edit", () => Editor.Undo());
            AddUntilStep("original uneven boundaries restored", () => markersAt(0.3f, 0.7f));
            AddStep("redo boundary edit", () => Editor.Redo());
            AddUntilStep("edited boundaries restored", () => markersAt(0.4f, 0.8f));
            AddStep("redo duration", () => Editor.Redo());
            AddUntilStep("stretched slider restored", () => slider().Duration == 2000 && markersAt(0.4f, 0.8f));

            AddStep("seek beyond slider lifetime", () => EditorClock.Seek(20000));
            AddWaitStep("let editor retire offscreen visuals", 5);
            AddStep("seek back to slider", () => EditorClock.Seek(2000));
            AddUntilStep("turn markers return without duplicates", () => markersAt(0.4f, 0.8f)
                && blueprint()!.ChildrenOfType<SticksTimelineTurnMarkers>().Count() == 1);
            AddStep("seek backward before slider start", () => EditorClock.Seek(1700));
            AddUntilStep("backward seek retains marker positions", () => markersAt(0.4f, 0.8f));

            AddStep("replace path with same-direction motion and a dwell", () =>
            {
                slider().SetTimedSegments(new float[] { 90, 45, 0, 90 }, new double[] { 100, 200, 100, 600 });
                EditorBeatmap.Update(slider());
            });
            AddUntilStep("same-direction joins and dwell have no turn marks", () => markersAt());
            AddStep("make slider stationary", () =>
            {
                slider().SetTimedSegments(new float[] { 0, 0 }, new double[] { 300, 700 });
                EditorBeatmap.Update(slider());
            });
            AddWaitStep("update stationary path", 3);
            AddAssert("stationary slider has no turn marks", () => markersAt());
        }

        private SticksSlider slider() => EditorBeatmap.HitObjects.OfType<SticksSlider>().Single();

        private TimelineHitObjectBlueprint? blueprint() => this.ChildrenOfType<TimelineHitObjectBlueprint>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Item, slider()));

        private Box[] visibleMarkers() => blueprint()!.ChildrenOfType<Box>()
            .Where(box => box.Name == "Slider turn" && box.IsPresent).OrderBy(box => box.X).ToArray();

        private bool markersAt(params float[] expected)
        {
            if (blueprint()?.ChildrenOfType<SticksTimelineTurnMarkers>().Any() != true)
                return false;

            Box[] markers = visibleMarkers();
            return markers.Length == expected.Length
                   && markers.Select((marker, index) => Math.Abs(marker.X - expected[index]) < 0.0001f).All(matches => matches);
        }

        private void assertScreenPositions()
        {
            TimelineHitObjectBlueprint nativeBlueprint = blueprint()!;
            foreach (Box marker in visibleMarkers())
            {
                Vector2 expected = nativeBlueprint.ToScreenSpace(new Vector2(nativeBlueprint.DrawWidth * marker.X, nativeBlueprint.DrawHeight / 2));
                Assert.That(marker.ScreenSpaceDrawQuad.Centre.X, Is.EqualTo(expected.X).Within(0.5f));
                Assert.That(marker.ScreenSpaceDrawQuad.Centre.Y, Is.EqualTo(expected.Y).Within(0.5f));
            }
        }
    }
}
