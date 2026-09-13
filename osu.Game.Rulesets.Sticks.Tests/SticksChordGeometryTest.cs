using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksChordGeometryTest
    {
        [TestCase(20, 20, 10, true)]
        [TestCase(20, 20, 10.01f, false)]
        [TestCase(20, 20, 19, false)]
        [TestCase(20, 20, 20, false)]
        [TestCase(20, 40, 20, true)]
        [TestCase(20, 40, 20.01f, false)]
        [TestCase(40, 20, 20, true)]
        public void TestThresholdIsHalfOfTheNarrowerVisibleArc(float firstSpan, float secondSpan, float separation, bool expected)
        {
            Assert.That(SticksChordGeometry.ShouldStack(0, firstSpan, separation, secondSpan), Is.EqualTo(expected));
            Assert.That(SticksChordGeometry.ShouldStack(separation, secondSpan, 0, firstSpan), Is.EqualTo(expected));
        }

        [TestCase(0)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(9)]
        [TestCase(10)]
        public void TestReadabilityUsesCircleSizeRatherThanTheUninitialisedTwentyDegreeProperty(float cs)
        {
            float width = SticksHitObject.HitAngleForCircleSize(cs);
            SticksHitObject[] atBoundary = { flick(0, StickSide.Left, 350), flick(0, StickSide.Right, 350 + width / 2) };
            SticksHitObject[] belowOverlap = { flick(0, StickSide.Left, 350), flick(0, StickSide.Right, 350 + width / 2 + 0.01f) };
            float[] original = belowOverlap.Select(note => note.Angle).ToArray();

            SticksChordGeometry.ResolveReadableChords(atBoundary, width);
            SticksChordGeometry.ResolveReadableChords(belowOverlap, width);

            Assert.Multiple(() =>
            {
                Assert.That(atBoundary[0].Angle, Is.EqualTo(atBoundary[1].Angle));
                Assert.That(belowOverlap.Select(note => note.Angle), Is.EqualTo(original));
            });
        }

        [Test]
        public void TestWraparoundResolvesToOnePurpleTarget()
        {
            SticksHitObject[] heads = { flick(1000, StickSide.Left, 357), flick(1000, StickSide.Right, 3) };
            SticksChordGeometry.ResolveReadableChords(heads, 20);

            Assert.That(heads.Select(note => note.Angle), Is.All.EqualTo(0).Within(0.001));
        }

        [TestCase(0)]
        [TestCase(37)]
        [TestCase(179)]
        public void TestAntipodalResolutionIsOrderIndependentAndRotatesWithTheSource(float rotation)
        {
            float first = 20 + rotation;
            float second = 200 + rotation;
            float source = 20 + rotation;
            float forward = SticksChordGeometry.SharedAngle(first, second, source);
            float backward = SticksChordGeometry.SharedAngle(second, first, source);

            Assert.Multiple(() =>
            {
                Assert.That(forward, Is.EqualTo(backward).Within(0.001));
                Assert.That(forward, Is.EqualTo(SticksHitObject.NormaliseAngle(110 + rotation)).Within(0.001));
            });
        }

        [Test]
        public void TestReadabilityDoesNotMergeDifferentTimesSameHandsOrClicks()
        {
            SticksHitObject[] heads =
            {
                flick(0, StickSide.Left, 0), flick(1, StickSide.Right, 2),
                flick(1000, StickSide.Left, 45), flick(1000, StickSide.Left, 47),
                flick(2000, StickSide.Left, 90), new SticksClick { StartTime = 2000, Side = StickSide.Right, Angle = 92 },
            };
            float[] original = heads.Select(note => note.Angle).ToArray();

            SticksChordGeometry.ResolveReadableChords(heads, 45);

            Assert.That(heads.Select(note => note.Angle), Is.EqualTo(original));
        }

        [Test]
        public void TestExactStacksSurviveParityAndObjectOrdering()
        {
            SticksHitObject[] forward = historyFixture();
            SticksHitObject[] backward = historyFixture();
            Array.Reverse(backward);

            SticksParityConversion.Apply(forward, map(), default);
            SticksParityConversion.Apply(backward, map(), default);

            Assert.That(forward[2].Angle, Is.EqualTo(forward[3].Angle));
            foreach (SticksHitObject note in forward)
                Assert.That(backward.Single(other => other.StartTime == note.StartTime && other.Side == note.Side).Angle,
                    Is.EqualTo(note.Angle).Within(0.001));
        }

        [Test]
        public void TestFollowingTurnsUseTheResolvedStackAndKeepOriginalSourceHistory()
        {
            SticksHitObject[] coupled = historyFixture();
            var left = historyFixture().Where(note => note.Side == StickSide.Left).ToArray();
            var right = historyFixture().Where(note => note.Side == StickSide.Right).ToArray();
            SticksParityConversion.Apply(left, map(), default);
            SticksParityConversion.Apply(right, map(), default);
            SticksParityConversion.Apply(coupled, map(), default);

            float resolved = coupled[2].Angle;
            Assert.That(Math.Abs(SticksHitObject.DeltaAngle(0, resolved)), Is.GreaterThan(1), "The resolved output must differ from its source heading.");
            foreach (SticksHitObject[] solo in new[] { left, right })
            {
                float correction = SticksHitObject.DeltaAngle(solo[1].Angle, resolved);
                SticksHitObject next = coupled.Single(note => note.StartTime == solo[2].StartTime && note.Side == solo[2].Side);
                Assert.That(next.Angle, Is.EqualTo(SticksHitObject.NormaliseAngle(solo[2].Angle + correction)).Within(0.001),
                    "The next note must start from the target the player actually saw, while measuring source motion from original angles.");
            }
        }

        [Test]
        public void TestSliderEndpointHistoryUsesTheResolvedHeadAndUnchangedTimedPath()
        {
            SticksHitObject[] coupled = sliderHistoryFixture();
            SticksHitObject[] solo = sliderHistoryFixture().Where(note => note.Side == StickSide.Left).ToArray();
            SticksParityConversion.Apply(solo, map(), default);
            SticksParityConversion.Apply(coupled, map(), default);
            SticksSlider slider = coupled.OfType<SticksSlider>().Single();
            SticksSlider soloSlider = solo.OfType<SticksSlider>().Single();
            float correction = SticksHitObject.DeltaAngle(soloSlider.AngleAt(soloSlider.EndTime), slider.AngleAt(slider.EndTime));

            Assert.Multiple(() =>
            {
                Assert.That(slider.Angle, Is.EqualTo(coupled.Single(note => note.StartTime == 500 && note.Side == StickSide.Right).Angle));
                Assert.That(slider.SegmentArcAngles, Is.EqualTo(new[] { 15f, -5 }));
                Assert.That(slider.SegmentDurationAt(0), Is.EqualTo(200));
                Assert.That(slider.SegmentDurationAt(1), Is.EqualTo(200));
                Assert.That(coupled.Single(note => note.StartTime == 1200).Angle,
                    Is.EqualTo(SticksHitObject.NormaliseAngle(solo.Last().Angle + correction)).Within(0.001));
            });
        }

        [Test]
        public void TestSeparatedPairsKeepTheOriginalIndependentParityBehaviour()
        {
            SticksHitObject[] pair = { flick(0, StickSide.Left, 0), flick(0, StickSide.Right, 120), flick(500, StickSide.Left, 40), flick(500, StickSide.Right, 230) };
            SticksHitObject[] left = { flick(0, StickSide.Left, 0), flick(500, StickSide.Left, 40) };
            SticksHitObject[] right = { flick(0, StickSide.Right, 120), flick(500, StickSide.Right, 230) };
            SticksParityConversion.Apply(pair, map(), default);
            SticksParityConversion.Apply(left, map(), default);
            SticksParityConversion.Apply(right, map(), default);

            foreach (SticksHitObject note in left.Concat(right))
                Assert.That(pair.Single(candidate => candidate.Side == note.Side && candidate.StartTime == note.StartTime).Angle, Is.EqualTo(note.Angle));
        }

        [Test]
        public void TestStrongOverlapCleanupIsOptInButExactStackPreservationIsGeneral()
        {
            SticksHitObject[] ordinary = { flick(0, StickSide.Left, 350), flick(0, StickSide.Right, 2) };
            SticksHitObject[] experiment = { flick(0, StickSide.Left, 350), flick(0, StickSide.Right, 2) };
            SticksParityConversion.Apply(ordinary, map(), default);
            SticksParityConversion.Apply(experiment, map(), default, readableChords: true, fullHitAngle: 27.5f);

            Assert.Multiple(() =>
            {
                Assert.That(ordinary[0].Angle, Is.EqualTo(350));
                Assert.That(ordinary[1].Angle, Is.EqualTo(2));
                Assert.That(experiment[0].Angle, Is.EqualTo(experiment[1].Angle));
                Assert.That(experiment[0].Angle, Is.EqualTo(356).Within(0.001));
            });
        }

        [Test]
        public void TestParityCreatedOverlapResolvesBeforeFollowingHistory()
        {
            SticksHitObject[] ordinary =
            {
                flick(0, StickSide.Left, 0), flick(0, StickSide.Right, 210),
                flick(500, StickSide.Left, 0), flick(500, StickSide.Right, 210),
                flick(1000, StickSide.Left, 90), flick(1000, StickSide.Right, 120),
            };
            SticksHitObject[] experiment = ordinary.Select(note => flick(note.StartTime, note.Side, note.Angle)).ToArray();
            SticksParityConversion.Apply(ordinary, map(), default);
            SticksParityConversion.Apply(experiment, map(), default, readableChords: true, fullHitAngle: 20);

            Assert.Multiple(() =>
            {
                Assert.That(ordinary[2].Angle, Is.Not.EqualTo(ordinary[3].Angle));
                Assert.That(SticksChordGeometry.ShouldStack(ordinary[2].Angle, 20, ordinary[3].Angle, 20), Is.True);
                Assert.That(experiment[2].Angle, Is.EqualTo(experiment[3].Angle));
                Assert.That(experiment[2].Angle, Is.EqualTo(105).Within(0.001));
                for (int i = 4; i < experiment.Length; i++)
                {
                    float correction = SticksHitObject.DeltaAngle(ordinary[i - 2].Angle, experiment[i - 2].Angle);
                    Assert.That(experiment[i].Angle, Is.EqualTo(SticksHitObject.NormaliseAngle(ordinary[i].Angle + correction)).Within(0.001));
                }
            });
        }

        [TestCase("none", 4)]
        [TestCase("none", 9)]
        [TestCase("easy", 4)]
        [TestCase("hardrock", 4)]
        [TestCase("difficulty-adjust-unset", 9)]
        [TestCase("difficulty-adjust-angle", 9)]
        public void TestConverterResolvesTheActualPostModVisualWidthWithoutChangingSource(string mode, float cs)
        {
            Beatmap<HitObject> source = map();
            source.Difficulty.CircleSize = cs;
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { UseCounterpoint = true };
            Mod mod = mode switch
            {
                "easy" => new SticksModEasy(),
                "hardrock" => new SticksModHardRock(),
                "difficulty-adjust-unset" => new SticksModDifficultyAdjust(),
                "difficulty-adjust-angle" => new SticksModDifficultyAdjust { PrimaryHitAngle = { Value = 42 } },
                _ => null,
            };
            BeatmapDifficulty actual = source.Difficulty.Clone();
            if (mod is IApplicableToBeatmapConverter converterMod)
                converterMod.ApplyToBeatmapConverter(converter);
            if (mod is IApplicableToDifficulty difficultyMod)
                difficultyMod.ApplyToDifficulty(actual);
            var finalHead = flick(0, StickSide.Left, 0);
            finalHead.ApplyDefaults(source.ControlPointInfo, actual);
            if (mod is IApplicableToHitObject hitObjectMod)
                hitObjectMod.ApplyToHitObject(finalHead);

            Assert.Multiple(() =>
            {
                Assert.That(converter.CounterpointHitAngleFor(source.Difficulty), Is.EqualTo(finalHead.PrimaryHitAngle).Within(0.001));
                Assert.That(source.Difficulty.CircleSize, Is.EqualTo(cs));
            });
        }

        private static SticksHitObject[] historyFixture() => new[]
        {
            flick(0, StickSide.Left, 0), flick(0, StickSide.Right, 90),
            flick(500, StickSide.Left, 0), flick(500, StickSide.Right, 0),
            flick(1000, StickSide.Left, 90), flick(1000, StickSide.Right, 270),
        };

        private static SticksHitObject[] sliderHistoryFixture()
        {
            var slider = new SticksSlider { StartTime = 500, Side = StickSide.Left, Angle = 0, Duration = 400 };
            slider.SetTimedSegments(new[] { 15f, -5 }, new[] { 200d, 200 });
            return new SticksHitObject[]
            {
                flick(0, StickSide.Left, 0), flick(0, StickSide.Right, 90), slider,
                flick(500, StickSide.Right, 0), flick(1200, StickSide.Left, 100),
            };
        }

        private static SticksFlick flick(double time, StickSide side, float angle) => new SticksFlick { StartTime = time, Side = side, Angle = angle };

        private static Beatmap<HitObject> map()
        {
            var result = new Beatmap<HitObject>();
            result.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            return result;
        }
    }
}
