// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;
using osu.Game.Rulesets.Sticks.Objects;
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
        public void TestTimelineEndHandleResizesDurationWithoutChangingPath()
        {
            AddStep("add segmented slider", () =>
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
            AddAssert("path unchanged after resize", pathIsUnchanged);
            AddAssert("carrier marker follows duration", markerIsCurrent);

            AddStep("undo resize", () => Editor.Undo());
            AddUntilStep("duration restored", () => currentSlider().Duration == initial_duration);
            AddAssert("path unchanged after undo", pathIsUnchanged);

            AddStep("redo resize", () => Editor.Redo());
            AddUntilStep("resized duration restored", () => Math.Abs(currentSlider().Duration - resizedDuration) <= 0.001);
            AddAssert("path unchanged after redo", pathIsUnchanged);
            AddAssert("carrier marker current after redo", markerIsCurrent);
        }

        [Test]
        public void TestReversalPlacementStartsWithOneClickAndUsesVisibleConfirmation()
        {
            AddStep("add slider", () =>
            {
                continuationSlider = new SticksSlider
                {
                    StartTime = 2000,
                    Duration = 1000,
                    Side = StickSide.Left,
                    Angle = 0,
                    ArcAngle = 90,
                };
                continuationSlider.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(continuationSlider);
            });
            AddStep("seek to end and select", () =>
            {
                EditorClock.Seek(continuationSlider.EndTime);
                EditorBeatmap.SelectedHitObjects.Add(continuationSlider);
            });
            AddUntilStep("reversal button available", () => drawAlpha(icon(FontAwesome.Solid.AngleDoubleRight)) > 0.9f);

            AddStep("click reversal button once", () =>
            {
                SpriteIcon reversal = icon(FontAwesome.Solid.AngleDoubleRight);
                InputManager.MoveMouseTo(reversal);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("disabled confirmation appears", () => drawAlpha(icon(FontAwesome.Solid.Check)) > 0.1f);
            AddAssert("no segment committed yet", () => continuationSlider.SegmentCount == 1);

            AddStep("seek forward", () => EditorClock.Seek(3500));
            AddUntilStep("confirmation enabled", () => drawAlpha(icon(FontAwesome.Solid.Check)) > 0.9f);
            AddStep("click visible confirmation", () =>
            {
                SpriteIcon confirmation = icon(FontAwesome.Solid.Check);
                InputManager.MoveMouseTo(confirmation);
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("reversal committed", () => continuationSlider.SegmentCount == 2);
            AddAssert("duration follows selected time", () => Math.Abs(continuationSlider.Duration - 1500) <= 0.001);
            AddAssert("new segment reverses direction", () => Math.Abs(continuationSlider.SegmentArcAngleAt(1) - -45) <= 0.001);
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

        private SpriteIcon icon(IconUsage usage) =>
            this.ChildrenOfType<SticksSelectionBlueprint>().Single()
                .ChildrenOfType<SpriteIcon>().Single(candidate => candidate.Icon.Equals(usage));

        private static float drawAlpha(SpriteIcon icon) => icon.DrawColourInfo.Colour.AverageColour.Linear.A;

    }
}
