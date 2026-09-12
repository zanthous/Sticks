using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    public partial class SticksClickVisualTest : OsuTestScene
    {
        private const double hit_time = 2000;

        private ManualClock manual = null!;
        private FramedClock clock = null!;
        private DrawableSticksRuleset drawable = null!;

        private DrawableSticksClick click => drawable.ChildrenOfType<DrawableSticksClick>().Single();
        private DrawableSticksFlick flick => drawable.ChildrenOfType<DrawableSticksFlick>().Single();
        private DrawableSticksHold hold => drawable.ChildrenOfType<DrawableSticksHold>().Single();
        private CircularContainer halo => click.ChildrenOfType<CircularContainer>().Single();

        [TestCase(StickSide.Left)]
        [TestCase(StickSide.Right)]
        public void TestHaloStrokeCentreTracksSimultaneousNormalHeads(StickSide side)
        {
            createMixedNotes(side, reverseInsertion: side == StickSide.Right);

            foreach (double progress in new[] { 0.1, 0.5, 1.0 })
            {
                AddStep($"seek to {progress:P0} approach", () =>
                {
                    manual.CurrentTime = hit_time - click.HitObject.ApproachDuration * (1 - progress);
                    clock.ProcessFrame();
                });
                AddWaitStep("update note geometry", 2);
                AddAssert("halo stroke matches both marker centres", () =>
                {
                    // CircularContainer borders extend inward from their outer bounds.
                    // Compare their painted midpoint, not their outer edge, with the
                    // visible CircularProgress geometry used by the ordinary heads.
                    float haloRadius = (halo.DrawWidth - halo.BorderThickness) / 2;
                    float expectedRadius = SticksPlayfield.GUIDE_RADIUS * (float)progress;

                    Assert.Multiple(() =>
                    {
                        Assert.That(halo.BorderThickness, Is.EqualTo(5));
                        Assert.That(haloRadius, Is.EqualTo(expectedRadius).Within(0.001));
                        Assert.That(markerRadius(flick), Is.EqualTo(haloRadius).Within(0.001), "Flick centreline");
                        Assert.That(markerRadius(hold), Is.EqualTo(haloRadius).Within(0.001), "Hold head centreline");

                        if (progress == 1)
                        {
                            CircularContainer guide = drawable.Playfield.ChildrenOfType<CircularContainer>()
                                .Single(c => c.Parent == drawable.Playfield && c.BorderThickness == 2);
                            float guideRadius = (guide.DrawWidth - guide.BorderThickness) / 2;
                            Assert.That(guideRadius, Is.EqualTo(haloRadius).Within(0.001), "Guide and note stroke centres coincide at hit time");
                        }
                    });
                    return true;
                });
            }
        }

        [TestCase(-100, false)]
        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(100, true)]
        public void TestNormalHeadsDrawAfterHaloRegardlessOfTimeOrInsertion(double clickTimeOffset, bool reverseInsertion)
        {
            createMixedNotes(StickSide.Left, clickTimeOffset, reverseInsertion);
            AddAssert("halo precedes both ordinary heads in actual draw order", () =>
            {
                var container = drawable.ChildrenOfType<SticksHitObjectContainer>().Single();
                var children = (IEnumerable<Drawable>)typeof(CompositeDrawable)
                    .GetProperty("InternalChildren", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(container)!;
                Drawable[] drawOrder = children.ToArray();
                int haloIndex = Array.IndexOf(drawOrder, click);

                // The framework draws this sorted child list forwards. Test the loaded
                // container so insertion and lifetime ordering are exercised together.
                Assert.Multiple(() =>
                {
                    Assert.That(haloIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(Array.IndexOf(drawOrder, flick), Is.GreaterThan(haloIndex), "Flick must cover the halo");
                    Assert.That(Array.IndexOf(drawOrder, hold), Is.GreaterThan(haloIndex), "Hold head must cover the halo");
                });
                return true;
            });
        }

        private void createMixedNotes(StickSide side, double clickTimeOffset = 0, bool reverseInsertion = false)
        {
            AddStep("create mixed notes", () =>
            {
                Clear();
                var ruleset = new SticksRuleset();
                var beatmap = new Beatmap<SticksHitObject>
                {
                    BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, new BeatmapDifficulty()),
                };
                SticksHitObject[] objects =
                {
                    new SticksClick { StartTime = hit_time + clickTimeOffset, Side = side },
                    new SticksFlick { StartTime = hit_time, Side = side, Angle = 45 },
                    new SticksHold { StartTime = hit_time, Duration = 500, Side = side, Angle = 135 },
                };
                if (reverseInsertion)
                    Array.Reverse(objects);

                // Preserve authored ordering for simultaneous notes while retaining the
                // chronological beatmap order required by the gameplay lifetime manager.
                foreach (SticksHitObject hitObject in objects.OrderBy(o => o.StartTime))
                {
                    hitObject.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
                    beatmap.HitObjects.Add(hitObject);
                }

                // The drawable ruleset can replace the beatmap's approach duration while
                // loading settings. Start inside every note's visible lifetime, then
                // derive the geometry checkpoints from the loaded click's duration.
                manual = new ManualClock { CurrentTime = hit_time - 100 };
                clock = new FramedClock(manual);
                clock.ProcessFrame();
                drawable = new DrawableSticksRuleset(ruleset, beatmap) { Clock = clock };
                var input = (SticksReplayInputProvider)typeof(DrawableSticksRuleset)
                    .GetField("replayInputProvider", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(drawable)!;
                input.Update(Vector2.Zero, Vector2.Zero);
                Add(drawable);
            });
            AddUntilStep("mixed notes loaded", () => drawable.ChildrenOfType<DrawableSticksClick>().Count() == 1
                                                    && drawable.ChildrenOfType<DrawableSticksFlick>().Count() == 1
                                                    && drawable.ChildrenOfType<DrawableSticksHold>().Count() == 1
                                                    && click.IsLoaded && flick.IsLoaded && hold.IsLoaded);
            AddStep("select centre-out presentation", () => ((SticksPlayfield)drawable.Playfield).NotePresentation = SticksNotePresentation.CenterOut);
            AddWaitStep("update mixed notes", 2);
        }

        private static float markerRadius(DrawableHitObject note)
        {
            SticksArcMarker marker = note.ChildrenOfType<SticksArcMarker>().Single();
            CircularProgress arc = marker.ChildrenOfType<CircularProgress>().Single(c => c.Alpha > 0);
            return arc.DrawWidth / 2 * (1 - arc.InnerRadius / 2);
        }
    }
}
