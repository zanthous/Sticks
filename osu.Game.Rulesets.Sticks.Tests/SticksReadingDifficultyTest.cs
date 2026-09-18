using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Sticks.Difficulty;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksReadingDifficultyTest
    {
        [Test]
        public void TestChangingStepSizeAddsReadingAtMatchedPaceAndMeanMovement()
        {
            var regular = pattern(new[] { 90f });
            var varied = pattern(new[] { 30f, 150f, 120f, 60f });

            Assert.That(reading(varied), Is.GreaterThan(reading(regular)));
        }

        [Test]
        public void TestSequenceReadingRespectsSymmetryAndPlaybackRate()
        {
            var notes = pattern(new[] { 30f, 150f, 120f, 60f });
            double expected = reading(notes);
            var rotated = notes.Select(n => note(n.StartTime, n.Angle + 17, n.Side)).ToArray();
            var reflected = notes.Select(n => note(n.StartTime, -n.Angle, n.Side)).ToArray();
            var swapped = notes.Select(n => note(n.StartTime, n.Angle, n.Side == StickSide.Left ? StickSide.Right : StickSide.Left)).ToArray();
            var shifted = notes.Select(n => note(n.StartTime + 13000, n.Angle, n.Side)).ToArray();

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(reading(rotated), Is.EqualTo(expected).Within(1e-8));
                Assert.That(reading(reflected), Is.EqualTo(expected).Within(1e-8));
                Assert.That(reading(swapped), Is.EqualTo(expected).Within(1e-8));
                Assert.That(reading(shifted), Is.EqualTo(expected).Within(1e-8));
                Assert.That(reading(notes, 0.75), Is.LessThan(expected));
                Assert.That(reading(notes, 1.25), Is.GreaterThan(expected));
            });
        }

        [Test]
        public void TestTripleCarriesDemandIntoFollowingJump()
        {
            float[] angles = { 0, 180, 30, 270, 60, 90, 240 };
            double[] tightTimes = { 0, 130, 260, 325, 390, 520, 650 };
            double[] evenTimes = { 0, 130, 260, 390, 520, 650, 780 };
            var tight = angles.Select((a, i) => note(tightTimes[i], a, i % 2 == 0 ? StickSide.Left : StickSide.Right));
            var even = angles.Select((a, i) => note(evenTimes[i], a, i % 2 == 0 ? StickSide.Left : StickSide.Right));

            // Identical angles and final 130 ms gap; only the preceding burst differs.
            Assert.That(endStrain(tight), Is.GreaterThan(endStrain(even)));
        }

        [Test]
        public void TestBreakClearsSequenceAndStrain()
        {
            var before = pattern(new[] { 30f, 150f, 120f, 60f });
            double time = before[^1].StartTime + 1000;
            var after = new[] { note(time, 13, StickSide.Left), note(time + 125, 97, StickSide.Right), note(time + 250, 160, StickSide.Left) };
            var breaks = new[] { new BreakPeriod(before[^1].StartTime + 100, time) };

            Assert.That(endStrain(before.Concat(after), breaks), Is.EqualTo(endStrain(after)).Within(1e-8));
        }

        [Test]
        public void TestChordPrefixesAndMemberOrder()
        {
            var chords = Enumerable.Range(0, 80).SelectMany(i => new[]
            {
                note(i * 200, i * 37, StickSide.Left),
                note(i * 200, i * 37, StickSide.Right),
            }).ToArray();
            var state = new SticksDifficultyModel.IncrementalState(1, 6);

            for (int i = 0; i < chords.Length; i++)
            {
                state.Append(chords[i]);
                var expected = SticksDifficultyModel.CalculateOrdered(chords.Take(i + 1).ToArray(), 1, 6);
                Assert.That(state.GetBreakdown().Reading, Is.EqualTo(expected.Reading).Within(1e-8), $"Prefix {i + 1}");
            }

            Assert.That(reading(chords.Chunk(2).SelectMany(pair => pair.Reverse()).ToArray()), Is.EqualTo(reading(chords)).Within(1e-8));
        }

        [Test]
        public void TestClicksDoNotReplaceDirectionalHistory()
        {
            SticksHitObject[] sequence(float clickAngle) => new SticksHitObject[]
            {
                note(0, 0, StickSide.Left),
                new SticksClick { StartTime = 125, Angle = clickAngle, Side = StickSide.Left },
                note(250, 120, StickSide.Left),
            };

            Assert.That(endStrain(sequence(120)), Is.EqualTo(endStrain(sequence(0))).Within(1e-8));
        }

        [Test]
        public void TestTargetAcquisitionStartsAtSliderEndpoint()
        {
            SticksHitObject[] sequence(float nextAngle) => new SticksHitObject[]
            {
                new SticksSlider { StartTime = 0, Duration = 500, Angle = 0, ArcAngle = 90, Side = StickSide.Left },
                note(750, nextAngle, StickSide.Left),
            };

            Assert.That(endStrain(sequence(90)), Is.LessThan(endStrain(sequence(0))));
        }

        private static SticksHitObject[] pattern(float[] steps)
        {
            float[] angles = { 0, 180 };
            return Enumerable.Range(0, 258).Select(i =>
            {
                int side = i % 2;
                if (i >= 2)
                    angles[side] += steps[(i / 2 - 1) % steps.Length];
                return note(i * 125, angles[side], side == 0 ? StickSide.Left : StickSide.Right);
            }).ToArray();
        }

        private static SticksHitObject note(double time, float angle, StickSide side) => new SticksFlick
        {
            StartTime = time,
            Angle = angle,
            Side = side,
            PrimaryHitAngle = 27.5f,
            SecondaryHitAngle = 13.75f,
        };

        private static double reading(SticksHitObject[] notes, double clockRate = 1) => SticksDifficultyModel.CalculateOrdered(notes, clockRate, 6).Reading;

        private static double endStrain(IEnumerable<SticksHitObject> notes, IEnumerable<BreakPeriod> breaks = null)
        {
            var state = new SticksReadingDifficulty(1, breaks);
            double strain = 0;
            foreach (SticksHitObject n in notes)
                strain = state.Process(new[] { n }, n.StartTime, 0, 0, 0, false);
            return strain;
        }
    }
}
