#nullable enable

using System;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input;
using osu.Framework.Testing;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Tests.Visual;
using osuTK.Input;
using StickChoice = osu.Game.Rulesets.Sticks.Edit.SticksHitObjectInspector.StickChoice;

namespace osu.Game.Rulesets.Sticks.Tests
{
    public partial class SticksInspectorEditingTest : EditorTestScene
    {
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
        public void TestFieldsCommitIndependentlyWithoutLosingFocus()
        {
            OsuTextBox editedAngle = null!;
            OsuTextBox editedDuration = null!;
            OsuTextBox endTime = null!;
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
            AddUntilStep("editable slider fields loaded", () => hasField("Angle") && hasField("Duration"));
            AddStep("hover inspector angle", () => InputManager.MoveMouseTo(field("Angle")));
            AddWaitStep("expand inspector", 3);
            AddStep("focus and enter an unfinished draft", () =>
            {
                editedAngle = field("Angle");
                editedDuration = field("Duration");
                endTime = field("End time");
                InputManager.MoveMouseTo(editedAngle);
                InputManager.Click(MouseButton.Left);
                editedAngle.Text = "75";
                editedDuration.Text = "-5";
                draftStarted = Time.Current;
            });
            AddUntilStep("wait longer than old inspector refresh", () => Time.Current - draftStarted >= 400);
            AddAssert("same focused field retains draft", () => ReferenceEquals(field("Angle"), editedAngle)
                && editedAngle.HasFocus && editedAngle.Text == "75" && field("Duration").Text == "-5");
            AddAssert("typing has not edited the slider", () => slider().Angle == 30 && slider().Duration == 1000);
            AddAssert("no Apply or Reset buttons", () => !inspector().ChildrenOfType<OsuButton>()
                .Any(button => button.Text.ToString() is "Apply" or "Reset"));
            AddStep("click Duration to commit Angle", () =>
            {
                InputManager.MoveMouseTo(editedDuration);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("focus transfer commits only Angle", () => slider().Angle == 75 && editedDuration.HasFocus);
            AddAssert("other field draft remains uncommitted", () => slider().Duration == 1000
                && ReferenceEquals(field("Duration"), editedDuration) && editedDuration.Text == "-5");
            AddStep("commit invalid duration with Enter", enter);
            AddUntilStep("validation error appears", hasError);
            AddAssert("invalid duration changes no authored properties", () => slider().Angle == 75 && slider().Duration == 1000
                && slider().StartTime == 2000 && slider().ArcAngle == 90);
            AddAssert("invalid field restores its value and keeps focus", () => valueIs("Duration", 1000) && editedDuration.HasFocus);
            AddStep("correct duration", () => field("Duration").Text = "1500");
            AddWaitStep("update valid draft", 2);
            AddStep("commit valid duration with Enter", enter);
            AddUntilStep("duration and dependent values update", () => slider().Duration == 1500
                && valueIs("End time", 3500) && valueIs("Segment 1 duration", 1500));
            AddAssert("refresh preserves controls and focused field", () => ReferenceEquals(field("Angle"), editedAngle)
                && ReferenceEquals(field("Duration"), editedDuration) && ReferenceEquals(field("End time"), endTime)
                && editedDuration.HasFocus && !hasError());
            AddStep("undo duration edit", () => Editor.Undo());
            AddUntilStep("undo restores duration but retains committed angle", () => slider().Angle == 75 && slider().Duration == 1000);
            AddStep("undo angle edit", () => Editor.Undo());
            AddUntilStep("second undo restores angle", () => slider().Angle == 30 && slider().Duration == 1000);
            AddStep("redo angle edit", () => Editor.Redo());
            AddUntilStep("redo angle only", () => slider().Angle == 75 && slider().Duration == 1000);
            AddStep("redo duration edit", () => Editor.Redo());
            AddUntilStep("redo duration", () => slider().Angle == 75 && slider().Duration == 1500);
            AddStep("select restored slider", () =>
            {
                EditorBeatmap.SelectedHitObjects.Clear();
                EditorBeatmap.SelectedHitObjects.Add(slider());
            });
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
            AddUntilStep("segment timing updates total duration", () => slider().Angle == 75 && slider().Duration == 750
                && slider().SegmentArcAngleAt(0) == 0 && slider().TotalAngularDistance == 0);
            AddStep("undo segment duration", () => Editor.Undo());
            AddUntilStep("segment duration undo preserves committed shape", () => slider().Angle == 75 && slider().Duration == 1500
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
            AddUntilStep("slider inspector ready", () => hasField("Segment 3 duration"));
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

        private bool hasField(string name) => inspector().ChildrenOfType<OsuTextBox>().Any(textBox => textBox.Name == name);

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
    }
}
