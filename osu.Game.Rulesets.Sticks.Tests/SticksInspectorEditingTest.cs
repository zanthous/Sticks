#nullable enable

using System;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Testing;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Edit.Blueprints;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Screens.Edit.Compose.Components;
using osu.Game.Tests.Visual;
using osuTK.Input;
using StickChoice = osu.Game.Rulesets.Sticks.Edit.SticksHitObjectInspector.StickChoice;

namespace osu.Game.Rulesets.Sticks.Tests
{
    public partial class SticksInspectorEditingTest : EditorTestScene
    {
        private readonly InspectorTextInputSource textInput = new InspectorTextInputSource();

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs<TextInputSource>(textInput);
            return dependencies;
        }

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
        public void TestSliderPointSizesCommitAndUndoWithoutChangingTiming()
        {
            SticksSlider slider() => EditorBeatmap.HitObjects.OfType<SticksSlider>().Single();
            AddStep("select slider with a turn", () =>
            {
                var note = new SticksSlider { StartTime = 2000, Duration = 1500 };
                note.SetTimedSegments(new[] { 90f, -90f }, new[] { 500d, 1000d });
                addSelected(note);
            });
            AddUntilStep("head properties appear", () => hasField("Point 0 size") && hasField("Angle") && hasField("Start time"));
            AddAssert("only selected head properties shown", () => !hasField("Point 1 size") && !hasField("Point 2 size")
                && !hasField("Duration") && hasField("Segment 1 speed") && !hasField("Segment 2 speed"));
            AddStep("seek to first point", () => EditorClock.Seek(2500));
            AddUntilStep("first point handle available", () => pointHandle("Slider turn 1").IsPresent);
            AddStep("click first point", () => clickHandle("Slider turn 1"));
            AddUntilStep("inspector follows point click", () => hasField("Point 1 size") && hasField("Segment 2 speed") && pointField().Current.Value == 1);
            AddAssert("head and other point fields hidden", () => !hasField("Angle") && !hasField("Start time") && !hasField("Point 0 size") && !hasField("Point 2 size"));
            AddStep("widen turn", () => field("Point 1 size").Text = "2");
            AddWaitStep("process size draft", 2);
            AddStep("commit turn size", () => commit("Point 1 size"));
            AddUntilStep("only turn width changed", () => slider().NodeSizeAt(0) == 1 && slider().NodeSizeAt(1) == 2 && slider().NodeSizeAt(2) == 1 && !hasError());
            AddAssert("midpoints interpolate and timing is unchanged", () => slider().SizeMultiplierAt(2250) == 1.5f
                && slider().SizeMultiplierAt(3000) == 1.5f && slider().SegmentDurationAt(0) == 500 && slider().Duration == 1500);
            AddStep("undo point size", () => Editor.Undo());
            AddUntilStep("undo restores uniform width", () => !slider().HasNodeSizes);
            AddStep("redo point size", () => Editor.Redo());
            AddUntilStep("redo restores size profile", () => slider().NodeSizeAt(1) == 2);
            AddStep("reselect restored slider", () =>
            {
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.Add(slider());
            });
            AddUntilStep("selection resets to head", () => hasField("Point 0 size"));
            AddStep("choose restored point", () => selectPoint(1));
            AddUntilStep("turn field reflects restored size", () => hasField("Point 1 size") && valueIs("Point 1 size", 2));
            AddStep("choose last point", () => selectPoint(2));
            AddUntilStep("last point has only its size", () => hasField("Point 2 size") && !hasSegmentFields() && !hasField("Point 1 size"));
            AddStep("enter invalid point size", () => field("Point 2 size").Text = "0");
            AddWaitStep("process invalid size draft", 2);
            AddStep("commit invalid size", () => commit("Point 2 size"));
            AddUntilStep("invalid size rejected", () => hasError() && slider().NodeSizeAt(2) == 1 && valueIs("Point 2 size", 1));
            AddStep("seek between points", () => EditorClock.Seek(2750));
            AddWaitStep("update visible head", 2);
            AddAssert("original head position has no stray selection handle", () =>
            {
                var selection = this.ChildrenOfType<SticksSelectionBlueprint>().Single();
                return !selection.ReceivePositionalInputAt(selection.ToScreenSpace(SticksPlayfield.PointAt(0, SticksPlayfield.GUIDE_RADIUS)));
            });
            AddStep("click visible slider head", () =>
            {
                InputManager.MoveMouseTo(this.ChildrenOfType<SticksSelectionBlueprint>().Single().ScreenSpaceSelectionPoint);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("head selected again", () => hasField("Point 0 size") && pointField().Current.Value == 0);
            AddStep("change head size", () => field("Point 0 size").Text = "1.5");
            AddStep("commit head size", () => commit("Point 0 size"));
            AddUntilStep("only head size changes", () => slider().NodeSizeAt(0) == 1.5f && slider().NodeSizeAt(1) == 2 && slider().NodeSizeAt(2) == 1);
        }

        [Test]
        public void TestSelectionOutlineFollowsPointAndBodyClickSelectsHead()
        {
            SticksSlider slider() => EditorBeatmap.HitObjects.OfType<SticksSlider>().Single();
            SticksSelectionBlueprint selection() => this.ChildrenOfType<SticksSelectionBlueprint>().Single();
            SelectionBox outline() => this.ChildrenOfType<SticksSelectionHandler>().Single().ChildrenOfType<SelectionBox>().Single();
            bool headHighlighted() => selection().ChildrenOfType<SticksBlueprintPiece>().Single().ChildrenOfType<CircularProgress>().Single().Alpha > 0;
            bool outlinesPoint(string name) => outline().IsPresent
                && (outline().ScreenSpaceDrawQuad.Centre - pointHandle(name).ScreenSpaceDrawQuad.Centre).Length < 0.1f;
            bool outlinesHead() => outline().IsPresent
                && (outline().ScreenSpaceDrawQuad.Centre - selection().ScreenSpaceSelectionPoint).Length < 0.1f;

            AddStep("select slider with a wider ribbon", () =>
            {
                var note = new SticksSlider { StartTime = 2000, Duration = 200 };
                note.SetTimedSegments(new[] { 90f, -90f }, new[] { 100d, 100d });
                note.SetNodeSizeMultipliers(new[] { 1f, 3f, 3f });
                addSelected(note);
            });
            AddUntilStep("head initially selected", () => hasField("Point 0 size") && outlinesHead() && headHighlighted());
            AddStep("click first point", () => clickHandle("Slider turn 1"));
            AddUntilStep("outline follows clicked point", () => hasField("Point 1 size") && outlinesPoint("Slider turn 1") && !headHighlighted());
            AddStep("choose tail in inspector", () => selectPoint(2));
            AddUntilStep("outline follows inspector selection", () => hasField("Point 2 size") && outlinesPoint("Slider tail") && !headHighlighted());
            AddStep("click wide ribbon away from any point", () =>
            {
                const double pathTime = 2150;
                double approach = this.ChildrenOfType<SticksHitObjectComposer>().Single().PlayerApproachDuration;
                float radius = SticksEditorCoordinates.RadiusAt(EditorClock.CurrentTimeAccurate, pathTime, approach);
                var position = selection().ToScreenSpace(SticksPlayfield.PointAt(slider().AngleAt(pathTime) - 24, radius));
                Assert.That(selection().ReceivePositionalInputAt(position), Is.True);
                InputManager.MoveMouseTo(position);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("body selects head properties and highlight", () => hasField("Point 0 size") && pointField().Current.Value == 0
                && outlinesHead() && headHighlighted());
            AddStep("select first point again", () => selectPoint(1));
            AddUntilStep("point highlighted again", () => outlinesPoint("Slider turn 1") && !headHighlighted());
            AddStep("seek past selected point", () => EditorClock.Seek(2110));
            AddUntilStep("hidden point leaves no phantom outline or head highlight", () => !pointHandle("Slider turn 1").IsPresent
                && !outline().IsPresent && !headHighlighted() && pointField().Current.Value == 1);
            AddStep("rewind to selected point", () => EditorClock.Seek(2000));
            AddUntilStep("selected point outline returns", () => outlinesPoint("Slider turn 1") && !headHighlighted());
            AddStep("rotate slider from its head", () =>
            {
                InputManager.MoveMouseTo(selection().ScreenSpaceSelectionPoint);
                InputManager.PressButton(MouseButton.Left);
                InputManager.MoveMouseTo(selection().ToScreenSpace(SticksPlayfield.PointAt(30, SticksPlayfield.GUIDE_RADIUS)));
            });
            AddStep("release head", () => InputManager.ReleaseButton(MouseButton.Left));
            AddUntilStep("head drag still rotates the complete object", () => Math.Abs(slider().Angle - 30) < 0.1
                && slider().SegmentArcAngles.SequenceEqual(new[] { 90f, -90f }) && slider().Duration == 200
                && pointField().Current.Value == 0 && outlinesHead() && headHighlighted());
        }

        [Test]
        public void TestPointSelectionDiscardsQueuedDraftAndIncludesOrdinaryJoins()
        {
            SticksSlider slider() => EditorBeatmap.HitObjects.OfType<SticksSlider>().Single();
            OsuTextBox oldSize = null!;
            AddStep("select slider with ordinary joins", () =>
            {
                var note = new SticksSlider { StartTime = 2000, Duration = 1500 };
                note.SetTimedSegments(new[] { 90f, 45f, 45f }, new[] { 500d, 500d, 500d });
                addSelected(note);
                EditorClock.Seek(2500);
            });
            AddUntilStep("head properties loaded", () => hasField("Point 0 size"));
            AddStep("focus head size", () =>
            {
                focus("Point 0 size");
                oldSize = field("Point 0 size");
            });
            AddUntilStep("head size text selected", () => oldSize.SelectedText == "1");
            AddStep("queue text and select another point", () =>
            {
                textInput.Type("3");
                selectPoint(2);
            });
            AddUntilStep("new point has its own field", () => hasField("Point 2 size") && valueIs("Point 2 size", 1) && !oldSize.HasFocus);
            AddAssert("draft did not change any point", () => Enumerable.Range(0, 4).All(i => slider().NodeSizeAt(i) == 1));
            AddUntilStep("ordinary join can be selected", () => pointHandle("Slider turn 1").IsPresent);
            AddStep("select ordinary join on playfield", () => clickHandle("Slider turn 1"));
            AddUntilStep("inspector shows only the following segment", () => pointField().Current.Value == 1 && hasField("Segment 2 duration")
                && !hasField("Segment 1 duration") && !hasField("Segment 3 duration") && !hasField("Point 0 size"));
            AddStep("select last point", () => selectPoint(3));
            AddUntilStep("last point selected without segment controls", () => hasField("Point 3 size") && !hasSegmentFields());
            AddStep("shorten slider externally", () =>
            {
                slider().SetTimedSegments(new[] { 90f }, new[] { 1500d });
                EditorBeatmap.Update(slider());
            });
            AddUntilStep("selection follows remaining endpoint", () => pointField().Current.Value == 1 && hasField("Point 1 size")
                && !hasField("Point 3 size") && !hasSegmentFields());
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void TestHeadEditsFirstSegmentSpeedTurnAndDuration(bool multipleSegments, bool paired)
        {
            SticksSlider[] sliders() => EditorBeatmap.HitObjects.OfType<SticksSlider>().ToArray();
            bool pathIs(float firstAngle, double firstDuration = 500) => sliders().Length == (paired ? 2 : 1)
                && sliders().All(slider => slider.Angle == 30 && slider.StartTime == 2000 && slider.NodeSizeAt(0) == 1
                    && slider.SegmentArcAngles.SequenceEqual(multipleSegments ? new[] { firstAngle, 45f } : new[] { firstAngle })
                    && Math.Abs(slider.SegmentDurationAt(0) - firstDuration) < 0.001
                    && slider.Duration == firstDuration + (multipleSegments ? 1000 : 0)
                    && (!multipleSegments || Math.Abs(slider.SegmentDurationAt(1) - 1000) < 0.001));

            AddStep("select slider heads", () =>
            {
                var notes = (paired ? new[] { StickSide.Left, StickSide.Right } : new[] { StickSide.Left }).Select(side =>
                {
                    var note = new SticksSlider { StartTime = 2000, Duration = multipleSegments ? 1500 : 500, Angle = 30, Side = side };
                    note.SetTimedSegments(multipleSegments ? new[] { -90f, 45f } : new[] { -90f },
                        multipleSegments ? new[] { 500d, 1000d } : new[] { 500d });
                    return note;
                }).ToArray();
                addSelected(notes);
            });
            AddUntilStep("head exposes complete first segment controls", () => hasField("Point 0 size") && hasField("Angle") && hasField("Start time")
                && hasField("Segment 1 speed") && valueIs("Segment 1 speed", -180)
                && valueIs("Segment 1 angle", -90) && valueIs("Segment 1 duration", 500) && pointField().Current.Value == 0);
            AddAssert("later segment fields are hidden", () => !hasField("Segment 2 speed"));
            AddStep("focus head speed", () => focus("Segment 1 speed"));
            AddUntilStep("head speed text selected", () => field("Segment 1 speed").SelectedText == "-180");
            AddStep("type new speed", () => textInput.Type("-360"));
            AddUntilStep("new speed received", () => valueIs("Segment 1 speed", -360));
            AddStep("commit speed from head", enter);
            AddUntilStep("only first segment changes and retains direction", () => pathIs(-180) && !hasError() && pointField().Current.Value == 0);
            AddStep("undo head speed edit", () => Editor.Undo());
            AddUntilStep("undo restores first segment", () => pathIs(-90));
            AddStep("redo head speed edit", () => Editor.Redo());
            AddUntilStep("redo restores first segment speed", () => pathIs(-180));
            AddStep("reselect restored heads", () =>
            {
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.AddRange(sliders());
            });
            AddUntilStep("head speed restored in inspector", () => hasField("Point 0 size") && hasField("Segment 1 speed") && valueIs("Segment 1 speed", -360));
            AddStep("change first segment duration", () => field("Segment 1 duration").Text = "250");
            AddStep("commit head duration", () => commit("Segment 1 duration"));
            AddUntilStep("only first segment timing changes", () => pathIs(-180, 250) && valueIs("Segment 1 speed", -720) && !hasError());
            AddStep("change first segment turn", () => field("Segment 1 angle").Text = "-45");
            AddStep("commit head turn", () => commit("Segment 1 angle"));
            AddUntilStep("head turn updates speed readout", () => pathIs(-45, 250) && valueIs("Segment 1 speed", -180) && !hasError());
            AddStep("enter invalid speed", () => field("Segment 1 speed").Text = "NaN");
            AddStep("commit invalid head speed", () => commit("Segment 1 speed"));
            AddUntilStep("invalid head speed rejected", () => hasError() && pathIs(-45, 250) && valueIs("Segment 1 speed", -180));
        }

        [Test]
        public void TestPointEditsFollowingSegmentAndSupportsUndoAndValidation()
        {
            SticksSlider slider() => EditorBeatmap.HitObjects.OfType<SticksSlider>().Single();
            bool pathIs(float secondAngle, double secondDuration = 1000) => slider().Angle == 30 && slider().StartTime == 2000
                && slider().Duration == 750 + secondDuration
                && slider().SegmentArcAngles.SequenceEqual(new[] { 90f, secondAngle, 45f })
                && Math.Abs(slider().SegmentDurationAt(0) - 500) < 0.001
                && Math.Abs(slider().SegmentDurationAt(1) - secondDuration) < 0.001
                && Math.Abs(slider().SegmentDurationAt(2) - 250) < 0.001;

            AddStep("select slider with reversal", () =>
            {
                var note = new SticksSlider { StartTime = 2000, Duration = 1750, Angle = 30 };
                note.SetTimedSegments(new[] { 90f, -90f, 45f }, new[] { 500d, 1000d, 250d });
                addSelected(note);
            });
            AddUntilStep("head loaded", () => hasField("Point 0 size"));
            AddUntilStep("head shows first segment speed", () => hasField("Segment 1 speed") && valueIs("Segment 1 speed", 180));
            AddStep("select first point", () => selectPoint(1));
            AddUntilStep("point shows following segment speed", () => hasField("Segment 2 speed") && valueIs("Segment 2 speed", -90));
            AddStep("focus second segment speed", () => focus("Segment 2 speed"));
            AddUntilStep("speed selected", () => field("Segment 2 speed").SelectedText == "-90");
            AddStep("type desired speed", () => textInput.Type("-240"));
            AddUntilStep("speed draft received", () => valueIs("Segment 2 speed", -240));
            AddStep("commit speed", enter);
            AddUntilStep("only turn angle changes", () => pathIs(-240) && valueIs("Segment 2 angle", -240) && !hasError());
            AddAssert("unselected segment fields stay hidden", () => !hasField("Segment 1 speed") && !hasField("Segment 3 speed"));
            AddStep("undo speed edit", () => Editor.Undo());
            AddUntilStep("one undo restores previous path", () => pathIs(-90));
            AddStep("redo speed edit", () => Editor.Redo());
            AddUntilStep("redo restores requested speed", () => pathIs(-240));
            AddStep("select restored slider", () =>
            {
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.Add(slider());
            });
            AddUntilStep("restored head loaded", () => hasField("Point 0 size"));
            AddStep("reselect first point", () => selectPoint(1));
            AddUntilStep("speed field restored", () => hasField("Segment 2 speed") && valueIs("Segment 2 speed", -240));
            AddStep("set positive speed", () => field("Segment 2 speed").Text = "240");
            AddStep("commit positive speed", () => commit("Segment 2 speed"));
            AddUntilStep("positive speed reverses only following segment", () => pathIs(240) && valueIs("Segment 2 angle", 240) && !hasError());
            AddStep("set negative speed", () => field("Segment 2 speed").Text = "-240");
            AddStep("commit negative speed", () => commit("Segment 2 speed"));
            AddUntilStep("negative speed reverses only following segment", () => pathIs(-240) && valueIs("Segment 2 angle", -240) && !hasError());
            AddStep("enter invalid speed", () => field("Segment 2 speed").Text = "NaN");
            AddWaitStep("update invalid draft", 2);
            AddStep("commit invalid speed", () => commit("Segment 2 speed"));
            AddUntilStep("invalid speed rejected without modifying slider", () => hasError() && pathIs(-240) && valueIs("Segment 2 speed", -240));
            AddStep("set speed to zero", () => field("Segment 2 speed").Text = "0");
            AddWaitStep("update zero speed draft", 2);
            AddStep("commit on focus loss", () => focus("Point 1 size"));
            AddUntilStep("segment is stationary with original timing", () => pathIs(0) && valueIs("Segment 2 angle", 0) && !hasError());
            AddStep("change following segment duration", () => field("Segment 2 duration").Text = "750");
            AddStep("commit point duration", () => commit("Segment 2 duration"));
            AddUntilStep("only following segment timing changes", () => pathIs(0, 750) && !hasError());
            AddStep("change following segment turn", () => field("Segment 2 angle").Text = "-60");
            AddStep("commit point turn", () => commit("Segment 2 angle"));
            AddUntilStep("only following segment shape changes", () => pathIs(-60, 750) && valueIs("Segment 2 speed", -80) && !hasError());
            AddStep("select next point", () => selectPoint(2));
            AddUntilStep("next point shows its own following segment", () => hasField("Segment 3 speed") && valueIs("Segment 3 speed", 180)
                && valueIs("Segment 3 angle", 45) && valueIs("Segment 3 duration", 250) && !hasField("Segment 2 speed"));
            AddStep("select a single-segment slider", () => addSelected(new SticksSlider { StartTime = 4000, Duration = 500, ArcAngle = 60 }));
            AddUntilStep("single slider head loaded", () => hasField("Point 0 size"));
            AddUntilStep("single-segment speed is editable too", () => hasField("Segment 1 speed") && !hasField("Segment 2 speed") && valueIs("Segment 1 speed", 120));
            AddStep("select single segment endpoint", () => selectPoint(1));
            AddUntilStep("endpoint has no following segment controls", () => hasField("Point 1 size") && !hasSegmentFields());
        }

        [Test]
        public void TestSizeInputCommitsAndSelectionChangeDrainsPendingText()
        {
            OsuTextBox sizeBox = null!;
            SticksFlick first() => EditorBeatmap.HitObjects.OfType<SticksFlick>().Single(note => note.StartTime == 2000);
            SticksFlick second() => EditorBeatmap.HitObjects.OfType<SticksFlick>().Single(note => note.StartTime == 2500);

            AddStep("add two notes and select first", () =>
            {
                addSelected(new SticksFlick { StartTime = 2000, Angle = 30 }, new SticksFlick { StartTime = 2500, Angle = 90 });
                EditorBeatmap.SelectedHitObjects.Remove(second());
            });
            AddUntilStep("size field loaded", () => hasField("Size"));
            AddStep("focus size", () => focus("Size"));
            AddUntilStep("initial size selected", () => field("Size").HasFocus && field("Size").SelectedText == "1");
            AddStep("type double size through text input", () => textInput.Type("2"));
            AddUntilStep("typed size displayed", () => valueIs("Size", 2));
            AddStep("commit size with Enter", enter);
            AddUntilStep("size committed", () => first().SizeMultiplier == 2 && !hasError());
            AddStep("undo size", () => Editor.Undo());
            AddUntilStep("size restored by undo", () => first().SizeMultiplier == 1);
            AddStep("redo size", () => Editor.Redo());
            AddUntilStep("double size restored by redo", () => first().SizeMultiplier == 2);
            AddStep("select first note again", () =>
            {
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.Add(first());
            });
            AddUntilStep("restored size field loaded", () => hasField("Size") && valueIs("Size", 2));
            AddStep("focus restored size", () =>
            {
                focus("Size");
                sizeBox = field("Size");
                sizeBox.SelectAll();
            });
            AddUntilStep("double size selected", () => sizeBox.HasFocus && sizeBox.SelectedText == "2");
            AddStep("receive text just before selection rebuild", () =>
            {
                // Native input can arrive after the input manager's update but before
                // the inspector rebuilds, leaving work in the old textbox's input queue.
                textInput.Type("3");
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.Add(second());
            });
            AddUntilStep("new selection has its own size field", () => hasField("Size")
                && !ReferenceEquals(field("Size"), sizeBox) && valueIs("Size", 1));
            AddUntilStep("removed field no longer focused", () => !sizeBox.HasFocus);
            AddAssert("pending draft cannot change either note", () => first().SizeMultiplier == 2 && second().SizeMultiplier == 1);
            AddStep("new field remains editable", () =>
            {
                focus("Size");
                textInput.Type("2");
            });
            AddUntilStep("new field receives text", () => valueIs("Size", 2));
            AddStep("commit by focusing Angle", () => focus("Angle"));
            AddUntilStep("new selection size committed", () => second().SizeMultiplier == 2 && !hasError());
        }

        [Test]
        public void TestFieldsCommitIndependentlyWithoutLosingFocus()
        {
            OsuTextBox editedAngle = null!;
            OsuTextBox editedDuration = null!;
            OsuTextBox speed = null!;
            double draftStarted = 0;
            SticksSlider slider() => EditorBeatmap.HitObjects.OfType<SticksSlider>().Single();

            AddStep("add selected slider", () => addSelected(new SticksSlider
            {
                StartTime = 2000,
                Duration = 1000,
                Side = StickSide.Left,
                Angle = 30,
                ArcAngle = 90,
            }));
            AddUntilStep("head properties ready", () => hasField("Point 0 size"));
            AddUntilStep("editable slider fields loaded", () => hasField("Segment 1 angle") && hasField("Segment 1 duration"));
            AddStep("hover inspector angle", () => InputManager.MoveMouseTo(field("Segment 1 angle")));
            AddWaitStep("expand inspector", 3);
            AddStep("focus and enter an unfinished draft", () =>
            {
                editedAngle = field("Segment 1 angle");
                editedDuration = field("Segment 1 duration");
                speed = field("Segment 1 speed");
                InputManager.MoveMouseTo(editedAngle);
                InputManager.Click(MouseButton.Left);
                editedAngle.Text = "75";
                editedDuration.Text = "-5";
                draftStarted = Time.Current;
            });
            AddUntilStep("wait longer than old inspector refresh", () => Time.Current - draftStarted >= 400);
            AddAssert("same focused field retains draft", () => ReferenceEquals(field("Segment 1 angle"), editedAngle)
                && editedAngle.HasFocus && editedAngle.Text == "75" && field("Segment 1 duration").Text == "-5");
            AddAssert("typing has not edited the slider", () => slider().Angle == 30 && slider().ArcAngle == 90 && slider().Duration == 1000);
            AddAssert("no Apply or Reset buttons", () => !inspector().ChildrenOfType<OsuButton>()
                .Any(button => button.Text.ToString() is "Apply" or "Reset"));
            AddStep("click Duration to commit Angle", () =>
            {
                InputManager.MoveMouseTo(editedDuration);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("focus transfer commits only Angle", () => slider().ArcAngle == 75 && editedDuration.HasFocus);
            AddAssert("other field draft remains uncommitted", () => slider().Duration == 1000
                && ReferenceEquals(field("Segment 1 duration"), editedDuration) && editedDuration.Text == "-5");
            AddStep("commit invalid duration with Enter", enter);
            AddUntilStep("validation error appears", hasError);
            AddAssert("invalid duration changes no authored properties", () => slider().ArcAngle == 75 && slider().Duration == 1000
                && slider().StartTime == 2000 && slider().Angle == 30);
            AddAssert("invalid field restores its value and keeps focus", () => valueIs("Segment 1 duration", 1000) && editedDuration.HasFocus);
            AddStep("correct duration", () => field("Segment 1 duration").Text = "1500");
            AddWaitStep("update valid draft", 2);
            AddStep("commit valid duration with Enter", enter);
            AddUntilStep("duration and dependent values update", () => slider().Duration == 1500
                && valueIs("Segment 1 speed", 50) && valueIs("Segment 1 duration", 1500));
            AddAssert("refresh preserves controls and focused field", () => ReferenceEquals(field("Segment 1 angle"), editedAngle)
                && ReferenceEquals(field("Segment 1 duration"), editedDuration) && ReferenceEquals(field("Segment 1 speed"), speed)
                && editedDuration.HasFocus && !hasError());
            AddStep("undo duration edit", () => Editor.Undo());
            AddUntilStep("undo restores duration but retains committed angle", () => slider().ArcAngle == 75 && slider().Duration == 1000);
            AddStep("undo angle edit", () => Editor.Undo());
            AddUntilStep("second undo restores angle", () => slider().Angle == 30 && slider().ArcAngle == 90 && slider().Duration == 1000);
            AddStep("redo angle edit", () => Editor.Redo());
            AddUntilStep("redo angle only", () => slider().ArcAngle == 75 && slider().Duration == 1000);
            AddStep("redo duration edit", () => Editor.Redo());
            AddUntilStep("redo duration", () => slider().ArcAngle == 75 && slider().Duration == 1500);
            AddStep("select restored slider", () =>
            {
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.Add(slider());
            });
            AddUntilStep("restored head properties ready", () => hasField("Point 0 size"));
            AddUntilStep("segment fields loaded", () => hasField("Segment 1 angle") && hasField("Segment 1 duration"));
            AddStep("draft stationary and shorter segment", () =>
            {
                field("Segment 1 angle").Text = "0";
                field("Segment 1 duration").Text = "750";
            });
            AddWaitStep("update segment draft", 2);
            AddStep("commit segment angle", () => commit("Segment 1 angle"));
            AddUntilStep("segment angle commits separately", () => slider().SegmentArcAngleAt(0) == 0 && slider().Duration == 1500);
            AddStep("commit segment duration", () => commit("Segment 1 duration"));
            AddUntilStep("segment timing updates total duration", () => slider().Angle == 30 && slider().Duration == 750
                && slider().SegmentArcAngleAt(0) == 0 && slider().TotalAngularDistance == 0);
            AddStep("undo segment duration", () => Editor.Undo());
            AddUntilStep("segment duration undo preserves committed shape", () => slider().Angle == 30 && slider().Duration == 1500
                && slider().SegmentArcAngleAt(0) == 0);
        }

        [Test]
        public void TestPurplePairEditsTogetherAndSelectionChangeRetargetsControls()
        {
            SticksFlick unrelated = null!;
            SticksFlick note(StickSide side) => EditorBeatmap.HitObjects.OfType<SticksFlick>().Single(hitObject => hitObject.Side == side);
            bool pairAt(float angle, double time) => EditorBeatmap.HitObjects.Count == 2
                && EditorBeatmap.HitObjects.Cast<SticksHitObject>().All(hitObject => hitObject.Angle == angle && hitObject.StartTime == time);

            AddStep("add selected left note", () => addSelected(
                new SticksFlick { StartTime = 2000, Angle = 25, Side = StickSide.Left }));
            AddUntilStep("shared fields loaded", () => hasField("Angle") && hasField("Start time"));
            AddStep("choose Both", () => stickField().Current.Value = StickChoice.Both);
            AddUntilStep("Both creates and selects an exact partner", () => pairAt(25, 2000)
                && EditorBeatmap.SelectedHitObjects.Count == 2 && stickField().Current.Value == StickChoice.Both);
            AddStep("choose Both again", () => stickField().Current.Value = StickChoice.Both);
            AddAssert("Both does not create duplicate partners", () => pairAt(25, 2000));
            AddStep("draft common angle and time", () =>
            {
                field("Angle").Text = "90";
                field("Start time").Text = "2500";
            });
            AddWaitStep("update pair draft", 2);
            AddStep("commit common angle", () => commit("Angle"));
            AddUntilStep("both angles commit without the other draft", () => pairAt(90, 2000));
            AddAssert("time draft remains pending", () => field("Start time").Text == "2500");
            AddAssert("opposite stick assignments preserved", () => EditorBeatmap.HitObjects.Cast<SticksHitObject>()
                .Select(hitObject => hitObject.Side).Distinct().Count() == 2);
            AddStep("undo pair edit", () => Editor.Undo());
            AddUntilStep("one undo restores entire pair", () => pairAt(25, 2000));
            AddStep("select restored pair", () =>
            {
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.AddRange(EditorBeatmap.HitObjects);
            });
            AddUntilStep("restored pair shows Both", () => stickField().Current.Value == StickChoice.Both);
            AddStep("reduce pair to Right", () => stickField().Current.Value = StickChoice.Right);
            AddUntilStep("only selected right note remains", () => EditorBeatmap.HitObjects.Count == 1
                && EditorBeatmap.SelectedHitObjects.Count == 1 && note(StickSide.Right).Angle == 25);
            AddStep("undo pair reduction", () => Editor.Undo());
            AddUntilStep("one undo restores both notes", () => pairAt(25, 2000));
            AddStep("redo pair reduction", () => Editor.Redo());
            AddUntilStep("one redo returns to Right", () => EditorBeatmap.HitObjects.Count == 1 && note(StickSide.Right).Angle == 25);
            AddStep("restore pair again", () => Editor.Undo());
            AddUntilStep("pair restored for individual edit", () => pairAt(25, 2000));
            AddStep("select right note only", () =>
            {
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.Add(note(StickSide.Right));
            });
            AddUntilStep("inspector follows new selection", () => hasField("Angle")
                && stickField().Current.Value == StickChoice.Right);
            AddStep("edit new selection", () => field("Angle").Text = "140");
            AddWaitStep("update single draft", 2);
            AddStep("commit selected note angle", () => commit("Angle"));
            AddUntilStep("only right note changes", () => note(StickSide.Right).Angle == 140 && note(StickSide.Left).Angle == 25);
            AddUntilStep("unrelated opposite-hand note hides Both", () => !stickField().Items.Contains(StickChoice.Both));
            AddStep("remove unselected conflicting note", () =>
            {
                unrelated = note(StickSide.Left);
                EditorBeatmap.Remove(unrelated);
            });
            AddUntilStep("removal makes Both available", () => stickField().Items.Contains(StickChoice.Both));
            AddStep("restore unselected conflicting note", () => EditorBeatmap.Add(unrelated));
            AddUntilStep("adding a conflict hides Both again", () => !stickField().Items.Contains(StickChoice.Both));
            AddStep("edit unselected note into exact partner", () =>
            {
                unrelated.Angle = 140;
                EditorBeatmap.Update(unrelated);
            });
            AddUntilStep("editing an unselected partner refreshes availability", () => stickField().Items.Contains(StickChoice.Both));
            AddStep("make notes different and select both", () =>
            {
                unrelated.Angle = 25;
                EditorBeatmap.Update(unrelated);
                EditorBeatmap.SelectedHitObjects.Add(unrelated);
            });
            AddUntilStep("unpaired selection has display-only Mixed state", () => stickField().Current.Value == StickChoice.Mixed);
            AddAssert("Mixed is not a selectable option and impossible Both stays hidden", () => !stickField().Items.Contains(StickChoice.Mixed)
                && !stickField().Items.Contains(StickChoice.Both));
        }

        [Test]
        public void TestStaleCommitRecoversAndEscapeCancelsDraft()
        {
            SticksFlick note() => EditorBeatmap.HitObjects.OfType<SticksFlick>().Single();

            AddStep("add selected note", () => addSelected(new SticksFlick
            {
                StartTime = 2000,
                Angle = 20,
                Side = StickSide.Left,
            }));
            AddUntilStep("angle field loaded", () => hasField("Angle"));
            AddStep("focus and start draft", () =>
            {
                focus("Angle");
                field("Angle").Text = "90";
            });
            AddWaitStep("update draft", 2);
            AddStep("change selected note outside inspector", () =>
            {
                note().Angle = 45;
                EditorBeatmap.Update(note());
            });
            AddWaitStep("observe external change", 2);
            AddStep("try committing stale draft", () => commit("Angle"));
            AddUntilStep("stale draft warning visible", hasError);
            AddAssert("external change restored in field", () => note().Angle == 45 && valueIs("Angle", 45));
            AddStep("enter fresh angle", () => field("Angle").Text = "100");
            AddWaitStep("update fresh draft", 2);
            AddStep("commit fresh angle without Reset", () => commit("Angle"));
            AddUntilStep("fresh edit succeeds", () => note().Angle == 100 && !hasError());
            AddStep("focus another draft", () =>
            {
                focus("Angle");
                field("Angle").Text = "140";
            });
            AddStep("cancel draft with Escape", () =>
            {
                InputManager.PressKey(Key.Escape);
                InputManager.ReleaseKey(Key.Escape);
            });
            AddUntilStep("Escape restores current value without changing note", () => note().Angle == 100 && valueIs("Angle", 100));
            AddStep("move focus after cancellation", () => focus("Start time"));
            AddAssert("cancelled draft cannot commit on focus loss", () => note().Angle == 100);
        }

        [Test]
        public void TestClickFieldsRejectCollisionsAndBothReusesExistingPartner()
        {
            SticksClick edited = null!;
            SticksClick partner = null!;
            AddStep("select click note", () =>
            {
                edited = new SticksClick { StartTime = 2000, Side = StickSide.Left };
                addSelected(edited);
            });
            AddUntilStep("click fields loaded", () => hasField("Start time"));
            AddAssert("clicks have no directional or path fields", () => !hasField("Angle") && !hasField("Duration")
                && !hasField("End time") && !inspector().ChildrenOfType<OsuTextBox>().Any(box => box.Name.StartsWith("Segment ", StringComparison.Ordinal)));
            AddStep("draft new timestamp", () => field("Start time").Text = "2500");
            AddWaitStep("update time draft", 2);
            AddStep("add an unselected note at drafted time", () =>
            {
                var collision = new SticksClick { StartTime = 2500, Side = StickSide.Left };
                collision.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(collision);
            });
            AddWaitStep("observe added note", 2);
            AddStep("commit colliding timestamp", () => commit("Start time"));
            AddUntilStep("new collision is rejected", hasError);
            AddAssert("selected click was not moved", () => edited.StartTime == 2000 && edited.Side == StickSide.Left);
            AddStep("choose valid time", () => field("Start time").Text = "2300");
            AddWaitStep("update valid click draft", 2);
            AddStep("commit click time", () => commit("Start time"));
            AddUntilStep("time committed", () => edited.StartTime == 2300 && edited.Side == StickSide.Left);
            AddStep("choose other stick", () => stickField().Current.Value = StickChoice.Right);
            AddUntilStep("stick changes immediately", () => edited.StartTime == 2300 && edited.Side == StickSide.Right);
            AddStep("add an unselected exact partner", () =>
            {
                partner = new SticksClick { StartTime = 2300, Side = StickSide.Left };
                partner.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(partner);
            });
            AddWaitStep("let editor observe partner", 2);
            AddAssert("only original click remains selected", () => EditorBeatmap.SelectedHitObjects.Count == 1
                && EditorBeatmap.SelectedHitObjects.Contains(edited));
            AddStep("choose Both to select the existing pair", () => stickField().Current.Value = StickChoice.Both);
            AddUntilStep("Both reuses the exact unselected partner", () => EditorBeatmap.HitObjects.Count == 3
                && EditorBeatmap.SelectedHitObjects.Count == 2 && EditorBeatmap.SelectedHitObjects.Contains(edited)
                && EditorBeatmap.SelectedHitObjects.Contains(partner) && stickField().Current.Value == StickChoice.Both);
            AddStep("reduce click pair to Left", () => stickField().Current.Value = StickChoice.Left);
            AddUntilStep("requested click retained and unrelated note preserved", () => EditorBeatmap.HitObjects.Count == 2
                && EditorBeatmap.HitObjects.Contains(partner) && !EditorBeatmap.HitObjects.Contains(edited)
                && EditorBeatmap.HitObjects.OfType<SticksClick>().Any(note => note.StartTime == 2500));
        }

        [Test]
        public void TestBothPreservesTimedSliderThroughUndoAndRedo()
        {
            bool matchingSliders(int count) => EditorBeatmap.HitObjects.Count == count
                && EditorBeatmap.HitObjects.OfType<SticksSlider>().Count() == count
                && EditorBeatmap.HitObjects.Cast<SticksSlider>().All(slider => slider.StartTime == 2000
                    && slider.Angle == 35 && slider.Duration == 1000 && slider.HasTimedSegments
                    && slider.SegmentArcAngles.SequenceEqual(new float[] { 45, 0, -90 })
                    && Math.Abs(slider.SegmentDurationAt(0) - 250) < 0.001
                    && Math.Abs(slider.SegmentDurationAt(1) - 150) < 0.001
                    && Math.Abs(slider.SegmentDurationAt(2) - 600) < 0.001);

            AddStep("select timed slider", () =>
            {
                var slider = new SticksSlider { StartTime = 2000, Duration = 1000, Angle = 35, Side = StickSide.Left };
                slider.SetTimedSegments(new float[] { 45, 0, -90 }, new double[] { 250, 150, 600 });
                addSelected(slider);
            });
            AddUntilStep("slider inspector ready", () => hasField("Point 0 size") && pointField().Items.Count() == 4);
            AddStep("choose Both for slider", () => stickField().Current.Value = StickChoice.Both);
            AddUntilStep("both selected sliders retain exact independent timing", () => matchingSliders(2)
                && EditorBeatmap.HitObjects.Cast<SticksSlider>().Select(slider => slider.Side).Distinct().Count() == 2
                && EditorBeatmap.SelectedHitObjects.Count == 2);
            AddStep("undo slider partner creation", () => Editor.Undo());
            AddUntilStep("one undo restores original timed slider", () => matchingSliders(1)
                && EditorBeatmap.HitObjects.Cast<SticksSlider>().Single().Side == StickSide.Left);
            AddStep("redo slider partner creation", () => Editor.Redo());
            AddUntilStep("redo restores both complete paths", () => matchingSliders(2)
                && EditorBeatmap.HitObjects.Cast<SticksSlider>().Select(slider => slider.Side).Distinct().Count() == 2);
        }

        private void addSelected(params SticksHitObject[] notes)
        {
            foreach (SticksHitObject note in notes)
            {
                note.EnsureLegacyEditorMarker();
                EditorBeatmap.Add(note);
            }
            EditorBeatmap.SelectedHitObjects.Clear();
            EditorBeatmap.SelectedHitObjects.AddRange(notes);
        }

        private SticksHitObjectInspector inspector() => this.ChildrenOfType<SticksHitObjectInspector>().Single();

        private OsuDropdown<int> pointField() => inspector().ChildrenOfType<OsuDropdown<int>>().Single(dropdown => dropdown.Name == "Slider point");

        private void selectPoint(int point) => pointField().Current.Value = point;

        private Drawable pointHandle(string name) => this.ChildrenOfType<SticksSelectionBlueprint>().Single()
            .ChildrenOfType<Drawable>().Single(drawable => drawable.Name == name);

        private void clickHandle(string name)
        {
            InputManager.MoveMouseTo(pointHandle(name).ScreenSpaceDrawQuad.Centre);
            InputManager.Click(MouseButton.Left);
        }

        private bool hasField(string name) => inspector().ChildrenOfType<OsuTextBox>().Any(textBox => textBox.Name == name);

        private bool hasSegmentFields() => inspector().ChildrenOfType<OsuTextBox>().Any(textBox => textBox.Name.StartsWith("Segment ", StringComparison.Ordinal));

        private OsuTextBox field(string name) => inspector().ChildrenOfType<OsuTextBox>().Single(textBox => textBox.Name == name);

        private OsuDropdown<StickChoice> stickField() => inspector().ChildrenOfType<OsuDropdown<StickChoice>>()
            .Single(dropdown => dropdown.Name == "Stick");

        private bool valueIs(string name, double expected) => double.TryParse(field(name).Text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double value) && value == expected;

        private void focus(string name) => ((IFocusManager)InputManager).ChangeFocus(field(name));

        private void commit(string name)
        {
            focus(name);
            enter();
        }

        private void enter()
        {
            InputManager.PressKey(Key.Enter);
            InputManager.ReleaseKey(Key.Enter);
        }

        private bool hasError() => inspector().ChildrenOfType<OsuTextFlowContainer>()
            .Any(flow => flow.Name == "Validation error" && flow.IsPresent
                         && flow.ChildrenOfType<SpriteText>().Any(text => !string.IsNullOrWhiteSpace(text.Text.ToString())));

        private sealed class InspectorTextInputSource : TextInputSource
        {
            public void Type(string text) => TriggerTextInput(text);
        }
    }
}
