using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksCounterpointRolesTest
    {
        [TestCase(0)]
        [TestCase(200)]
        public void TestRecurringMixedPhraseKeepsItsPulseOnOneHandAndItsFillsOnTheOther(double shift)
        {
            Beatmap<HitObject> source = phraseMap(shift);
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] result = convert(source, true);
            double start = 2000 + shift;
            double[] pulse = { start, start + 400, start + 800, start + 1200 };
            double[] fills = { start + 1100, start + 1400 };
            SticksHitObject[] pulseNotes = pulse.Select(time => result.Single(note => note.StartTime == time)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(pulseNotes.Select(note => note.Side).Distinct().Count(), Is.EqualTo(1));
                Assert.That(fills.Select(time => result.Single(note => note.StartTime == time).Side), Has.All.EqualTo(other(pulseNotes[0].Side)));
                Assert.That(pulseNotes.OfType<SticksSlider>().Count(), Is.EqualTo(3), "The steady part retains its short sliders.");
                Assert.That(pulse.Any(time => baseline.Single(note => note.StartTime == time).Side != result.Single(note => note.StartTime == time).Side), Is.True);
            });
            foreach (SticksHitObject original in baseline.Where(note => note.StartTime >= start && note.StartTime <= start + 1400))
            {
                SticksHitObject converted = result.Single(note => note.StartTime == original.StartTime);
                Assert.Multiple(() =>
                {
                    Assert.That(converted.GetType(), Is.EqualTo(original.GetType()));
                    Assert.That(converted.GetEndTime(), Is.EqualTo(original.GetEndTime()));
                    Assert.That(converted.Angle, Is.EqualTo(original.Angle));
                    if (original is SticksSlider slider)
                        Assert.That(Enumerable.Range(0, slider.SegmentCount).Select(i => ((SticksSlider)converted).SegmentArcAngleAt(i)),
                            Is.EqualTo(Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentArcAngleAt)));
                });
            }
            assertNoOverlap(result);
        }

        [Test]
        public void TestLongerPulseSlidersDoNotTrapTheNewLeadHand()
        {
            Beatmap<HitObject> source = phraseMap(0, 300);
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] result = convert(source, true);
            double[] pulse = { 2000, 2400, 2800, 3200 };
            Assert.That(pulse.Select(time => result.Single(note => note.StartTime == time).Side),
                Is.EqualTo(pulse.Select(time => baseline.Single(note => note.StartTime == time).Side)),
                "100 ms after each slider is below the current recovery requirement; retain the playable baseline assignment.");
            assertNoOverlap(result);
        }

        [TestCase(0, false)]
        [TestCase(200, false)]
        [TestCase(0, true)]
        [TestCase(200, true)]
        public void TestPickupIsPreservedBeforeTheRecurringPulse(double shift, bool earlyComboBoundary)
        {
            Beatmap<HitObject> source = phraseMap(shift);
            ((Circle)source.HitObjects.Single(note => note.StartTime == 2000 + shift)).NewCombo = false;
            source.HitObjects.Add(new Circle { StartTime = 1900 + shift, Position = position(85), NewCombo = true });
            if (earlyComboBoundary)
            {
                source.HitObjects.Remove(source.HitObjects.Single(note => note.StartTime == 3400 + shift));
                source.HitObjects.Single(note => note.StartTime == 3600 + shift).StartTime = 3450 + shift;
            }
            source.HitObjects = source.HitObjects.OrderBy(note => note.StartTime).ToList();
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] result = convert(source, true);
            double[] pulse = { 2000 + shift, 2400 + shift, 2800 + shift, 3200 + shift };
            StickSide lead = result.Single(note => note.StartTime == pulse[0]).Side;
            double[] fills = earlyComboBoundary ? new[] { 1900 + shift, 3100 + shift } : new[] { 1900 + shift, 3100 + shift, 3400 + shift };

            Assert.Multiple(() =>
            {
                Assert.That(pulse.Select(time => result.Single(note => note.StartTime == time).Side), Has.All.EqualTo(lead));
                Assert.That(fills.Select(time => result.Single(note => note.StartTime == time).Side), Has.All.EqualTo(other(lead)),
                    "The pickup is part of the fill voice; it must not become the pulse's phase anchor.");
                Assert.That(pulse.Any(time => baseline.Single(note => note.StartTime == time).Side != result.Single(note => note.StartTime == time).Side), Is.True);
            });
            foreach (SticksHitObject original in baseline.Where(note => note.StartTime >= 1900 + shift && note.StartTime <= Math.Max(pulse[^1], fills[^1])))
            {
                SticksHitObject converted = result.Single(note => note.StartTime == original.StartTime);
                Assert.Multiple(() =>
                {
                    Assert.That(converted.GetType(), Is.EqualTo(original.GetType()));
                    Assert.That(converted.GetEndTime(), Is.EqualTo(original.GetEndTime()));
                    Assert.That(converted.Angle, Is.EqualTo(original.Angle));
                });
            }
            assertNoOverlap(result);
        }

        [Test]
        public void TestUniformRhythmDoesNotBecomeAnArbitraryPulseFillDivision()
        {
            Beatmap<HitObject> source = phraseMap(0);
            source.HitObjects.Clear();
            for (int i = 0; i < 16; i++)
                source.HitObjects.Add(new Circle { StartTime = 1000 + i * 200, Position = position(i % 2 == 0 ? 20 : 200), NewCombo = i % 8 == 0 });
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] result = convert(source, true);
            Assert.That(result.Select(note => (note.StartTime, note.GetType(), note.Side, note.Angle)),
                Is.EqualTo(baseline.Select(note => (note.StartTime, note.GetType(), note.Side, note.Angle))));
        }

        private static Beatmap<HitObject> phraseMap(double shift, double duration = 200)
        {
            var source = new Beatmap<HitObject>();
            source.Difficulty.OverallDifficulty = 8;
            source.Difficulty.CircleSize = 4;
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 400 });
            source.HitObjects.Add(new Circle { StartTime = 1600 + shift, Position = position(40) });
            source.HitObjects.Add(new Circle { StartTime = 1800 + shift, Position = position(70) });
            for (int phrase = 0; phrase < 3; phrase++)
            {
                double start = 2000 + shift + phrase * 1600;
                for (int i = 0; i < 3; i++)
                    source.HitObjects.Add(new Slider { StartTime = start + i * 400, Duration = duration, Position = position(100 + i * 70), NewCombo = i == 0 });
                source.HitObjects.Add(new Circle { StartTime = start + 1100, Position = position(210) });
                source.HitObjects.Add(new Circle { StartTime = start + 1200, Position = position(210) });
                source.HitObjects.Add(new Circle { StartTime = start + 1400, Position = position(250) });
            }
            return source;
        }

        private static void assertNoOverlap(IEnumerable<SticksHitObject> notes)
        {
            foreach (var lane in notes.GroupBy(note => note.Side))
            {
                double end = double.NegativeInfinity;
                foreach (SticksHitObject note in lane.OrderBy(note => note.StartTime))
                {
                    Assert.That(note.StartTime, Is.GreaterThanOrEqualTo(end - 0.001));
                    end = Math.Max(end, note.GetEndTime());
                }
            }
        }

        private static SticksHitObject[] convert(Beatmap<HitObject> source, bool experimental) =>
            new SticksBeatmapConverter(source, new SticksRuleset()) { UseCounterpoint = experimental }.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
        private static StickSide other(StickSide side) => side == StickSide.Left ? StickSide.Right : StickSide.Left;
        private static Vector2 position(float angle) => SticksBeatmapConverter.STANDARD_CENTRE + new Vector2(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180)) * 160;

        private class Circle : HitObject, IHasPosition, IHasCombo
        {
            public Vector2 Position { get; set; }
            public float X { get => Position.X; set => Position = new Vector2(value, Y); }
            public float Y { get => Position.Y; set => Position = new Vector2(X, value); }
            public bool NewCombo { get; set; }
            public int ComboOffset { get; set; }
        }

        private sealed class Slider : Circle, IHasDuration
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
        }
    }
}
