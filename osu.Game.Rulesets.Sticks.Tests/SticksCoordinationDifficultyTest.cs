using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Sticks.Difficulty;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksCoordinationDifficultyTest
    {
        [Test]
        public void TestFixedReferenceResponseIsBoundedAndIndependentOfMapLength()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksCoordinationDifficulty.Response(0), Is.Zero);
                Assert.That(SticksCoordinationDifficulty.Response(0.5), Is.EqualTo(0.75));
                Assert.That(SticksCoordinationDifficulty.Response(1), Is.EqualTo(1));
                Assert.That(SticksCoordinationDifficulty.Normalize(50, 10), Is.EqualTo(SticksCoordinationDifficulty.Normalize(500, 100)).Within(1e-12));
                Assert.That(SticksCoordinationDifficulty.Normalize(50, 0), Is.Zero);
            });

            double[] bonuses = new[] { 0d, 0.001, 0.01, 0.1, 1, 10, 100, 1000, 1e12 }
                .Select(work => SticksCoordinationDifficulty.Response(SticksCoordinationDifficulty.Normalize(work, 1))).ToArray();
            Assert.That(bonuses, Is.Ordered.Ascending);
            Assert.That(bonuses.All(b => b >= 0 && b <= 1), Is.True);
            Assert.That(bonuses[2], Is.LessThan(0.001), "Negligible work must not get a substantial fraction of the bonus.");
        }

        [Test]
        public void TestCoordinationEntersTheSameCalibrationAsTheOtherSkills()
        {
            var result = difficulty(doubles(60, 150, 120));
            double combined = Math.Pow(new[] { result.Mechanical, result.Reading, result.Control, result.Coordination }
                                       .Sum(skill => Math.Pow(skill, 3.3)), 1 / 3.3);
            double calibrated = SticksDifficultyScaling.CalibrateStarRating(0.89 * combined * result.TimingPrecision);
            double expected = Math.Clamp(calibrated + SticksDifficultyScaling.AngularPrecisionStarAdjustment(calibrated, result.AngularPrecision), 0, 30);
            Assert.Multiple(() =>
            {
                Assert.That(result.StarRating, Is.EqualTo(expected).Within(1e-9));
                Assert.That(result.Coordination, Is.GreaterThan(0).And.LessThanOrEqualTo(8));
                Assert.That(result.CoordinationStarAddition, Is.EqualTo(result.StarRating - result.BaseStarRating).Within(1e-9));
            });
        }

        [Test]
        public void TestBreaksRemoveWorkAndTimeWithoutMovingWorkOntoRemainingWindows()
        {
            // Two unit-work heads on each hand: [0,500] and [500,1000].
            // Removing the middle half must leave exactly half the work, not four
            // whole heads squeezed onto half as much time.
            SticksCoordinationDifficulty calculate(IEnumerable<BreakPeriod> breaks)
            {
                var model = new SticksCoordinationDifficulty(1, breaks);
                var mechanical = new Dictionary<StickSide, double> { [StickSide.Left] = 1, [StickSide.Right] = 1 };
                foreach (double t in new[] { 0d, 1000 })
                {
                    model.BeginGroup();
                    model.AddGroup(new SticksHitObject[]
                    {
                        new SticksClick { StartTime = t, Side = StickSide.Left },
                        new SticksClick { StartTime = t, Side = StickSide.Right },
                    }, t, mechanical, 0, new Dictionary<StickSide, double>(), true);
                }
                return model;
            }

            var full = calculate(Array.Empty<BreakPeriod>());
            var partial = calculate(new[] { new BreakPeriod(-1000, -500), new BreakPeriod(250, 625), new BreakPeriod(500, 750), new BreakPeriod(1500, 2000) });
            var silent = calculate(new[] { new BreakPeriod(-1, 1001) });
            Assert.Multiple(() =>
            {
                Assert.That(full.CoordinatedWork, Is.EqualTo(4).Within(1e-9));
                Assert.That(partial.CoordinatedWork, Is.EqualTo(2).Within(1e-9));
                Assert.That(partial.PlayableSeconds, Is.EqualTo(0.5));
                Assert.That(partial.Participation, Is.EqualTo(full.Participation).Within(1e-9));
                Assert.That(silent.CoordinatedWork, Is.Zero);
                Assert.That(silent.Participation, Is.Zero);
            });
        }

        [Test]
        public void TestCoordinationRespondsToDensityAndAngularSpacingWithinTheSamePatternType()
        {
            double bonus(double interval, float angleStep) => difficulty(doubles(40, interval, angleStep)).Coordination;
            Assert.That(bonus(150, 90), Is.GreaterThan(bonus(300, 90)));
            Assert.That(bonus(300, 90), Is.GreaterThan(bonus(600, 90)));
            Assert.That(bonus(300, 160), Is.GreaterThan(bonus(300, 90)));
            Assert.That(bonus(300, 90), Is.GreaterThan(bonus(300, 10)));

            var alternating = doubles(40, 300, 90).Where((_, i) => i % 4 is 0 or 3).ToArray();
            Assert.That(difficulty(alternating).CoordinationStarAddition, Is.Zero.Within(1e-9));
        }

        [Test]
        public void TestPairedSliderWorkReflectsSpeedAndReversals()
        {
            double bonus(float velocity, int turns = 0) => difficulty(new[] { StickSide.Left, StickSide.Right }.Select(side => new SticksSlider
            {
                StartTime = 1000, Side = side, Angle = side == StickSide.Left ? 0 : 180,
                Duration = 4000, ArcAngle = velocity * 4 / (turns + 1), RepeatCount = turns,
            })).Coordination;

            Assert.That(bonus(0), Is.LessThan(bonus(30)));
            Assert.That(bonus(30), Is.LessThan(bonus(120)));
            Assert.That(bonus(120), Is.LessThan(bonus(240)));
            Assert.That(bonus(120, 1), Is.GreaterThan(bonus(120)));
            Assert.That(bonus(120, 3), Is.GreaterThan(bonus(120, 1)));
        }

        [Test]
        public void TestClickCoordinationDoesNotRequireAngularPrecision()
        {
            SticksHitObject[] clicks() => doubles(40, 300, 90).Select(n => (SticksHitObject)new SticksClick { StartTime = n.StartTime, Side = n.Side }).ToArray();
            var wide = difficulty(clicks(), 0);
            var narrow = difficulty(clicks(), 10);
            Assert.That(narrow.StarRating, Is.EqualTo(wide.StarRating).Within(1e-9));
            Assert.That(narrow.CoordinationStarAddition, Is.EqualTo(wide.CoordinationStarAddition).Within(1e-9));
        }

        [Test]
        public void TestTimedCoordinationUpdatesRemainLocal()
        {
            long updates(int count)
            {
                var state = new SticksDifficultyModel.IncrementalState(1, 5);
                foreach (SticksHitObject obj in doubles(count, 150, 90))
                {
                    obj.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
                    state.Append(obj);
                    _ = state.GetBreakdown();
                }
                long result = state.CoordinationIntervalUpdateCount;
                for (int i = 0; i < 100; i++)
                    _ = state.GetBreakdown();
                Assert.That(state.CoordinationIntervalUpdateCount, Is.EqualTo(result), "Reading a prefix must not replay coordination intervals.");
                return result;
            }

            long small = updates(200);
            long large = updates(400);
            Assert.That(large, Is.LessThan(small * 2.1), "Doubling the map should not revisit the whole prefix after every head.");
        }

        private static IEnumerable<SticksHitObject> doubles(int count, double interval, float angleStep)
        {
            for (int i = 0; i < count; i++)
            {
                foreach (StickSide side in new[] { StickSide.Left, StickSide.Right })
                    yield return new SticksFlick { StartTime = 1000 + i * interval, Side = side, Angle = i * angleStep % 360 };
            }
        }

        private static SticksDifficultyBreakdown difficulty(IEnumerable<SticksHitObject> source, float circleSize = 4)
        {
            SticksHitObject[] notes = source.ToArray();
            var defaults = new BeatmapDifficulty { CircleSize = circleSize, OverallDifficulty = 5 };
            foreach (SticksHitObject note in notes)
                note.ApplyDefaults(new ControlPointInfo(), defaults);
            return SticksDifficultyCalculator.CalculateDifficulty(notes, overallDifficulty: 5);
        }
    }
}
