using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    public partial class SticksApproachRateTest : OsuTestScene
    {
        private const double hit_time = 3000;

        private DrawableSticksRuleset drawable = null!;
        private SticksRulesetConfigManager config = null!;
        private ManualClock manual = null!;
        private FramedClock clock = null!;

        [TestCase("none", 1)]
        [TestCase("DA", 0.5)]
        [TestCase("DA", 1)]
        [TestCase("DA", 1.25)]
        [TestCase("DA", 2)]
        [TestCase("DT", 1.5)]
        [TestCase("HT", 0.75)]
        public void TestSpeedPreservesPlayerApproachTime(string modType, double speed)
        {
            AddStep("load mixed notes at player AR 8", () =>
            {
                Clear();
                var ruleset = new SticksRuleset();
                config = (SticksRulesetConfigManager)RulesetConfigs.GetConfigFor(ruleset)!;
                config.SetValue(SticksRulesetSetting.ApproachRate, 8f);
                var beatmap = new Beatmap<SticksHitObject>
                {
                    BeatmapInfo = new BeatmapInfo(ruleset.RulesetInfo, new BeatmapDifficulty { ApproachRate = 2 }),
                };
                beatmap.HitObjects.AddRange(new SticksHitObject[]
                {
                    new SticksFlick { StartTime = hit_time, Angle = 45 },
                    new SticksClick { StartTime = hit_time },
                    new SticksHold { StartTime = hit_time, Duration = 1000, Angle = 135 },
                    new SticksSlider { StartTime = hit_time, Duration = 1000, Angle = 225, ArcAngle = 90 },
                    new SticksSlice { StartTime = hit_time, Angle = 315 },
                });
                foreach (SticksHitObject note in beatmap.HitObjects)
                    note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

                Mod[] mods = modType switch
                {
                    "DA" => new Mod[]
                    {
                        new SticksModDifficultyAdjust
                        {
                            SpeedChange = { Value = speed },
                            OverallDifficulty = { Value = 10 },
                            PrimaryHitAngle = { Value = 42 },
                        },
                    },
                    "DT" => new Mod[] { new SticksModDoubleTime { SpeedChange = { Value = speed } } },
                    "HT" => new Mod[] { new SticksModHalfTime { SpeedChange = { Value = speed } } },
                    _ => Array.Empty<Mod>(),
                };
                foreach (Mod mod in mods)
                {
                    if (mod is IApplicableToDifficulty difficultyMod)
                        difficultyMod.ApplyToDifficulty(beatmap.Difficulty);
                }

                manual = new ManualClock { CurrentTime = hit_time - 100, Rate = speed };
                clock = new FramedClock(manual);
                clock.ProcessFrame();
                drawable = new DrawableSticksRuleset(ruleset, beatmap, mods) { Clock = clock };
                Assert.That((hit_time - drawable.GameplayStartTime) / speed, Is.GreaterThanOrEqualTo(1800),
                    "The host must allow a full AR 0 approach before player settings load.");
                Add(drawable);
            });
            AddUntilStep("notes loaded", () => drawable.IsLoaded && drawable.Playfield.AllHitObjects.Count() == 5
                && drawable.Playfield.AllHitObjects.All(note => note.IsLoaded));
            AddStep("approach lasts 750 real milliseconds", () => assertApproach(8, 750, speed));
            AddStep("seek to halfway through real approach", () =>
            {
                manual.CurrentTime = hit_time - 375 * speed;
                clock.ProcessFrame();
            });
            AddWaitStep("update approach geometry", 2);
            AddStep("halo is halfway to the guide", () =>
            {
                var halo = drawable.ChildrenOfType<SticksClickHalo>().Single();
                double radius = (halo.DrawWidth - SticksClickHalo.STROKE_THICKNESS) / 2;
                Assert.That(radius, Is.EqualTo(SticksPlayfield.GUIDE_RADIUS / 2).Within(0.001));
            });
            foreach ((float ar, double duration) in new[] { (0f, 1800d), (12f, 150d), (5f, 1200d) })
            {
                AddStep($"change player AR to {ar}", () => config.SetValue(SticksRulesetSetting.ApproachRate, ar));
                AddWaitStep("update approach transforms", 2);
                AddStep($"approach lasts {duration} real milliseconds", () => assertApproach(ar, duration, speed));
            }
            AddStep("reapply map defaults and rebuild nested notes", () =>
            {
                foreach (SticksHitObject note in drawable.Beatmap.HitObjects)
                    note.ApplyDefaults(drawable.Beatmap.ControlPointInfo, drawable.Beatmap.Difficulty);
            });
            AddWaitStep("reload nested notes", 2);
            AddStep("reapplied notes retain player approach", () => assertApproach(5, 1200, speed));
        }

        private void assertApproach(float approachRate, double realDuration, double speed)
        {
            Assert.That(config.Get<float>(SticksRulesetSetting.ApproachRate), Is.EqualTo(approachRate));
            Assert.That(drawable.Beatmap.Difficulty.ApproachRate, Is.EqualTo(2), "Display preferences must not change map difficulty.");
            Assert.That(drawable.PlayerApproachDuration / speed, Is.EqualTo(realDuration).Within(0.001));
            foreach (SticksHitObject note in drawable.Beatmap.HitObjects)
                assertNestedApproach(note, realDuration, speed);
            foreach (var note in drawable.Playfield.AllHitObjects)
                Assert.That((note.HitObject.StartTime - note.LifetimeStart) / speed, Is.EqualTo(realDuration).Within(0.001), note.GetType().Name);
        }

        private static void assertNestedApproach(SticksHitObject note, double realDuration, double speed)
        {
            Assert.That(note.ApproachDuration / speed, Is.EqualTo(realDuration).Within(0.001), note.GetType().Name);
            foreach (SticksHitObject nested in note.NestedHitObjects.OfType<SticksHitObject>())
                assertNestedApproach(nested, realDuration, speed);
        }
    }
}
