using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
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
    public class SticksDuetAccompanimentTest
    {
        [Test]
        public void TestShortNativeSliderAddsHeadChordWithoutInventingInteriorPulse()
        {
            Beatmap<HitObject> source = map(slider(1000, 500));
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] duet = convert(source, true);
            SticksFlick accent = duet.OfType<SticksFlick>().Single();
            SticksSlider primary = duet.OfType<SticksSlider>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(duet, Has.Length.EqualTo(baseline.Length + 1));
                Assert.That(accent.StartTime, Is.EqualTo(1000));
                Assert.That(accent.Side, Is.Not.EqualTo(primary.Side));
                Assert.That(primary.SyncedNoteSide, Is.EqualTo(accent.Side));
                Assert.That(signature(primary), Is.EqualTo(signature(baseline.Single())));
            });
            assertAddedClearance(baseline, duet);
        }

        [Test]
        public void TestNearbyHalfBeatRhythmSupportsIndependentInteriorAccent()
        {
            Beatmap<HitObject> source = map(circle(0, 90), circle(250, 135), slider(1000, 500), circle(2000, 210), circle(2250, 270));
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] duet = convert(source, true);
            SticksFlick accent = added(baseline, duet).OfType<SticksFlick>().Single();
            SticksSlider primary = duet.OfType<SticksSlider>().Single();
            SticksHitObject before = baseline.Where(note => note.Side == accent.Side && note.StartTime < accent.StartTime).Last();
            SticksHitObject after = baseline.First(note => note.Side == accent.Side && note.StartTime > accent.StartTime);
            float progress = (float)((accent.StartTime - before.StartTime) / (after.StartTime - before.StartTime));
            float expected = SticksHitObject.NormaliseAngle(before.Angle + progress * SticksHitObject.DeltaAngle(before.Angle, after.Angle));

            Assert.Multiple(() =>
            {
                Assert.That(accent.StartTime, Is.EqualTo(1250));
                Assert.That(accent.Side, Is.Not.EqualTo(primary.Side));
                Assert.That(accent.Angle, Is.EqualTo(expected).Within(0.001));
                Assert.That(baseline.Select(signature), Is.SubsetOf(duet.Select(signature)));
            });
            assertAddedClearance(baseline, duet);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestRepeatAccentPreservesNodeSamplesAndHitsoundSetting(bool disableHitsounds)
        {
            NativeSlider native = slider(1000, 1000);
            native.RepeatCount = 1;
            native.Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_FINISH, volume: 37) };
            native.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 51) });
            native.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 42) });
            native.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 63) });
            Beatmap<HitObject> source = map(native, circle(1000, 90));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset())
            {
                ConversionMode = SticksConversionMode.Standard,
                DisableBeatmapHitsounds = disableHitsounds,
                UseCounterpoint = false,
            };
            SticksHitObject[] baseline = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            converter.ConversionMode = SticksConversionMode.Duet;
            SticksHitObject[] duet = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            SticksFlick accent = added(baseline, duet).OfType<SticksFlick>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(accent.StartTime, Is.EqualTo(1500));
                Assert.That(accent.Samples.Single().Name, Is.EqualTo(disableHitsounds ? HitSampleInfo.HIT_NORMAL : HitSampleInfo.HIT_CLAP));
                Assert.That(accent.Samples.Single().Volume, Is.EqualTo(disableHitsounds ? 100 : 42));
                Assert.That(accent.Samples, Is.Not.SameAs(native.NodeSamples[1]));
                Assert.That(baseline.Select(signature), Is.SubsetOf(duet.Select(signature)));
            });
            assertAddedClearance(baseline, duet);
        }

        [TestCase(260, 0)]
        [TestCase(261, 1)]
        public void TestInteriorAccentRequiresSameStickRecharge(double gap, int expected)
        {
            NativeSlider native = slider(1000, 1000);
            native.RepeatCount = 1;
            Beatmap<HitObject> source = map(native, circle(1500 - gap, 90));
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] duet = convert(source, true);

            Assert.That(added(baseline, duet).Length, Is.EqualTo(expected));
            assertAddedClearance(baseline, duet);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestFallbackDirectionUsesFinalReversalGeometry(bool disableReversals)
        {
            NativeSlider native = slider(1000, 600);
            native.RepeatCount = 1;
            var converter = new SticksBeatmapConverter(map(native), new SticksRuleset())
            {
                DisableReversals = disableReversals,
                UseCounterpoint = false,
            };
            SticksHitObject[] duet = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            SticksSlider primary = duet.OfType<SticksSlider>().Single();
            SticksFlick interior = duet.OfType<SticksFlick>().Single(note => note.StartTime == 1300);

            Assert.Multiple(() =>
            {
                Assert.That(primary.RepeatCount, Is.EqualTo(disableReversals ? 0 : 1));
                Assert.That(interior.Angle, Is.EqualTo(SticksHitObject.NormaliseAngle(primary.AngleAt(interior.StartTime))).Within(0.001));
                Assert.That(interior.Side, Is.Not.EqualTo(primary.Side));
            });
        }

        [Test]
        public void TestPairedNativeSliderDoesNotAlsoReceiveAccompaniment()
        {
            SticksHitObject[] duet = convert(map(slider(1000, 2000)), true);

            Assert.That(duet, Has.Length.EqualTo(2));
            Assert.That(duet, Has.All.TypeOf<SticksSlider>());
        }

        [Test]
        public void TestAccompanimentPhrasesLeaveFourBeatsBetweenNativeSources()
        {
            Beatmap<HitObject> source = map(slider(1000, 300), slider(2000, 300), slider(3000, 300), slider(4000, 300));
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] duet = convert(source, true);

            Assert.That(added(baseline, duet).Select(note => note.StartTime), Is.EqualTo(new[] { 1000d, 3000 }));
            assertAddedClearance(baseline, duet);
        }

        [Test]
        public void TestBpmChangeDoesNotExtrapolateAccompanimentRhythm()
        {
            Beatmap<HitObject> source = map(slider(1000, 500));
            source.ControlPointInfo.Add(1250, new TimingControlPoint { BeatLength = 333 });
            SticksHitObject[] baseline = convert(source, false);
            SticksHitObject[] duet = convert(source, true);

            Assert.That(duet.Select(signature), Is.EqualTo(baseline.Select(signature)));
        }

        [Test]
        public void TestReusingConverterClearsAccompanimentBetweenModes()
        {
            Beatmap<HitObject> source = map(slider(1000, 500));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = SticksConversionMode.Standard, UseCounterpoint = false };
            string[] baseline = converter.Convert().HitObjects.Cast<SticksHitObject>().Select(signature).ToArray();
            converter.ConversionMode = SticksConversionMode.Duet;
            string[] first = converter.Convert().HitObjects.Cast<SticksHitObject>().Select(signature).ToArray();
            string[] repeated = converter.Convert().HitObjects.Cast<SticksHitObject>().Select(signature).ToArray();
            converter.ConversionMode = SticksConversionMode.Standard;
            string[] restored = converter.Convert().HitObjects.Cast<SticksHitObject>().Select(signature).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(first, Has.Length.EqualTo(2));
                Assert.That(repeated, Is.EqualTo(first));
                Assert.That(restored, Is.EqualTo(baseline));
                Assert.That(source.HitObjects, Has.Count.EqualTo(1));
            });
        }

        private static SticksHitObject[] added(SticksHitObject[] baseline, SticksHitObject[] duet)
        {
            var existing = baseline.Select(signature).ToHashSet();
            return duet.Where(note => !existing.Contains(signature(note))).ToArray();
        }

        private static void assertAddedClearance(SticksHitObject[] baseline, SticksHitObject[] duet)
        {
            foreach (SticksHitObject accent in added(baseline, duet))
            {
                foreach (SticksHitObject other in duet.Where(note => note != accent && note.Side == accent.Side))
                {
                    double gap = other.StartTime < accent.StartTime ? accent.StartTime - endTime(other) : other.StartTime - endTime(accent);
                    Assert.That(gap, Is.GreaterThan(SticksBeatmapConverter.RAPID_ALTERNATION_THRESHOLD));
                }
            }
        }

        private static string signature(SticksHitObject note) =>
            $"{note.GetType().Name}:{note.StartTime}:{note.Side}:{note.Angle}:{endTime(note)}:"
            + (note is SticksSlider sliderObject ? string.Join(",", sliderObject.SegmentArcAngles) : string.Empty);

        private static double endTime(SticksHitObject note) => note is IHasDuration duration ? duration.EndTime : note.StartTime;

        private static SticksHitObject[] convert(Beatmap<HitObject> source, bool duet)
        {
            // Isolate the former duet accompaniment templates from the newer arrangement pass.
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { UseCounterpoint = false };
            if (!duet)
                converter.ConversionMode = SticksConversionMode.Standard;
            return converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
        }

        private static Beatmap<HitObject> map(params HitObject[] objects)
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.AddRange(objects);
            return source;
        }

        private static SourceCircle circle(double time, float angle) => new SourceCircle { StartTime = time, Position = position(angle) };
        private static NativeSlider slider(double time, double duration) => new NativeSlider { StartTime = time, Duration = duration, Position = position(0) };
        private static Vector2 position(float angle) => SticksBeatmapConverter.STANDARD_CENTRE + 160 * new Vector2(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180));

        private class SourceCircle : HitObject, IHasPosition
        {
            public Vector2 Position { get; set; }
            public float X { get => Position.X; set => Position = new Vector2(value, Y); }
            public float Y { get => Position.Y; set => Position = new Vector2(X, value); }
        }

        private sealed class NativeSlider : SourceCircle, IHasDuration, IHasRepeats
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
            public int RepeatCount { get; set; }
            public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();
        }
    }
}
