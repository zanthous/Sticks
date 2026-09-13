using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    public partial class SticksClickGameplayTest : OsuTestScene
    {
        private ManualClock manual = null!;
        private FramedClock clock = null!;
        private DrawableSticksRuleset drawable = null!;
        private SticksReplayInputProvider input = null!;
        private SticksPlayfield playfield => (SticksPlayfield)drawable.Playfield;
        private DrawableSticksClick left => drawable.ChildrenOfType<DrawableSticksClick>().First(c => c.HitObject.Side == StickSide.Left);
        private DrawableSticksClick right => drawable.ChildrenOfType<DrawableSticksClick>().First(c => c.HitObject.Side == StickSide.Right);

        [TestCase(false)]
        [TestCase(true)]
        public void TestButtonsJudgeOnlyTheirOwnHand(bool strum)
        {
            AddStep("create click pair", () =>
            {
                Clear();
                var ruleset = new SticksRuleset();
                var beatmap = new Beatmap<SticksHitObject>
                {
                    BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, new BeatmapDifficulty()),
                };
                beatmap.HitObjects.Add(new SticksClick { StartTime = 1000, Side = StickSide.Left });
                beatmap.HitObjects.Add(new SticksClick { StartTime = 1000, Side = StickSide.Right });
                foreach (var hitObject in beatmap.HitObjects)
                    hitObject.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
                manual = new ManualClock { CurrentTime = 500 };
                clock = new FramedClock(manual);
                clock.ProcessFrame();
                drawable = new DrawableSticksRuleset(ruleset, beatmap) { Clock = clock };
                input = (SticksReplayInputProvider)typeof(DrawableSticksRuleset)
                    .GetField("replayInputProvider", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(drawable)!;
                input.Update(Vector2.Zero, Vector2.Zero);
                playfield.StrumMode = strum;
                Add(drawable);
            });
            AddUntilStep("halos loaded", () => drawable.ChildrenOfType<DrawableSticksClick>().Count() == 2 && left.IsLoaded && right.IsLoaded);
            AddWaitStep("observe neutral buttons", 2);
            AddAssert("double halo is purple", () => left.ChildrenOfType<CircularContainer>().Single().BorderColour.AverageColour == playfield.OverlapColour);
            AddStep("reach hit time", () =>
            {
                manual.CurrentTime = 1000;
                clock.ProcessFrame();
            });
            AddWaitStep("finish clock seek", 2);
            AddStep("press left shoulder", () => input.Update(Vector2.Zero, Vector2.Zero, leftShoulder: true));
            AddWaitStep("process shoulder", 2);
            AddAssert("shoulder follows strum restriction", () => left.Judged == !strum);
            AddAssert("right hand untouched", () => !right.Judged);
            AddStep("press left stick while shoulder stays held", () => input.Update(Vector2.Zero, Vector2.Zero, leftShoulder: true, leftStickButton: true));
            AddUntilStep("left click judged", () => left.Judged);
            AddAssert("left timing is perfect without aim", () => left.Result.Type == HitResult.Great);
            AddAssert("remaining halo is red", () => right.ChildrenOfType<CircularContainer>().Single().BorderColour.AverageColour == playfield.ColourFor(StickSide.Right));
            AddStep("press right trigger", () => input.Update(Vector2.Zero, Vector2.Zero, rightTrigger: true, leftShoulder: true, leftStickButton: true));
            AddWaitStep("process trigger", 2);
            AddAssert("trigger follows strum restriction", () => right.Judged == !strum);
            AddStep("press right stick", () => input.Update(Vector2.Zero, Vector2.Zero, rightTrigger: true, rightStickButton: true));
            AddUntilStep("right click judged", () => right.Judged);
            AddAssert("right timing is perfect without aim", () => right.Result.Type == HitResult.Great);
            AddAssert("both scoring halves follow the click", () =>
                drawable.ChildrenOfType<osu.Game.Rulesets.Objects.Drawables.DrawableHitObject>()
                        .Where(d => d.HitObject is SticksClick.TimingWeight)
                        .All(d => d.Result.Type == HitResult.Great));
            AddStep("rewind before clicks", () =>
            {
                input.Update(Vector2.Zero, Vector2.Zero);
                manual.CurrentTime = 500;
                clock.ProcessFrame();
            });
            AddUntilStep("clicks and weights reset", () =>
                drawable.ChildrenOfType<osu.Game.Rulesets.Objects.Drawables.DrawableHitObject>()
                        .Where(d => d.HitObject is SticksClick or SticksClick.TimingWeight)
                        .All(d => !d.Judged));
            AddStep("relax at click time", () =>
            {
                playfield.RelaxMode = true;
                manual.CurrentTime = 1000;
                clock.ProcessFrame();
            });
            AddUntilStep("relax resolves both halves", () =>
                drawable.ChildrenOfType<osu.Game.Rulesets.Objects.Drawables.DrawableHitObject>()
                        .Where(d => d.HitObject is SticksClick or SticksClick.TimingWeight)
                        .All(d => d.Result.Type == HitResult.Great));
        }
    }
}
