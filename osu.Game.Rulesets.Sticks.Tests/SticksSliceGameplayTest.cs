using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    public partial class SticksSliceGameplayTest : OsuTestScene
    {
        private ManualClock manual = null!;
        private FramedClock clock = null!;
        private DrawableSticksRuleset drawable = null!;
        private SticksReplayInputProvider input = null!;
        private SticksPlayfield playfield => (SticksPlayfield)drawable.Playfield;

        [TestCase(false)]
        [TestCase(true)]
        public void OppositeStickOnlyWorksWithTheModAndCanContinueThroughSlices(bool eitherStick)
        {
            create(eitherStick,
                new SticksSlice { StartTime = 1000, Side = StickSide.Right, Direction = SticksSliceDirection.Clockwise },
                new SticksSlice { StartTime = 1200, Side = StickSide.Right, Angle = 30 });
            move(970, at(-12), Vector2.Zero);
            move(1000, at(0), Vector2.Zero);
            AddAssert("first pass respects hand requirement", () => sliceAt(1000).Judged == eitherStick);
            move(1170, at(18), Vector2.Zero);
            move(1200, at(30), Vector2.Zero);
            AddAssert("no recharge needed between slices", () => sliceAt(1200).Judged == eitherStick);
            if (eitherStick)
            {
                AddAssert("full timing credit", () => drawable.ChildrenOfType<DrawableHitObject>()
                    .Where(d => d.HitObject is SticksSlice or SticksClick.TimingWeight).All(d => d.Result.Type == HitResult.Great));
                move(500, Vector2.Zero, Vector2.Zero);
                AddUntilStep("rewind resets slices", () => drawable.ChildrenOfType<DrawableSticksSlice>().All(d => !d.Judged));
                move(970, at(12), Vector2.Zero);
                move(1000, at(-12), Vector2.Zero);
                AddAssert("wrong rotation rejected after rewind", () => !sliceAt(1000).Judged);
            }
        }

        [Test]
        public void ADoubleSliceStillRequiresTwoSticksWithAmbidextrous()
        {
            create(true,
                new SticksSlice { StartTime = 1000, Side = StickSide.Left },
                new SticksSlice { StartTime = 1000, Side = StickSide.Right });
            move(970, at(-12), at(-12));
            move(1000, at(0), at(-12));
            AddAssert("one crossing hits only one slice", () => drawable.ChildrenOfType<DrawableSticksSlice>().Count(d => d.Judged) == 1);
            move(1010, at(0), at(0));
            AddAssert("other side hits the remaining slice", () => drawable.ChildrenOfType<DrawableSticksSlice>().All(d => d.Result.Type.IsHit()));
        }

        [Test]
        public void EitherStickTracksTheSliderItGrabbedAndCanHitTheOtherColour()
        {
            create(true,
                new SticksSlider { StartTime = 1000, Duration = 1000, Angle = 0, ArcAngle = 90, Side = StickSide.Right },
                new SticksFlick { StartTime = 1500, Angle = 180, Side = StickSide.Left });
            move(1000, at(0), Vector2.Zero);
            AddStep("check physical owner", () => Assert.That(slider.TrackingAuthorised && slider.TrackingSide == StickSide.Left, Is.True, $"head={slider.HeadJudged}, side={slider.TrackingSide}, authorised={slider.TrackingAuthorised}, either={playfield.EitherStick}, sequence={playfield.FlickSequence(StickSide.Left)}, time={playfield.LastFlick(StickSide.Left).Time}"));
            move(1250, at(22.5f), Vector2.Zero);
            move(1500, at(45), at(180));
            AddStep("check opposite flick", () =>
            {
                var flick = drawable.ChildrenOfType<DrawableSticksFlick>().Single();
                Assert.That(flick.Result.Type, Is.EqualTo(HitResult.Great), $"judged={flick.Judged}, event={playfield.LastFlick(StickSide.Right)}, actualtime={flick.Time.Current}");
            });
            move(1750, at(67.5f), Vector2.Zero);
            move(2000, at(90), Vector2.Zero);
            AddAssert("slider tail follows physical left stick", () => drawable.ChildrenOfType<DrawableSticksSliderTail>().Single().Result.Type == HitResult.SliderTailHit);
        }

        [Test]
        public void ReacquiringPairedSlidersUsesOneGesturePerSlider()
        {
            create(true,
                new SticksSlider { StartTime = 1000, Duration = 1000, Side = StickSide.Left },
                new SticksSlider { StartTime = 1000, Duration = 1000, Side = StickSide.Right });
            move(1250, Vector2.Zero, Vector2.Zero);
            AddAssert("both heads missed", () => drawable.ChildrenOfType<DrawableSticksSlider>().All(d => d.HeadJudged && !d.TrackingAuthorised));
            move(1300, at(0), at(0));
            AddAssert("both can reacquire", () => drawable.ChildrenOfType<DrawableSticksSlider>().All(d => d.TrackingAuthorised));
            AddAssert("each owns a different stick", () => drawable.ChildrenOfType<DrawableSticksSlider>().Select(d => d.TrackingSide).Distinct().Count() == 2);
        }

        [Test]
        public void SimultaneousClicksDoNotConsumeTheSamePendingTarget()
        {
            create(true,
                new SticksClick { StartTime = 1000, Side = StickSide.Left },
                new SticksClick { StartTime = 1000, Side = StickSide.Right });
            move(1000, Vector2.Zero, Vector2.Zero);
            AddStep("press both click buttons", () => input.Update(Vector2.Zero, Vector2.Zero, leftStickButton: true, rightStickButton: true));
            AddUntilStep("both clicks hit", () => drawable.ChildrenOfType<DrawableSticksClick>().All(d => d.Result.Type == HitResult.Great));
        }

        private DrawableSticksSlice sliceAt(double time) => drawable.ChildrenOfType<DrawableSticksSlice>().Single(d => d.HitObject.StartTime == time);
        private DrawableSticksSlider slider => drawable.ChildrenOfType<DrawableSticksSlider>().Single();

        private void create(bool eitherStick, params SticksHitObject[] notes)
        {
            AddStep("create playfield", () =>
            {
                Clear();
                var ruleset = new SticksRuleset();
                var beatmap = new Beatmap<SticksHitObject>
                {
                    BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, new BeatmapDifficulty()),
                };
                beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                beatmap.HitObjects.AddRange(notes);
                foreach (var note in notes)
                    note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
                manual = new ManualClock { CurrentTime = 900 };
                clock = new FramedClock(manual);
                clock.ProcessFrame();
                drawable = new DrawableSticksRuleset(ruleset, beatmap) { Clock = clock };
                if (eitherStick)
                    new SticksModAmbidextrous().ApplyToDrawableRuleset(drawable);
                input = (SticksReplayInputProvider)typeof(DrawableSticksRuleset)
                    .GetField("replayInputProvider", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(drawable)!;
                input.Update(Vector2.Zero, Vector2.Zero);
                Add(drawable);
            });
            AddUntilStep("playfield loaded", () => drawable.IsLoaded
                && drawable.ChildrenOfType<DrawableHitObject>().Count(d => d.HitObject is SticksSlice or SticksSlider or SticksFlick or SticksClick) == notes.Length
                && drawable.ChildrenOfType<DrawableHitObject>().All(d => d.IsLoaded));
            AddWaitStep("observe neutral sticks", 3);
        }

        private void move(double time, Vector2 left, Vector2 right)
        {
            AddStep($"approach {time}", () =>
            {
                manual.CurrentTime = time - 1;
                clock.ProcessFrame();
            });
            AddWaitStep("settle clock", 2);
            AddStep($"input at {time}", () =>
            {
                input.Update(left, right);
                manual.CurrentTime = time;
                clock.ProcessFrame();
            });
            AddWaitStep("process input", 2);
        }

        private static Vector2 at(float degrees) => new Vector2(MathF.Cos(degrees * MathF.PI / 180), MathF.Sin(degrees * MathF.PI / 180));
    }
}
