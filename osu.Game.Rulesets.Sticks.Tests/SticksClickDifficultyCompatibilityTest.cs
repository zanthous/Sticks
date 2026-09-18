using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Scoring;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksClickDifficultyCompatibilityTest
    {
        [TestCase(0, 79.5, 139.5, 199.5)]
        [TestCase(5, 49.5, 99.5, 149.5)]
        [TestCase(10, 19.5, 59.5, 99.5)]
        public void GreatLookupPreservesClickTimingAndGrades(double difficulty, double great, double ok, double meh)
        {
            var windows = new SticksHitWindows();
            windows.SetDifficulty(difficulty);
            var scoreProcessor = new SticksScoreProcessor(new SticksRuleset());

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(windows.IsHitResultAllowed(HitResult.Great), Is.True);
                Assert.That(windows.GetAllAvailableWindows().Select(window => window.result),
                    Is.EquivalentTo(new[] { HitResult.Great, HitResult.Ok, HitResult.Meh, HitResult.Miss }));
                Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(great));
                Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(ok));
                Assert.That(windows.WindowFor(HitResult.Meh), Is.EqualTo(meh));
                Assert.That(windows.WindowFor(HitResult.Miss), Is.EqualTo(400));
            });

            foreach (int sign in new[] { -1, 1 })
            {
                Assert.That(windows.ResultFor(sign * great), Is.EqualTo(HitResult.Great));
                Assert.That(windows.ResultFor(sign * (great + 0.001)), Is.EqualTo(HitResult.Ok));
                Assert.That(windows.ResultFor(sign * ok), Is.EqualTo(HitResult.Ok));
                Assert.That(windows.ResultFor(sign * (ok + 0.001)), Is.EqualTo(HitResult.Meh));
                Assert.That(windows.ResultFor(sign * meh), Is.EqualTo(HitResult.Meh));
                Assert.That(windows.ResultFor(sign * (meh + 0.001)), Is.EqualTo(HitResult.Miss));
            }

            Assert.That(scoreProcessor.GetBaseScoreForResult(HitResult.Great), Is.EqualTo(150));
            Assert.That(scoreProcessor.GetBaseScoreForResult(HitResult.Ok), Is.EqualTo(50));
            Assert.That(scoreProcessor.GetBaseScoreForResult(HitResult.Meh), Is.EqualTo(25));
        }

        [TestCase(1)]
        [TestCase(1.5)]
        public void UpstreamDifficultyObjectReadsClickGreatWindow(double clockRate)
        {
            var click = new SticksClick { StartTime = 1000 };
            click.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty { OverallDifficulty = 5 });

            // The upstream preprocessor reads the same Great window used in gameplay.
            var difficultyObject = new DifficultyHitObject(click, click, clockRate, new List<DifficultyHitObject>(), 0);

            Assert.That(difficultyObject.HitWindowGreat,
                Is.EqualTo(2 * click.HitWindows.WindowFor(HitResult.Great) / clockRate));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void NativeMixedClickMapCalculatesThroughUpstreamDifficultyObjects(bool timed, bool doubleTime)
        {
            var beatmap = mixedMap();
            var calculator = new SticksDifficultyCalculator(new SticksRuleset().RulesetInfo,
                new PassthroughWorkingBeatmap(beatmap));
            Mod[] mods = doubleTime ? new Mod[] { new SticksModDoubleTime() } : Array.Empty<Mod>();
            double clockRate = doubleTime ? 1.5 : 1;
            SticksDifficultyAttributes full;

            if (timed)
            {
                var attributes = calculator.CalculateTimed(mods);
                Assert.That(attributes, Has.Count.EqualTo(beatmap.HitObjects.Count));

                for (int i = 0; i < attributes.Count; i++)
                {
                    var expected = SticksDifficultyCalculator.CalculateDifficultyIndependent(
                        beatmap.HitObjects.Take(i + 1), clockRate, beatmap.Difficulty.OverallDifficulty);
                    Assert.That(attributes[i].Attributes.StarRating, Is.EqualTo(expected.StarRating).Within(0.0000001),
                        $"Prefix {i + 1} must retain native click difficulty.");
                }

                full = (SticksDifficultyAttributes)attributes[^1].Attributes;
            }
            else
            {
                full = (SticksDifficultyAttributes)calculator.Calculate(mods);
                var expected = SticksDifficultyCalculator.CalculateDifficultyIndependent(
                    beatmap.HitObjects, clockRate, beatmap.Difficulty.OverallDifficulty);
                Assert.That(full.StarRating, Is.EqualTo(expected.StarRating).Within(0.0000001));
            }

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(double.IsFinite(full.StarRating), Is.True);
                Assert.That(full.StarRating, Is.GreaterThan(0));
                Assert.That(full.ClockRate, Is.EqualTo(clockRate));
                Assert.That(full.AccuracyObjectCount, Is.EqualTo(5));
                Assert.That(full.MaxCombo, Is.EqualTo(5));
            });
        }

        private static Beatmap<SticksHitObject> mixedMap()
        {
            var difficulty = new BeatmapDifficulty { OverallDifficulty = 5, CircleSize = 4, SliderTickRate = 1 };
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(new SticksRuleset().RulesetInfo, difficulty),
            };
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.HitObjects.AddRange(new SticksHitObject[]
            {
                new SticksClick { StartTime = 1000 },
                new SticksFlick { StartTime = 1250, Side = StickSide.Right, Angle = 90 },
                new SticksFlick { StartTime = 1500, Angle = 180 },
                new SticksClick { StartTime = 1750, Side = StickSide.Right },
                new SticksClick { StartTime = 2750 },
            });
            foreach (SticksHitObject hitObject in beatmap.HitObjects)
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);
            return beatmap;
        }

        private sealed class PassthroughWorkingBeatmap : FlatWorkingBeatmap
        {
            private readonly IBeatmap beatmap;

            public PassthroughWorkingBeatmap(IBeatmap beatmap)
                : base(beatmap)
            {
                this.beatmap = beatmap;
            }

            public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return beatmap;
            }
        }
    }
}
