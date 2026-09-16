#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Input;
using DragArea = osu.Game.Screens.Edit.Compose.Components.Timeline.TimelineHitObjectBlueprint.DragArea;

namespace osu.Game.Rulesets.Sticks.Tests
{
    public partial class SticksTimelineSliderDragTest : EditorTestScene
    {
        private const double initial_duration = 1000;

        private readonly float[] initialSegments = { 90, -135, 180 };

        private double resizedDuration;
        private Vector2 stressDragOrigin;
        private int stressDragStep;
        private long managedMemoryBeforeStress;
        private long privateMemoryBeforeStress;
        private long managedMemoryAfterStress;
        private long privateMemoryAfterStress;
        private SticksSlider continuationSlider = null!;

        protected override Ruleset CreateEditorRuleset() => new SticksRuleset();

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();
            AddStep("clear objects", () => EditorBeatmap.Clear());
        }

        [Test]
        public void TestPlayerApproachRateSurvivesEditorUpdatesAndUndo()
        {
            SticksRulesetConfigManager config = null!;
            void assertApproach(double duration)
            {
                foreach (SticksHitObject note in EditorBeatmap.HitObjects)
                {
                    Assert.That(note.ApproachDuration, Is.EqualTo(duration).Within(0.001), note.GetType().Name);
                    foreach (SticksHitObject nested in note.NestedHitObjects.OfType<SticksHitObject>())
                        Assert.That(nested.ApproachDuration, Is.EqualTo(duration).Within(0.001), nested.GetType().Name);
                }
                foreach (var drawable in playfield().AllHitObjects)
                    Assert.That(drawable.LifetimeStart, Is.EqualTo(drawable.HitObject.StartTime - duration).Within(0.001));
                assertVisibleSelectionPositions();
            }

            AddStep("set player AR and add notes on a slower map", () =>
            {
                config = (SticksRulesetConfigManager)RulesetConfigs.GetConfigFor(new SticksRuleset())!;
                config.SetValue(SticksRulesetSetting.ApproachRate, 9.4f);
                EditorBeatmap.Difficulty.ApproachRate = 2;
                EditorClock.Stop();
                EditorClock.Seek(1800);
                foreach (SticksHitObject note in new SticksHitObject[]
                         {
                             new SticksFlick { StartTime = 2000, Angle = 0 },
                             new SticksSlider { StartTime = 2000, Angle = 90, ArcAngle = 90, Duration = 1000 },
                             new SticksClick { StartTime = 2000, Side = StickSide.Right },
                         })
                {
                    note.EnsureLegacyEditorMarker();
                    EditorBeatmap.Add(note);
                }
            });
            AddUntilStep("notes and selections loaded", () => playfield().AllHitObjects.Count() == 3 && selectionBlueprints().Length == 3);
            AddStep("all visuals use 540ms approach", () => assertApproach(540));
            AddStep("edit slider duration and regenerate its checkpoints", () =>
            {
                SticksSlider slider = currentSlider();
                slider.Duration = 1500;
                EditorBeatmap.Update(slider);
            });
            AddWaitStep("settle edited visuals", 3);
            AddStep("editing keeps player approach including checkpoints", () => assertApproach(540));
            AddStep("change player AR while editor stays open", () => config.SetValue(SticksRulesetSetting.ApproachRate, 6f));
            AddWaitStep("refresh approach", 3);
            AddStep("existing notes adopt 1050ms approach", () => assertApproach(1050));
            AddAssert("display preference does not overwrite map AR", () => EditorBeatmap.Difficulty.ApproachRate == 2);
            AddStep("undo duration edit", () => Editor.Undo());
            AddWaitStep("restore note", 3);
            AddStep("undo uses current player approach", () => assertApproach(1050));
            AddStep("redo duration edit", () => Editor.Redo());
            AddWaitStep("restore edit", 3);
            AddStep("redo uses current player approach", () => assertApproach(1050));
        }

        [Test]
        public void TestPlacementPreviewUsesPlayerApproachRate()
        {
            SticksRulesetConfigManager config = null!;
            SticksSliderPlacementBlueprint placement() => this.ChildrenOfType<SticksSliderPlacementBlueprint>().Single();
            float endpointRadius() => (placement().ChildrenOfType<SticksBlueprintPiece>().Single().EndpointPosition
                                       - new Vector2(SticksPlayfield.SIZE / 2)).Length;

            AddStep("choose slider with AR 9.4", () =>
            {
                config = (SticksRulesetConfigManager)RulesetConfigs.GetConfigFor(new SticksRuleset())!;
                config.SetValue(SticksRulesetSetting.ApproachRate, 9.4f);
                EditorBeatmap.ControlPointInfo.Clear();
                EditorBeatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 250 });
                EditorClock.Seek(2000);
                InputManager.PressKey(Key.Number3);
                InputManager.ReleaseKey(Key.Number3);
            });
            AddUntilStep("placement loaded", () => this.ChildrenOfType<SticksSliderPlacementBlueprint>().Any());
            AddStep("move to start", () => InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(0, 246))));
            AddWaitStep("update preview", 2);
            AddStep("start placing", () => InputManager.Click(MouseButton.Left));
            AddStep("trace short arc", () => InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(45, 246))));
            AddStep("scroll to preview end", () => EditorClock.Seek(2250));
            AddWaitStep("update slider preview", 3);
            AddStep("preview uses 540ms approach", () =>
            {
                Assert.That(placement().HitObject.Duration, Is.InRange(1, 400));
                Assert.That(endpointRadius(), Is.EqualTo(SticksPlayfield.GUIDE_RADIUS * (1 - placement().HitObject.Duration / 540)).Within(0.01));
            });
            AddStep("change approach while placing", () => config.SetValue(SticksRulesetSetting.ApproachRate, 5f));
            AddWaitStep("update preview approach", 3);
            AddStep("preview updates immediately", () =>
                Assert.That(endpointRadius(), Is.EqualTo(SticksPlayfield.GUIDE_RADIUS * (1 - placement().HitObject.Duration / 1200)).Within(0.01)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestTimelineEndHandleResizesDurationWithoutChangingPath(bool stationary)
        {
            float[] segments = stationary ? new[] { 0f } : initialSegments;
            bool samePath() => currentSlider().SegmentArcAngles.SequenceEqual(segments);

            AddStep("add segmented slider", () =>
            {
                var slider = new SticksSlider
                {
                    StartTime = 2000,
                    Duration = initial_duration,
                    Side = StickSide.Left,
                    Angle = 15,
                };

                slider.SetCustomSegments(segments);
                slider.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(slider);
            });

            AddUntilStep("timeline handle available", () => this.ChildrenOfType<DragArea>().Any(area => area.HandlePositionalInput));

            AddStep("drag timeline end forward", () =>
            {
                DragArea dragArea = this.ChildrenOfType<DragArea>().Single(area => area.HandlePositionalInput);
                Vector2 start = dragArea.ScreenSpaceDrawQuad.Centre;

                InputManager.MoveMouseTo(start);
                InputManager.PressButton(MouseButton.Left);
                InputManager.MoveMouseTo(start + new Vector2(160, 0));
            });

            AddStep("release timeline end", () => InputManager.ReleaseButton(MouseButton.Left));
            AddUntilStep("duration increased", () => currentSlider().Duration > initial_duration);

            AddStep("remember resized duration", () => resizedDuration = currentSlider().Duration);
            AddAssert("path unchanged after resize", samePath);
            AddAssert("carrier marker follows duration", markerIsCurrent);

            AddStep("undo resize", () => Editor.Undo());
            AddUntilStep("duration restored", () => currentSlider().Duration == initial_duration);
            AddAssert("path unchanged after undo", samePath);

            AddStep("redo resize", () => Editor.Redo());
            AddUntilStep("resized duration restored", () => Math.Abs(currentSlider().Duration - resizedDuration) <= 0.001);
            AddAssert("path unchanged after redo", samePath);
            AddAssert("carrier marker current after redo", markerIsCurrent);
        }

        [TestCase(2500)]
        [TestCase(4000)]
        public void TestAddSliderPointUsesTraceAndScrolledTime(double selectionTime)
        {
            Drawable addPointButton() => selectionBlueprints().Single().ChildrenOfType<Drawable>().Single(drawable => drawable.Name == "Add slider point");

            AddStep("select slider away from its tail time", () =>
            {
                EditorClock.Stop();
                continuationSlider = new SticksSlider
                {
                    StartTime = 2000,
                    Duration = 1003.5,
                    Side = StickSide.Left,
                    Angle = 0,
                    ArcAngle = 90,
                };
                continuationSlider.SetNodeSizeMultipliers(new[] { 1f, 1.5f });
                continuationSlider.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(continuationSlider);
                EditorClock.Seek(selectionTime);
                EditorBeatmap.SelectedHitObjects.Add(continuationSlider);
            });
            AddUntilStep("add point button visible", () => selectionBlueprints().Length == 1 && addPointButton().IsPresent);
            AddStep("click plus to continue", () =>
            {
                InputManager.MoveMouseTo(addPointButton().ScreenSpaceDrawQuad.Centre);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("point placement active", () => selectionBlueprints().Single().IsPlacingContinuation);
            AddAssert("jumps to exact off-grid tail time", () => Math.Abs(EditorClock.CurrentTimeAccurate - 3003.5) < 0.001);
            AddAssert("button click preserves the original slider", () => EditorBeatmap.HitObjects.Contains(continuationSlider)
                && continuationSlider.SegmentCount == 1 && continuationSlider.Duration == 1003.5);
            AddAssert("point handles hide during continuation", () => !selectionBlueprints().Single().ChildrenOfType<Drawable>()
                .Any(drawable => (drawable.Name == "Slider tail" || drawable.Name.StartsWith("Slider turn ", StringComparison.Ordinal)) && drawable.IsPresent));
            AddAssert("add button hides during continuation", () => !addPointButton().IsPresent);
            AddStep("trace reverse arc", () => InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(15, 246))));
            AddWaitStep("update trace", 3);
            AddStep("click on same beat", () => InputManager.Click(MouseButton.Left));
            AddAssert("mouse motion cannot add duration", () => continuationSlider.SegmentCount == 1
                && selectionBlueprints().Single().IsPlacingContinuation);
            AddStep("scroll before current segment", () => EditorClock.Seek(2900));
            AddStep("reject backwards point", () => InputManager.Click(MouseButton.Right));
            AddAssert("old slider still intact", () => continuationSlider.Duration == 1003.5 && continuationSlider.SegmentCount == 1);
            AddStep("scroll to new end", () => EditorClock.Seek(3503.5));
            AddWaitStep("update end preview", 3);
            AddAssert("modern directional preview is visible", () => selectionBlueprints().Single()
                .ChildrenOfType<SticksSliderHeadMarker>().Any(marker => marker.IsPresent));
            AddStep("finish with left click", () => InputManager.Click(MouseButton.Left));
            AddUntilStep("continuation committed", () => continuationSlider.SegmentCount == 2);
            AddAssert("time and shape are independent", () => Math.Abs(continuationSlider.Duration - 1503.5) < 0.001
                && Math.Abs(continuationSlider.SegmentDurationAt(0) - 1003.5) < 0.001
                && Math.Abs(continuationSlider.SegmentArcAngleAt(1) + 75) < 0.01);
            AddAssert("new span inherits the tail size", () => continuationSlider.NodeSizeMultiplierAt(0) == 1
                && continuationSlider.NodeSizeMultiplierAt(1) == 1.5f && continuationSlider.NodeSizeMultiplierAt(2) == 1.5f);
            AddAssert("point placement finished", () => !selectionBlueprints().Single().IsPlacingContinuation);
            AddStep("undo new point", () => Editor.Undo());
            AddUntilStep("one undo restores original slider", () => currentSlider().SegmentCount == 1 && currentSlider().Duration == 1003.5);
            AddStep("redo new point", () => Editor.Redo());
            AddUntilStep("new point restored", () => currentSlider().SegmentCount == 2 && currentSlider().Duration == 1503.5);
            AddStep("reselect extended slider", selectAllNotes);
            AddUntilStep("add point button returns", () => addPointButton().IsPresent);
            AddStep("start another point", () =>
            {
                InputManager.MoveMouseTo(addPointButton().ScreenSpaceDrawQuad.Centre);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("another point placement active", () => selectionBlueprints().Single().IsPlacingContinuation);
            AddStep("cancel pending point", () => InputManager.Key(Key.Escape));
            AddUntilStep("pending point cancelled", () => !selectionBlueprints().Single().IsPlacingContinuation);
            AddAssert("cancel keeps both committed points", () => currentSlider().SegmentCount == 2 && currentSlider().Duration == 1503.5);
            AddStep("reselect after cancellation", selectAllNotes);
            AddUntilStep("add button available again", () => addPointButton().IsPresent);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestRightClickPlacesReversalsAndEscapeKeepsCommittedSlider(bool paired)
        {
            int noteCount = paired ? 2 : 1;
            SticksSlider[] sliders() => EditorBeatmap.HitObjects.OfType<SticksSlider>().ToArray();
            SticksSelectionBlueprint pointPlacement() => selectionBlueprints().Single(blueprint => blueprint.IsPlacingContinuation);

            AddStep("choose slider tool", () =>
            {
                EditorBeatmap.ControlPointInfo.Clear();
                EditorBeatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                // The placed head snaps to 2000, but the current clock can be just off-grid.
                EditorClock.Seek(2003.5);
                InputManager.PressKey(Key.Number3);
                InputManager.ReleaseKey(Key.Number3);
            });
            AddUntilStep("slider tool ready", () => this.ChildrenOfType<SticksSliderPlacementBlueprint>().Any());
            AddStep("move to first point", () => InputManager.MoveMouseTo(
                playfield().ToScreenSpace(SticksPlayfield.PointAt(0, paired ? 340 : 246))));
            AddWaitStep("update start preview", 3);
            AddStep("place slider start", () => InputManager.Click(MouseButton.Left));
            AddStep("trace first arc without advancing beat", () => InputManager.MoveMouseTo(
                playfield().ToScreenSpace(SticksPlayfield.PointAt(90, paired ? 340 : 246))));
            AddWaitStep("update arc preview", 3);
            AddStep("right click on starting beat", () => InputManager.Click(MouseButton.Right));
            AddAssert("same beat leaves draft active", () => EditorBeatmap.HitObjects.Count == 0
                && this.ChildrenOfType<SticksSliderPlacementBlueprint>().Single().PlacementActive
                   == osu.Game.Rulesets.Edit.PlacementBlueprint.PlacementState.Active);
            AddStep("advance to first reversal beat", () => EditorClock.Seek(3000));
            AddWaitStep("update point time", 3);
            AddStep("right click first reversal point", () => InputManager.Click(MouseButton.Right));
            AddUntilStep("prefix placed and next point preview active", () => sliders().Length == noteCount
                && selectionBlueprints().Count(blueprint => blueprint.IsPlacingContinuation) == 1);
            AddAssert("first span ends on clicked beat", () =>
            {
                foreach (SticksSlider slider in sliders())
                {
                    Assert.That(slider.StartTime, Is.EqualTo(2000), "first point time");
                    Assert.That(slider.Duration, Is.EqualTo(1000), "first span duration");
                    Assert.That(slider.SegmentCount, Is.EqualTo(1), "first span count");
                    Assert.That(slider.ArcAngle, Is.EqualTo(90).Within(0.01), "first span arc");
                }
                return true;
            });
            AddStep("right click same reversal beat again", () => InputManager.Click(MouseButton.Right));
            AddAssert("duplicate point does nothing", () => sliders().All(slider => slider.SegmentCount == 1) && pointPlacement().IsPlacingContinuation);
            AddStep("trace next reversed span", () => InputManager.MoveMouseTo(
                playfield().ToScreenSpace(SticksPlayfield.PointAt(45, paired ? 340 : 246))));
            AddStep("advance to next point", () => EditorClock.Seek(3500));
            AddWaitStep("update next point", 3);
            AddStep("right click commits reversed span", () => InputManager.Click(MouseButton.Right));
            AddUntilStep("opposite span placed", () => sliders().All(slider => slider.SegmentCount == 2));
            AddAssert("span timing and reversal are exact", () => sliders().All(slider => slider.Duration == 1500
                && Math.Abs(slider.SegmentArcAngleAt(1) + 45) < 0.01));
            AddStep("preview another point", () => EditorClock.Seek(3750));
            AddWaitStep("update pending third point", 3);
            AddStep("escape cancels pending point", () =>
            {
                InputManager.PressKey(Key.Escape);
                InputManager.ReleaseKey(Key.Escape);
            });
            AddUntilStep("point preview cancelled", () => selectionBlueprints().All(blueprint => !blueprint.IsPlacingContinuation));
            AddAssert("committed slider remains", () =>
            {
                Assert.That(sliders(), Has.Length.EqualTo(noteCount), "placed objects");
                foreach (SticksSlider slider in sliders())
                {
                    Assert.That(slider.SegmentCount, Is.EqualTo(2), "committed segments");
                    Assert.That(slider.Duration, Is.EqualTo(1500), "committed duration");
                }
                return true;
            });
            AddStep("undo last placed point", () => Editor.Undo());
            AddUntilStep("only the last span undone", () => sliders().Length == noteCount
                && sliders().All(slider => slider.SegmentCount == 1 && slider.Duration == 1000));
            AddStep("redo last point", () => Editor.Redo());
            AddUntilStep("reversed span restored", () => sliders().Length == noteCount
                && sliders().All(slider => slider.SegmentCount == 2 && slider.Duration == 1500));
        }

        [Test]
        public void TestSliderPlacementReadoutTracksUnsnappedAndSnappedTravel()
        {
            void move(float angle) => InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(angle, 246)));
            bool visible(Drawable drawable)
            {
                for (Drawable? current = drawable; current != null; current = current.Parent)
                {
                    if (!current.IsPresent)
                        return false;
                }
                return true;
            }
            SpriteText[] readouts() => this.ChildrenOfType<SpriteText>()
                .Where(text => text.Name == "Slider placement details" && visible(text)).ToArray();
            bool shows(string angle) => readouts() is { Length: 1 } labels
                && labels[0].Text.ToString().Replace('−', '-').Contains(angle, StringComparison.Ordinal);

            AddStep("choose slider on fixed beat", () =>
            {
                EditorClock.Stop();
                EditorBeatmap.ControlPointInfo.Clear();
                EditorBeatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                EditorClock.Seek(2000);
                InputManager.PressKey(Key.Number3);
                InputManager.ReleaseKey(Key.Number3);
            });
            AddUntilStep("placement ready", () => this.ChildrenOfType<SticksSliderPlacementBlueprint>().Any());
            AddStep("move to start direction", () => move(0));
            AddWaitStep("update starting position", 2);
            AddStep("start slider", () => InputManager.Click(MouseButton.Left));
            AddUntilStep("initial travel reads zero", () => shows("0°"));
            AddStep("trace without Shift", () => move(37));
            AddUntilStep("unsnapped travel is always visible", () => shows("37°"));
            AddStep("hold Shift without moving", () => InputManager.PressKey(Key.ShiftLeft));
            AddUntilStep("stationary pointer updates snapped readout", () => shows("30°"));
            AddStep("release Shift without moving", () => InputManager.ReleaseKey(Key.ShiftLeft));
            AddUntilStep("unsnapped value returns", () => shows("37°"));
            AddStep("trace next snapped angle", () =>
            {
                InputManager.PressKey(Key.ShiftLeft);
                move(44);
            });
            AddUntilStep("readout matches snapped path", () => shows("45°"));
            AddStep("align pointer with endpoint and release Shift", () =>
            {
                move(45);
                InputManager.ReleaseKey(Key.ShiftLeft);
            });
            AddStep("advance to reversal time", () => EditorClock.Seek(3000));
            AddWaitStep("update timed span", 2);
            AddStep("place first span and continue", () => InputManager.Click(MouseButton.Right));
            AddUntilStep("new point resets displayed travel", () => EditorBeatmap.HitObjects.Count == 1
                && selectionBlueprints().Any(blueprint => blueprint.IsPlacingContinuation) && shows("0°"));
            AddStep("trace reverse travel", () => move(0));
            AddUntilStep("reverse travel includes negative sign", () => shows("-45°"));
            foreach (float angle in new[] { 270f, 180f, 90f, 0f })
                AddStep($"continue reverse rotation through {angle}", () => move(angle));
            AddUntilStep("travel remains unwrapped after full turn", () => shows("-405°"));
            AddStep("cancel pending point", () =>
            {
                InputManager.PressKey(Key.Escape);
                InputManager.ReleaseKey(Key.Escape);
            });
            AddUntilStep("cancelled point leaves no readout", () => readouts().Length == 0);
            AddAssert("completed span is preserved", () => EditorBeatmap.HitObjects.Single() is SticksSlider slider
                && slider.SegmentCount == 1 && slider.Duration == 1000 && Math.Abs(slider.ArcAngle - 45) < 0.01);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestPlacedChordIsNotCoveredByPlacementPreview(bool click)
        {
            SticksBlueprintPiece preview() => this.ChildrenOfType<osu.Game.Rulesets.Edit.HitObjectPlacementBlueprint>()
                .Single().ChildrenOfType<SticksBlueprintPiece>().Single();
            void move(float radius) => InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(60, radius)));

            AddStep("choose note tool", () =>
            {
                EditorBeatmap.ControlPointInfo.Clear();
                EditorBeatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                EditorClock.Seek(2000);
                InputManager.PressKey(click ? Key.Number4 : Key.Number2);
                InputManager.ReleaseKey(click ? Key.Number4 : Key.Number2);
            });
            AddUntilStep("placement ready", () => this.ChildrenOfType<osu.Game.Rulesets.Edit.HitObjectPlacementBlueprint>().Any());
            AddStep("preview left note", () => move(246));
            AddUntilStep("left preview visible", () => preview().Alpha == 1);
            AddStep("place left note", () => InputManager.Click(MouseButton.Left));
            AddUntilStep("left placed", () => EditorBeatmap.HitObjects.Count == 1);
            AddUntilStep("identical preview hidden", () => preview().Alpha == 0);
            AddStep("preview right note", () => move(214));
            AddUntilStep("opposite hand still previews", () => preview().Alpha == 1);
            AddStep("place matching right note", () => InputManager.Click(MouseButton.Left));
            AddUntilStep("pair placed", () => EditorBeatmap.HitObjects.Count == 2);
            AddUntilStep("preview no longer covers chord", () => preview().Alpha == 0);
            AddStep("seek to next beat", () => EditorClock.Seek(2500));
            AddUntilStep("next beat previews normally", () => preview().Alpha == 1);
        }

        [Test]
        public void TestRepeatedTimelineDragHasBoundedRetainedMemory()
        {
            AddStep("add segmented slider", addSegmentedSlider);
            AddUntilStep("timeline handle available", () => this.ChildrenOfType<DragArea>().Any(area => area.HandlePositionalInput));
            AddWaitStep("allow editor resources to settle", 180);

            AddStep("collect memory baseline", () =>
            {
                collectGarbage();
                managedMemoryBeforeStress = GC.GetTotalMemory(false);
                privateMemoryBeforeStress = Process.GetCurrentProcess().PrivateMemorySize64;
            });

            AddStep("start timeline drag", () =>
            {
                DragArea dragArea = this.ChildrenOfType<DragArea>().Single(area => area.HandlePositionalInput);
                stressDragOrigin = dragArea.ScreenSpaceDrawQuad.Centre;
                stressDragStep = 0;
                InputManager.MoveMouseTo(stressDragOrigin);
                InputManager.PressButton(MouseButton.Left);
            });

            AddRepeatStep("oscillate timeline end", () =>
            {
                float offset = stressDragStep++ % 2 == 0 ? 120 : 180;
                InputManager.MoveMouseTo(stressDragOrigin + new Vector2(offset, 0));
            }, 240);

            AddStep("release timeline end", () => InputManager.ReleaseButton(MouseButton.Left));
            AddWaitStep("allow expired drawables to settle", 10);
            AddStep("collect retained memory", () =>
            {
                collectGarbage();
                managedMemoryAfterStress = GC.GetTotalMemory(false);
                privateMemoryAfterStress = Process.GetCurrentProcess().PrivateMemorySize64;
            });

            AddStep("report retained memory", () => TestContext.Progress.WriteLine(
                $"Repeated timeline drag retained managed={(managedMemoryAfterStress - managedMemoryBeforeStress) / 1024d / 1024:0.0} MiB, " +
                $"private={(privateMemoryAfterStress - privateMemoryBeforeStress) / 1024d / 1024:0.0} MiB"));

            AddAssert("path remains bounded", pathIsUnchanged);
            AddAssert("managed growth below 64 MiB", () => managedMemoryAfterStress - managedMemoryBeforeStress < 64L * 1024 * 1024);
            AddAssert("private growth below 512 MiB", () => privateMemoryAfterStress - privateMemoryBeforeStress < 512L * 1024 * 1024);
        }

        [Test]
        public void TestRepeatedHoldTimelineDragHasBoundedRetainedMemory()
        {
            AddStep("add hold", () =>
            {
                var hold = new SticksHold
                {
                    StartTime = 2000,
                    Duration = initial_duration,
                    Side = StickSide.Left,
                    Angle = 15,
                };

                hold.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(hold);
            });
            AddUntilStep("timeline handle available", () => this.ChildrenOfType<DragArea>().Any(area => area.HandlePositionalInput));
            AddWaitStep("allow editor resources to settle", 180);

            AddStep("collect memory baseline", () =>
            {
                collectGarbage();
                managedMemoryBeforeStress = GC.GetTotalMemory(false);
                privateMemoryBeforeStress = Process.GetCurrentProcess().PrivateMemorySize64;
            });
            AddStep("start timeline drag", () =>
            {
                DragArea dragArea = this.ChildrenOfType<DragArea>().Single(area => area.HandlePositionalInput);
                stressDragOrigin = dragArea.ScreenSpaceDrawQuad.Centre;
                stressDragStep = 0;
                InputManager.MoveMouseTo(stressDragOrigin);
                InputManager.PressButton(MouseButton.Left);
            });
            AddRepeatStep("oscillate timeline end", () =>
            {
                float offset = stressDragStep++ % 2 == 0 ? 120 : 180;
                InputManager.MoveMouseTo(stressDragOrigin + new Vector2(offset, 0));
            }, 240);
            AddStep("release timeline end", () => InputManager.ReleaseButton(MouseButton.Left));
            AddWaitStep("allow expired drawables to settle", 10);

            AddStep("collect retained memory", () =>
            {
                collectGarbage();
                managedMemoryAfterStress = GC.GetTotalMemory(false);
                privateMemoryAfterStress = Process.GetCurrentProcess().PrivateMemorySize64;
                TestContext.Progress.WriteLine(
                    $"Repeated hold drag retained managed={(managedMemoryAfterStress - managedMemoryBeforeStress) / 1024d / 1024:0.0} MiB, " +
                    $"private={(privateMemoryAfterStress - privateMemoryBeforeStress) / 1024d / 1024:0.0} MiB");
            });

            AddAssert("managed growth below 64 MiB", () => managedMemoryAfterStress - managedMemoryBeforeStress < 64L * 1024 * 1024);
            AddAssert("private growth below 512 MiB", () => privateMemoryAfterStress - privateMemoryBeforeStress < 512L * 1024 * 1024);
        }

        [Test]
        public void TestModernPreviewRestoresGeometryAcrossSeeksAndDeleteUndoRedo()
        {
            SticksRadialTimelinePath[] deletedRibbons = Array.Empty<SticksRadialTimelinePath>();
            AddStep("add all four note types", () =>
            {
                var slider = new SticksSlider { StartTime = 2000, Duration = 2000, Side = StickSide.Left, Angle = 180 };
                slider.SetTimedSegments(new[] { 90f, -90f }, new[] { 750d, 1250d });
                SticksHitObject[] notes =
                {
                    new SticksFlick { StartTime = 2000, Side = StickSide.Left, Angle = 0 },
                    new SticksClick { StartTime = 2000, Side = StickSide.Right },
                    new SticksHold { StartTime = 2000, Duration = 2000, Side = StickSide.Right, Angle = 90 },
                    slider,
                };
                foreach (SticksHitObject note in notes)
                    note.EnsureLegacyEditorMarker();
                EditorBeatmap.AddRange(notes);
                EditorClock.Seek(1900);
            });
            AddUntilStep("mixed selection blueprints loaded", () => selectionBlueprints().Length == 4);
            AddStep("select all mixed notes", selectAllNotes);

            foreach (double time in new[] { 0d, 1900, 2000, 2500, 3500, 4500, 2500, 1900, 0, 3500 })
            {
                AddStep($"seek to {time}ms", () => EditorClock.Seek(time));
                AddWaitStep("settle seek", 3);
                AddAssert("selection geometry stays finite and follows visible objects", assertVisibleSelectionPositions);
                AddAssert("duration ribbons follow the current time", () =>
                {
                    int expected = EditorClock.CurrentTimeAccurate is >= 1900 and < 4000 ? 2 : 0;
                    if (visibleRibbonCount() != expected)
                        reportRibbonOwners();
                    Assert.That(visibleRibbonCount(), Is.EqualTo(expected), $"Ribbon count at {EditorClock.CurrentTimeAccurate}ms.");
                    return true;
                });
            }

            AddStep("delete selected notes", () =>
            {
                deletedRibbons = this.ChildrenOfType<SticksRadialTimelinePath>().ToArray();
                selectAllNotes();
                InputManager.PressKey(Key.Delete);
            });
            AddStep("release delete", () => InputManager.ReleaseKey(Key.Delete));
            AddUntilStep("notes removed", () => EditorBeatmap.HitObjects.Count == 0);
            AddUntilStep("deleted ribbons detached", () => visibleRibbonCount() == 0);
            // Editor removals retain their drawable for reuse; its separately owned body
            // must leave the playfield immediately without being destroyed before undo.
            AddUntilStep("deleted ribbon resources detached", () => deletedRibbons.Length == 2
                && deletedRibbons.All(path => path.Parent == null));
            AddStep("undo deletion", () => Editor.Undo());
            AddUntilStep("four note types restored", () => EditorBeatmap.HitObjects.Count == 4);
            AddStep("seek inside restored durations", () => EditorClock.Seek(2500));
            AddUntilStep("restored ribbons loaded", () => visibleRibbonCount() == 2);
            AddAssert("restored slider keeps its timed path", () =>
            {
                SticksSlider restored = EditorBeatmap.HitObjects.OfType<SticksSlider>().Single(slider => !slider.IsStationary);
                return restored.SegmentArcAngles.SequenceEqual(new[] { 90f, -90f })
                       && Math.Abs(restored.SegmentDurationAt(0) - 750) < 0.001
                       && Math.Abs(restored.SegmentDurationAt(1) - 1250) < 0.001;
            });
            AddAssert("restored legacy hold is a stationary slider", () =>
            {
                SticksSlider restored = EditorBeatmap.HitObjects.OfType<SticksSlider>().Single(slider => slider.IsStationary);
                return restored.Side == StickSide.Right && restored.Angle == 90 && restored.Duration == 2000;
            });
            AddStep("redo deletion", () => Editor.Redo());
            AddUntilStep("redo removes notes and their ribbons", () => EditorBeatmap.HitObjects.Count == 0 && visibleRibbonCount() == 0);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestAngularDragPreservesStickAndUndo(bool activeSlider)
        {
            AddStep("add right-stick note", () =>
            {
                SticksHitObject note = activeSlider
                    ? new SticksSlider { Duration = 2000, ArcAngle = 90 }
                    : new SticksFlick();
                note.StartTime = 2000;
                note.Side = StickSide.Right;
                note.Angle = 0;
                note.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(note);
                EditorClock.Seek(1900);
            });
            AddUntilStep("note blueprint loaded", () => selectionBlueprints().Length == 1);
            AddStep("seek to approaching head or active slider contact", () =>
                EditorClock.Seek(activeSlider ? 2500 : 2000 - EditorBeatmap.HitObjects.OfType<SticksHitObject>().Single().ApproachDuration / 2));
            AddWaitStep("update visible hit target", 3);
            AddStep("rotate visible target through 45 degrees", () =>
            {
                float angle = activeSlider ? currentSlider().AngleAt(EditorClock.CurrentTimeAccurate) + 45 : 45;
                float radius = activeSlider ? SticksPlayfield.GUIDE_RADIUS : SticksPlayfield.GUIDE_RADIUS / 2;
                Vector2 target = playfield().ToScreenSpace(SticksPlayfield.PointAt(angle, radius));
                InputManager.MoveMouseTo(selectionBlueprints().Single().ScreenSpaceSelectionPoint);
                InputManager.PressButton(MouseButton.Left);
                InputManager.MoveMouseTo(target);
            });
            AddStep("release head", () => InputManager.ReleaseButton(MouseButton.Left));
            AddUntilStep("angle changed without switching stick", () =>
            {
                SticksHitObject note = EditorBeatmap.HitObjects.OfType<SticksHitObject>().Single();
                return note.Side == StickSide.Right && Math.Abs(note.Angle - 45) < 0.1;
            });
            AddAssert("drag preserves the slider contour", () => !activeSlider || Math.Abs(currentSlider().ArcAngle - 90) < 0.001);
            AddStep("undo angular drag", () => Editor.Undo());
            AddUntilStep("original angle restored", () => EditorBeatmap.HitObjects.OfType<SticksHitObject>().Single().Angle == 0);
            AddStep("redo angular drag", () => Editor.Redo());
            AddUntilStep("edited angle restored on right stick", () =>
            {
                SticksHitObject note = EditorBeatmap.HitObjects.OfType<SticksHitObject>().Single();
                return note.Side == StickSide.Right && Math.Abs(note.Angle - 45) < 0.1;
            });
        }

        [Test]
        public void TestPlacementKeepsInputSideOnSharedRing()
        {
            AddStep("select flick placement tool", () =>
            {
                // The stock editor rejects placement before the first explicit timing point.
                EditorBeatmap.ControlPointInfo.Clear();
                EditorBeatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                EditorClock.Seek(2000);
                InputManager.PressKey(Key.Number2);
                InputManager.ReleaseKey(Key.Number2);
            });
            AddUntilStep("flick placement available", () => this.ChildrenOfType<SticksFlickPlacementBlueprint>().Any());
            AddStep("place outside ring", () =>
            {
                InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(30, SticksPlayfield.GUIDE_RADIUS + 16)));
            });
            AddWaitStep("update outside placement", 3);
            AddStep("click outside ring", () => InputManager.Click(MouseButton.Left));
            AddUntilStep("left note placed", () => EditorBeatmap.HitObjects.Count == 1);
            AddAssert("outside input keeps left hand and angle", () =>
            {
                var note = (SticksFlick)EditorBeatmap.HitObjects.Single();
                return note.Side == StickSide.Left && Math.Abs(note.Angle - 30) < 0.1;
            });
            AddStep("seek next placement time", () => EditorClock.Seek(2500));
            AddStep("place inside ring", () =>
            {
                InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(135, SticksPlayfield.GUIDE_RADIUS - 16)));
            });
            AddWaitStep("update inside placement", 3);
            AddStep("click inside ring", () => InputManager.Click(MouseButton.Left));
            AddUntilStep("right note placed", () => EditorBeatmap.HitObjects.Count == 2);
            AddAssert("inside input keeps right hand and angle", () =>
            {
                var note = (SticksFlick)EditorBeatmap.HitObjects.Last();
                return note.Side == StickSide.Right && Math.Abs(note.Angle - 135) < 0.1;
            });
            AddStep("undo second placement", () => Editor.Undo());
            AddUntilStep("only first left note remains", () => EditorBeatmap.HitObjects.Count == 1
                && ((SticksFlick)EditorBeatmap.HitObjects.Single()).Side == StickSide.Left);
            AddStep("redo second placement", () => Editor.Redo());
            AddUntilStep("right note restored", () => EditorBeatmap.HitObjects.Count == 2
                && ((SticksFlick)EditorBeatmap.HitObjects.Last()).Side == StickSide.Right);
        }

        [Test]
        public void TestTailDragCanMakeSliderStationaryAndMovingAgain()
        {
            osu.Framework.Graphics.Drawable tail() => selectionBlueprints().Single()
                .ChildrenOfType<osu.Framework.Graphics.Drawable>().Single(d => d.Name == "Slider tail");

            void dragTailTo(float angle)
            {
                InputManager.MoveMouseTo(tail().ScreenSpaceDrawQuad.Centre);
                InputManager.PressKey(Key.ShiftLeft);
                InputManager.PressButton(MouseButton.Left);
                InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(angle, SticksPlayfield.GUIDE_RADIUS)));
            }

            void releaseTail()
            {
                InputManager.ReleaseButton(MouseButton.Left);
                InputManager.ReleaseKey(Key.ShiftLeft);
            }

            AddStep("add moving slider", () =>
            {
                EditorBeatmap.Add(new SticksSlider { StartTime = 2000, Duration = 1000, Angle = 30, ArcAngle = 90 });
                EditorClock.Seek(3000);
                selectAllNotes();
            });
            AddUntilStep("tail handle visible", () => selectionBlueprints().Length == 1 && tail().Alpha == 1);
            AddStep("drag tail to start direction", () => dragTailTo(30));
            AddStep("release stationary tail", releaseTail);
            AddUntilStep("path has exactly zero movement", () => currentSlider().TotalAngularDistance == 0);
            AddAssert("stationary duration preserved", () => currentSlider().Duration == 1000);
            AddStep("undo stationary edit", () => Editor.Undo());
            AddUntilStep("original arc restored", () => Math.Abs(currentSlider().ArcAngle - 90) < 0.01);
            AddStep("redo stationary edit", () => Editor.Redo());
            AddUntilStep("stationary edit restored", () => currentSlider().TotalAngularDistance == 0);
            AddStep("reselect slider", selectAllNotes);
            AddWaitStep("update restored tail", 3);
            AddStep("drag stationary tail to new direction", () => dragTailTo(120));
            AddStep("release moving tail", releaseTail);
            AddUntilStep("slider moves again", () => Math.Abs(currentSlider().ArcAngle - 90) < 0.01);
            AddAssert("moving duration preserved", () => currentSlider().Duration == 1000);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestTurnHandlesPreserveTimingAndLaterEndpoints(bool independentlyTimed)
        {
            double[] spanDurations = Array.Empty<double>();
            float dragRadius = 0;
            Drawable? handle(string name) => selectionBlueprints().FirstOrDefault()?.ChildrenOfType<Drawable>()
                .SingleOrDefault(drawable => drawable.Name == name);
            bool visible(string name) => handle(name)?.IsPresent == true;
            bool hasPath(params float[] arcs) => currentSlider().SegmentCount == arcs.Length
                && arcs.Select((arc, index) => Math.Abs(currentSlider().SegmentArcAngleAt(index) - arc) < 0.01).All(matches => matches);
            bool timingAndLaterEndpointsUnchanged() => currentSlider().StartTime == 2000 && currentSlider().Duration == 1000
                && currentSlider().Angle == 15 && currentSlider().Side == StickSide.Right
                && spanDurations.Select((duration, index) => Math.Abs(currentSlider().SegmentDurationAt(index) - duration) < 0.001).All(matches => matches)
                && Math.Abs(currentSlider().SegmentStartAngleAt(2) + 30) < 0.01
                && Math.Abs(currentSlider().SegmentStartAngleAt(3) - 150) < 0.01;
            void moveTo(float angle) => InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(angle, dragRadius)));

            AddStep("select slider with two reversals", () =>
            {
                ((SticksRulesetConfigManager)RulesetConfigs.GetConfigFor(new SticksRuleset())!).SetValue(SticksRulesetSetting.ApproachRate, 5f);
                EditorClock.Stop();
                var slider = new SticksSlider { StartTime = 2000, Duration = 1000, Angle = 15, Side = StickSide.Right };
                if (independentlyTimed)
                    slider.SetTimedSegments(initialSegments, new double[] { 200, 300, 500 });
                else
                    slider.SetCustomSegments(initialSegments);
                spanDurations = Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentDurationAt).ToArray();
                slider.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(slider);
                EditorClock.Seek(2100);
                selectAllNotes();
            });
            AddUntilStep("both turn handles and tail are visible", () => visible("Slider turn 1") && visible("Slider turn 2") && visible("Slider tail"));
            AddStep("handles follow each endpoint's time and direction", () =>
            {
                for (int i = 0; i < currentSlider().SegmentCount; i++)
                {
                    string name = i == currentSlider().SegmentCount - 1 ? "Slider tail" : $"Slider turn {i + 1}";
                    float radius = SticksEditorCoordinates.RadiusAt(EditorClock.CurrentTimeAccurate, currentSlider().SegmentEndTimeAt(i), currentSlider().ApproachDuration);
                    Vector2 expected = playfield().ToScreenSpace(SticksPlayfield.PointAt(currentSlider().SegmentStartAngleAt(i + 1), radius));
                    Assert.That((handle(name)!.ScreenSpaceDrawQuad.Centre - expected).Length, Is.LessThan(0.1f), name);
                }
            });
            AddStep("drag first turn with Shift snapping", () =>
            {
                dragRadius = SticksEditorCoordinates.RadiusAt(EditorClock.CurrentTimeAccurate, currentSlider().SegmentEndTimeAt(0), currentSlider().ApproachDuration);
                InputManager.MoveMouseTo(handle("Slider turn 1")!.ScreenSpaceDrawQuad.Centre);
                InputManager.PressKey(Key.ShiftLeft);
                InputManager.PressButton(MouseButton.Left);
                moveTo(137);
            });
            AddUntilStep("incoming arc snaps and outgoing arc compensates", () => hasPath(120, -165, 180));
            AddAssert("drag leaves all times and subsequent endpoints fixed", timingAndLaterEndpointsUnchanged);
            AddStep("continue turn through next quarter", () => moveTo(227));
            AddStep("continue turn through next half", () => moveTo(317));
            AddStep("continue turn across zero", () => moveTo(47));
            AddStep("finish full revolution", () => moveTo(137));
            AddUntilStep("drag keeps a full rotation instead of wrapping", () => hasPath(480, -525, 180));
            AddStep("release turn", () =>
            {
                InputManager.ReleaseButton(MouseButton.Left);
                InputManager.ReleaseKey(Key.ShiftLeft);
            });
            AddAssert("completed turn edit preserves timing and remaining path", timingAndLaterEndpointsUnchanged);
            AddAssert("authored marker contains the edited path", markerIsCurrent);
            AddStep("undo entire turn drag", () => Editor.Undo());
            AddUntilStep("one undo restores the original three spans", () => hasPath(initialSegments));
            AddAssert("undo retains original span timing", timingAndLaterEndpointsUnchanged);
            AddStep("redo turn drag", () => Editor.Redo());
            AddUntilStep("redo restores the entire rotation", () => hasPath(480, -525, 180));
            AddAssert("redo retains original span timing", timingAndLaterEndpointsUnchanged);
            AddStep("reselect edited slider and seek past first turn", () =>
            {
                selectAllNotes();
                EditorClock.Seek(2420);
            });
            AddUntilStep("past turn hides while later endpoints remain", () => !visible("Slider turn 1") && visible("Slider turn 2") && visible("Slider tail"));
            AddStep("seek beyond slider end", () => EditorClock.Seek(3050));
            AddUntilStep("past endpoints are not draggable", () => !visible("Slider turn 1") && !visible("Slider turn 2") && !visible("Slider tail"));
            AddStep("rewind to approaching turns", () => EditorClock.Seek(2100));
            AddUntilStep("rewinding restores all endpoint handles", () => visible("Slider turn 1") && visible("Slider turn 2") && visible("Slider tail"));
            AddAssert("seeking does not alter the edited slider", () => hasPath(480, -525, 180) && timingAndLaterEndpointsUnchanged());
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void TestTurnDragAtExactReversalTimeKeepsAuthoredStart(bool gradual, bool paired)
        {
            SticksSlider[] sliders() => EditorBeatmap.HitObjects.OfType<SticksSlider>().ToArray();
            SticksSelectionBlueprint selection() => selectionBlueprints().First();
            Drawable turn() => selection().ChildrenOfType<Drawable>().Single(drawable => drawable.Name == "Slider turn 1");
            bool turnMoved(SticksSlider slider) => Math.Abs(slider.SegmentArcAngleAt(0) - 102) < 0.01
                && Math.Abs(slider.SegmentArcAngleAt(1) + 147) < 0.01 && slider.SegmentArcAngleAt(2) == 180;
            AddStep("select slider exactly on first reversal", () =>
            {
                EditorClock.Stop();
                foreach (StickSide side in paired ? new[] { StickSide.Left, StickSide.Right } : new[] { StickSide.Right })
                {
                    var slider = new SticksSlider { StartTime = 2000, Duration = 1000, Angle = 15, Side = side };
                    slider.SetTimedSegments(initialSegments, new double[] { 200, 300, 500 });
                    slider.EnsureLegacyEditorMarker();
                    EditorBeatmap.Add(slider);
                }
                EditorClock.Seek(sliders()[0].SegmentEndTimeAt(0));
                selectAllNotes();
            });
            AddUntilStep("turn handle visible at contact point", () => selectionBlueprints().Length == (paired ? 2 : 1) && turn().IsPresent);
            AddStep("confirm turn and selection contact coincide", () =>
                Assert.That((turn().ScreenSpaceDrawQuad.Centre - selection().ScreenSpaceSelectionPoint).Length, Is.LessThan(0.1f)));
            AddStep("press first reversal handle", () =>
            {
                InputManager.MoveMouseTo(turn().ScreenSpaceDrawQuad.Centre);
                InputManager.PressButton(MouseButton.Left);
            });
            foreach (float targetAngle in gradual ? new float[] { 107, 109, 111, 113, 115, 117 } : new float[] { 117 })
            {
                AddStep($"drag turn to {targetAngle} degrees", () => InputManager.MoveMouseTo(
                    playfield().ToScreenSpace(SticksPlayfield.PointAt(targetAngle, SticksPlayfield.GUIDE_RADIUS))));
            }
            AddStep("release reversal", () => InputManager.ReleaseButton(MouseButton.Left));
            AddAssert("authored start does not move", () => sliders().All(slider => Math.Abs(slider.Angle - 15) < 0.01 && slider.StartTime == 2000));
            AddUntilStep("only adjoining arcs change", () => sliders().Any(turnMoved)
                && sliders().All(slider => turnMoved(slider) || slider.SegmentArcAngles.SequenceEqual(initialSegments)));
            AddAssert("current contact follows the moved turn", () => sliders().Where(turnMoved)
                .All(slider => Math.Abs(slider.AngleAt(EditorClock.CurrentTimeAccurate) - 117) < 0.01));
            AddStep("rewind to authored head", () => EditorClock.Seek(2000));
            AddUntilStep("original head still renders at its saved angle", () =>
            {
                Vector2 expected = playfield().ToScreenSpace(SticksPlayfield.PointAt(15, SticksPlayfield.GUIDE_RADIUS));
                return selectionBlueprints().Length == (paired ? 2 : 1)
                       && selectionBlueprints().All(blueprint => (blueprint.ScreenSpaceSelectionPoint - expected).Length < 0.1f);
            });
            AddStep("undo exact-time turn drag", () => Editor.Undo());
            AddUntilStep("one undo restores original start and turns", () => sliders().All(slider => slider.Angle == 15 && slider.StartTime == 2000
                && slider.SegmentArcAngles.SequenceEqual(initialSegments)));
        }

        [TestCase(246)]
        [TestCase(295)]
        public void TestUndoCancelsUnfinishedSliderBeforeEarlierEdits(float radius)
        {
            SticksSliderPlacementBlueprint? pending = null;
            void undoShortcut()
            {
                InputManager.PressKey(Key.ControlLeft);
                InputManager.PressKey(Key.Z);
                InputManager.ReleaseKey(Key.Z);
                InputManager.ReleaseKey(Key.ControlLeft);
            }

            AddStep("place earlier note and choose slider", () =>
            {
                EditorBeatmap.ControlPointInfo.Clear();
                EditorBeatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                EditorBeatmap.Add(new SticksFlick { StartTime = 1000, Side = StickSide.Right });
                // The placed head snaps to 2000, but the current clock can be just off-grid.
                EditorClock.Seek(2003.5);
                InputManager.PressKey(Key.Number3);
                InputManager.ReleaseKey(Key.Number3);
            });
            AddUntilStep("slider tool available", () => this.ChildrenOfType<SticksSliderPlacementBlueprint>().Any());
            AddStep("move to slider start", () =>
                InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(30, radius))));
            AddWaitStep("update placement position", 3);
            AddStep("click slider start without drawing arc", () => InputManager.Click(MouseButton.Left));
            AddUntilStep("slider placement is active", () =>
            {
                pending = this.ChildrenOfType<SticksSliderPlacementBlueprint>().Single();
                return pending.PlacementActive == osu.Game.Rulesets.Edit.PlacementBlueprint.PlacementState.Active;
            });
            AddStep("undo unfinished start", undoShortcut);
            AddUntilStep("unfinished slider cancelled", () =>
                pending!.PlacementActive == osu.Game.Rulesets.Edit.PlacementBlueprint.PlacementState.Finished);
            AddAssert("earlier edit remains", () => EditorBeatmap.HitObjects.Count == 1
                && EditorBeatmap.HitObjects.Single() is SticksFlick);
            AddStep("undo earlier edit normally", undoShortcut);
            AddUntilStep("earlier note removed", () => EditorBeatmap.HitObjects.Count == 0);
            AddStep("redo earlier edit normally", () => Editor.Redo());
            AddUntilStep("earlier note restored without partial slider", () => EditorBeatmap.HitObjects.Count == 1
                && EditorBeatmap.HitObjects.Single() is SticksFlick);
        }

        [TestCase(Key.Number2, typeof(SticksFlick))]
        [TestCase(Key.Number3, typeof(SticksSlider), true)]
        [TestCase(Key.Number3, typeof(SticksSlider))]
        [TestCase(Key.Number4, typeof(SticksClick))]
        public void TestOuterPlacementCreatesExactPairAndHidesInvalidPreview(Key toolKey, Type noteType, bool stationary = false)
        {
            SticksBlueprintPiece preview() => this.ChildrenOfType<osu.Game.Rulesets.Edit.HitObjectPlacementBlueprint>()
                .Single().ChildrenOfType<SticksBlueprintPiece>().Single();

            void move(float radius, float angle = 0) =>
                InputManager.MoveMouseTo(playfield().ToScreenSpace(SticksPlayfield.PointAt(angle, radius)));

            bool exactPair()
            {
                var notes = EditorBeatmap.HitObjects.OfType<SticksHitObject>().ToArray();
                return notes.Length == 2 && notes.All(n => n.GetType() == noteType)
                    && notes[0].Side != notes[1].Side && notes[0].Angle == notes[1].Angle
                    && notes[0].StartTime == notes[1].StartTime;
            }

            AddStep("choose placement tool", () =>
            {
                EditorBeatmap.ControlPointInfo.Clear();
                EditorBeatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                EditorClock.Seek(2000);
                InputManager.PressKey(toolKey);
                InputManager.ReleaseKey(toolKey);
            });
            AddUntilStep("placement available", () => this.ChildrenOfType<osu.Game.Rulesets.Edit.HitObjectPlacementBlueprint>().Any());
            AddStep("enter both-stick band", () => move(340));
            AddUntilStep("preview visible", () => preview().Alpha == 1);
            AddAssert("preview uses purple", () =>
                (osuTK.Graphics.Color4)typeof(SticksBlueprintPiece).GetField("displayedColour", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(preview())! == playfield().OverlapColour);
            AddStep("move past placement boundary", () => move(365));
            AddUntilStep("outer invalid preview hidden", () => preview().Alpha == 0);
            AddStep("click invalid area", () => InputManager.Click(MouseButton.Left));
            AddAssert("invalid click places nothing", () => EditorBeatmap.HitObjects.Count == 0);
            AddStep("move inside minimum radius", () => move(100));
            AddUntilStep("inner invalid preview hidden", () => preview().Alpha == 0);
            AddStep("return to both-stick band", () => move(340));
            AddUntilStep("purple preview returns", () => preview().Alpha == 1);
            AddStep("begin paired placement", () => InputManager.PressButton(MouseButton.Left));
            AddStep("trace duration gesture", () =>
            {
                if (stationary)
                    move(190);
                if (noteType == typeof(SticksSlider) && !stationary)
                    move(340, 70);
            });
            AddWaitStep("update duration preview", 3);
            AddStep("release on starting beat", () => InputManager.ReleaseButton(MouseButton.Left));
            AddAssert("mouse position alone cannot finish slider", () => noteType != typeof(SticksSlider)
                || EditorBeatmap.HitObjects.Count == 0 && this.ChildrenOfType<SticksSliderPlacementBlueprint>().Single().HitObject.Duration == 0);
            AddStep("scroll to end time", () => { if (noteType == typeof(SticksSlider)) EditorClock.Seek(3000); });
            AddWaitStep("update end time", 3);
            AddStep("change radius without changing time", () => { if (noteType == typeof(SticksSlider)) move(220, stationary ? 0 : 70); });
            AddWaitStep("update pointer", 3);
            AddAssert("duration comes only from scroll", () => noteType != typeof(SticksSlider)
                || this.ChildrenOfType<SticksSliderPlacementBlueprint>().Single().HitObject.Duration == 1000);
            AddStep("scroll backwards to shorter end", () => { if (noteType == typeof(SticksSlider)) EditorClock.Seek(2500); });
            AddWaitStep("shorten draft", 3);
            AddAssert("scrolling back shortens duration", () => noteType != typeof(SticksSlider)
                || this.ChildrenOfType<SticksSliderPlacementBlueprint>().Single().HitObject.Duration == 500);
            AddStep("finish paired slider", () => { if (noteType == typeof(SticksSlider)) InputManager.Click(MouseButton.Left); });
            AddUntilStep("two exact opposite-hand notes placed", exactPair);
            AddAssert("duration and path match", () =>
            {
                var notes = EditorBeatmap.HitObjects.ToArray();
                if (notes[0] is SticksSlider firstSlider && notes[1] is SticksSlider secondSlider)
                    return firstSlider.Duration > 0 && (stationary ? firstSlider.ArcAngle == 0 : Math.Abs(firstSlider.ArcAngle) > 1)
                        && firstSlider.Duration == secondSlider.Duration
                        && firstSlider.SegmentArcAngles.SequenceEqual(secondSlider.SegmentArcAngles);
                return true;
            });
            AddStep("undo pair once", () => Editor.Undo());
            AddUntilStep("both notes removed together", () => EditorBeatmap.HitObjects.Count == 0);
            AddStep("redo pair once", () => Editor.Redo());
            AddUntilStep("both notes restored together", exactPair);
        }

        [Test]
        public void TestLargeSelectionKeepsOnlyVisibleGameplayRibbonsAcrossSeeks()
        {
            const int note_count = 1008;
            long memoryBefore = 0;
            double[] seekTimes = { 24000d, 4000, 18000, 0, 26000, 8000, 30000, 6000 };
            AddStep("add large mixed timeline", () =>
            {
                var notes = Enumerable.Range(0, note_count).Select(index =>
                {
                    SticksHitObject note = (index % 4) switch
                    {
                        0 => new SticksFlick(),
                        1 => new SticksClick(),
                        2 => new SticksHold { Duration = 250 },
                        _ => new SticksSlider { Duration = 250, ArcAngle = 45 },
                    };
                    note.StartTime = 2000 + index * 25;
                    note.Side = index / 4 % 2 == 0 ? StickSide.Left : StickSide.Right;
                    note.Angle = index * 37 % 360;
                    note.EnsureLegacyEditorMarker();
                    return note;
                }).ToArray();
                EditorBeatmap.AddRange(notes);
                EditorClock.Seek(6000);
                selectAllNotes();
            });
            AddUntilStep("all notes selected", () => EditorBeatmap.SelectedHitObjects.Count == note_count);
            AddWaitStep("settle large selection", 15);
            AddAssert("selection overlays do not duplicate gameplay rendering", assertLightweightSelections);
            foreach (double time in seekTimes)
            {
                AddStep($"warm timeline region at {time}ms", () => EditorClock.Seek(time));
                AddWaitStep("settle first-time object loading", 3);
            }
            AddStep("collect selection baseline", () =>
            {
                collectGarbage();
                memoryBefore = GC.GetTotalMemory(false);
            });

            foreach (double time in seekTimes)
            {
                AddStep($"seek large selection to {time}ms", () => EditorClock.Seek(time));
                AddWaitStep("settle large seek", 3);
                AddAssert("selected objects remain bounded and finite", () =>
                {
                    Assert.That(EditorBeatmap.SelectedHitObjects.Count, Is.EqualTo(note_count));
                    Assert.That(visibleRibbonCount(), Is.LessThan(80), $"Selection must not force all 504 duration bodies to render at {EditorClock.CurrentTimeAccurate}ms.");
                    return assertLightweightSelections() && assertVisibleSelectionPositions();
                });
            }
            AddStep("verify bounded retained seek memory", () =>
            {
                collectGarbage();
                long retained = GC.GetTotalMemory(false) - memoryBefore;
                TestContext.Progress.WriteLine($"Large-selection seeks retained {retained / 1024d / 1024:0.0} MiB");
                Assert.That(retained, Is.LessThan(64L * 1024 * 1024));
            });
            AddStep("clear selected map", () => EditorBeatmap.Clear());
            AddUntilStep("cleared map has no remaining ribbons", () => visibleRibbonCount() == 0);
        }

        private SticksPlayfield playfield() => this.ChildrenOfType<SticksPlayfield>().Single();

        private SticksSelectionBlueprint[] selectionBlueprints() => this.ChildrenOfType<SticksSelectionBlueprint>().ToArray();

        private void selectAllNotes()
        {
            EditorBeatmap.SelectedHitObjects.Clear();
            EditorBeatmap.SelectedHitObjects.AddRange(EditorBeatmap.HitObjects);
        }

        private int visibleRibbonCount() => this.ChildrenOfType<SticksRadialTimelinePath>().Count(path =>
            path.IsAlive && path.IsPresent && (int)typeof(SticksRadialTimelinePath).GetField("pointCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(path)! > 1);

        private void reportRibbonOwners()
        {
            foreach (SticksRadialTimelinePath path in this.ChildrenOfType<SticksRadialTimelinePath>())
            {
                var ancestors = new System.Collections.Generic.List<string>();
                for (osu.Framework.Graphics.Drawable? parent = path; parent != null; parent = parent.Parent)
                {
                    ancestors.Add($"{parent.GetType().Name} alive={parent.IsAlive} present={parent.IsPresent} life={parent.LifetimeStart}:{parent.LifetimeEnd}");
                    if (parent is SticksPlayfield)
                        break;
                }
                TestContext.Progress.WriteLine(string.Join(" <- ", ancestors));
            }
        }

        private bool assertLightweightSelections()
        {
            SticksSelectionBlueprint[] blueprints = selectionBlueprints();
            Assert.That(blueprints, Is.Not.Empty);
            Assert.That(blueprints.SelectMany(blueprint => blueprint.ChildrenOfType<BufferedContainer>()), Is.Empty);
            Assert.That(blueprints.SelectMany(blueprint => blueprint.ChildrenOfType<SmoothPath>()), Is.Empty);
            Assert.That(blueprints.SelectMany(blueprint => blueprint.ChildrenOfType<SticksRadialTimelinePath>()), Is.Empty);
            return true;
        }

        private bool assertVisibleSelectionPositions()
        {
            double now = EditorClock.CurrentTimeAccurate;
            SticksPlayfield currentPlayfield = playfield();
            int nonFiniteCount = 0;
            float maximumError = 0;
            string largestMismatch = string.Empty;
            foreach (SticksSelectionBlueprint blueprint in selectionBlueprints())
            {
                Vector2 local = currentPlayfield.ToLocalSpace(blueprint.ScreenSpaceSelectionPoint);
                if (!float.IsFinite(local.X) || !float.IsFinite(local.Y))
                    nonFiniteCount++;
                var note = (SticksHitObject)blueprint.Item;
                double end = note switch
                {
                    SticksSlider slider => slider.EndTime,
                    SticksHold hold => hold.EndTime,
                    _ => note.StartTime,
                };
                if (now <= note.StartTime - note.ApproachDuration || now > end)
                    continue;
                float progress = (float)Math.Clamp((now - note.StartTime + note.ApproachDuration) / note.ApproachDuration, 0, 1);
                float angle = note is SticksSlider moving ? moving.AngleAt(Math.Clamp(now, moving.StartTime, moving.EndTime)) : note.Angle;
                Vector2 expected = SticksPlayfield.PointAt(angle, SticksPlayfield.GUIDE_RADIUS * progress);
                float error = (local - expected).Length;
                if (error > maximumError)
                {
                    maximumError = error;
                    largestMismatch = $"Selection anchor diverged for {note.GetType().Name} at {now}ms (head {note.StartTime}ms).";
                }
            }
            Assert.That(nonFiniteCount, Is.Zero);
            Assert.That(maximumError, Is.LessThan(0.1f), largestMismatch);
            return true;
        }

        private void addSegmentedSlider()
        {
            var slider = new SticksSlider
            {
                StartTime = 2000,
                Duration = initial_duration,
                Side = StickSide.Left,
                Angle = 15,
            };

            slider.SetCustomSegments(initialSegments);
            slider.EnsureLegacyEditorMarker();
            EditorBeatmap.Add(slider);
        }

        private static void collectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private SticksSlider currentSlider() => EditorBeatmap.HitObjects.OfType<SticksSlider>().Single();

        private bool pathIsUnchanged()
        {
            SticksSlider slider = currentSlider();
            return slider.HasCustomSegments
                   && slider.RepeatCount == initialSegments.Length - 1
                   && slider.SegmentArcAngles.SequenceEqual(initialSegments);
        }

        private bool markerIsCurrent()
        {
            SticksSlider slider = currentSlider();
            return slider.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>()
                         .Any(marker => marker.Filename == SticksAuthoredBeatmapCodec.EncodeMarker(slider));
        }
    }
}
