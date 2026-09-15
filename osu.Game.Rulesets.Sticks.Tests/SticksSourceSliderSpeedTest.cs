using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksSourceSliderSpeedTest
    {
        [Test]
        public void SourceSpeedUsesTheMapSliderMultiplierWithoutAMod()
        {
            var source = map(path(1000, 140), path(2500, 280), path(4000, 140));
            Assert.That(convert(source).OfType<SticksSlider>().Select(speed),
                Is.EqualTo(new double[] { 90, 180, 90 }).Within(0.001));
            source.Difficulty.SliderMultiplier = 2.8;
            Assert.That(convert(source).OfType<SticksSlider>().Select(speed),
                Is.EqualTo(new double[] { 45, 90, 45 }).Within(0.001));
        }

        [Test]
        public void SourceSpeedChangesSurviveUnaccentedRunsAndLongSliders()
        {
            var notes = Enumerable.Range(1, 6).Select(i => path(i * 1000, 140)).ToList();
            notes.AddRange(new[] { path(8000, 420), path(8500, 420), path(9000, 420), path(11000, 2100, duration: 2500) });
            notes[8].Samples.Add(new HitSampleInfo(HitSampleInfo.HIT_CLAP));
            var source = map(notes.ToArray());
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { SoloConversion = true };
            var converted = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            foreach (double time in new double[] { 8000, 8500, 9000, 11000 })
                Assert.That(speed(at(converted, time)), Is.EqualTo(270).Within(0.001),
                    "Equal source speeds stay equal regardless of accents or slider duration.");
            Assert.That(converter.Convert().HitObjects.Cast<SticksHitObject>().Select(signature), Is.EqualTo(converted.Select(signature)));
            Assert.That(notes[6].Path.Distance, Is.EqualTo(420));
        }

        [Test]
        public void ReversalsClippingAndOtherModsKeepTheirTimingAndSpeedBounds()
        {
            var original = path(2500, 2800, duration: 1000, repeats: 1);
            var source = map(path(1000, 140), original, path(3100, 140), path(4500, 140));
            var slider = at(convert(source), 2500);
            Assert.That(slider.EndTime, Is.EqualTo(3100), "Solo's clipped endpoint must survive the speed change.");
            Assert.That(slider.SegmentCount, Is.EqualTo(2));
            Assert.That(slider.SegmentEndsWithReversal(0), Is.True);
            Assert.That(speed(slider), Is.EqualTo(720).Within(0.001));
            Assert.That(Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentDurationAt),
                Is.EqualTo(new double[] { 500, 100 }).Within(0.001));

            var adjust = new SticksModDifficultyAdjust { DisableReversals = { Value = true } };
            Mod[] mods = { new SticksModParity(), new SticksModEncore(), adjust };
            var forward = convert(source, mods);
            Assert.That(convert(source, mods.Reverse().ToArray()).Select(signature), Is.EqualTo(forward.Select(signature)));
            var continuous = at(forward, 2500);
            Assert.That(continuous.RepeatCount, Is.Zero);
            Assert.That(speed(continuous), Is.EqualTo(720).Within(0.001));
            Assert.That(continuous.EndTime, Is.EqualTo(3100));
        }

        [Test]
        public void OnlySourcePathsRescaleAndAuthoredSlidersBypassConversion()
        {
            var source = map(path(1000, 140), path(2500, 840, duration: 1000), path(4500, 140));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { LimitBeginnerCoordination = false };
            var changed = new HashSet<SticksSlider>();
            converter.SourceSliderBurstObserved = (slider, _, _) => changed.Add(slider);
            var converted = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            Assert.That(changed, Has.Count.EqualTo(1));
            Assert.That(changed.Single().StartTime, Is.EqualTo(2500));
            Assert.That(speed(changed.Single()), Is.EqualTo(270).Within(0.001));
            Assert.That(converted.OfType<SticksSlider>().Where(slider => !changed.Contains(slider)).Select(speed),
                Has.All.LessThanOrEqualTo(SticksBeatmapConverter.MAX_GENERATED_SLIDER_ANGULAR_VELOCITY + 0.001));

            foreach (bool carrier in new[] { false, true })
            {
                var authored = new SticksSlider { StartTime = 1000, Duration = 500, ArcAngle = 30, SizeMultiplier = 1.25f };
                IBeatmap native = carrier ? map(SticksAuthoredBeatmapCodec.CreateLegacyProxy(authored))
                    : new Beatmap<SticksHitObject> { HitObjects = new List<SticksHitObject> { authored } };
                var restored = (SticksSlider)convert(native).Single();
                Assert.That(signature(restored), Is.EqualTo(signature(authored)));
                Assert.That(restored.SizeMultiplier, Is.EqualTo(authored.SizeMultiplier));
            }
        }

        [Test]
        public void CompleteSourceAuditIncludesEveryFierySpeedGroup()
        {
            // Fiery's Extreme has 26 source sliders at 255 px/s, 11 at 510, and 8 at 612.
            float[] velocities = Enumerable.Repeat(255f, 26).Concat(Enumerable.Repeat(510f, 11)).Concat(Enumerable.Repeat(612f, 8)).ToArray();
            var source = map(velocities.Select((velocity, i) => path(1000 + i * 1000, velocity * 0.2f, 200)).ToArray());
            source.Difficulty.SliderMultiplier = 1.8;
            var observed = new List<(double SourceSpeed, double ConvertedSpeed)>();
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { SoloConversion = true };
            converter.SourceSliderObserved = (original, slider) => observed.Add(
                (((IHasPath)original).Path.Distance / ((IHasDuration)original).Duration * 1000, speed(slider)));
            converter.Convert();

            Assert.That(observed, Has.Count.EqualTo(45), "An audit of changed sliders alone omits the entire slow group.");
            Assert.That(observed.Take(26).Select(value => value.SourceSpeed), Has.All.EqualTo(255).Within(0.001));
            Assert.That(observed.Skip(26).Take(11).Select(value => value.SourceSpeed), Has.All.EqualTo(510).Within(0.001));
            Assert.That(observed.Skip(37).Select(value => value.SourceSpeed), Has.All.EqualTo(612).Within(0.001));
            Assert.That(observed.Take(26).Select(value => value.ConvertedSpeed), Has.All.EqualTo(63.75).Within(0.001));
            Assert.That(observed.Skip(26).Take(11).Select(value => value.ConvertedSpeed), Has.All.EqualTo(127.5).Within(0.001));
            Assert.That(observed.Skip(37).Select(value => value.ConvertedSpeed), Has.All.EqualTo(153).Within(0.001));
        }

        [Test]
        public void ExtremeSourceVelocityCannotExceedTheContinuousOrReversalCeiling()
        {
            var source = map(path(1000, 140), path(2500, 140), path(4000, 140),
                path(5500, 14000), path(7000, 14000, duration: 1000, repeats: 1));
            var converted = convert(source);
            Assert.That(speed(at(converted, 5500)), Is.EqualTo(720).Within(0.001));
            Assert.That(speed(at(converted, 7000)), Is.EqualTo(720).Within(0.001));
            Assert.That(at(converted, 7000).SegmentEndsWithReversal(0), Is.True);
        }

        [Test]
        public void DefaultConversionWidensOnlyFastSourceSlidersAndCapsAt720()
        {
            var source = map(path(1000, 280), path(2500, 560), path(4000, 840), path(5500, 1120), path(7000, 2240));
            var converted = convert(source).OfType<SticksSlider>().ToArray();
            Assert.That(converted.Select(speed), Is.EqualTo(new[] { 180d, 360, 540, 720, 720 }).Within(0.001));
            Assert.That(converted.Select(note => note.SizeMultiplier), Is.EqualTo(new[] { 1f, 1, 1.5f, 2, 2 }));
            foreach (var note in converted)
            {
                note.ApplyDefaults(source.ControlPointInfo, source.Difficulty);
                Assert.That(note.PrimaryHitAngle, Is.EqualTo(SticksHitObject.HitAngleForCircleSize(source.Difficulty.CircleSize) * note.SizeMultiplier).Within(0.001));
            }
        }

        private static SticksHitObject[] convert(IBeatmap source, params Mod[] mods)
        {
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { SoloConversion = true };
            foreach (IApplicableToBeatmapConverter mod in mods.OfType<IApplicableToBeatmapConverter>())
                mod.ApplyToBeatmapConverter(converter);
            return converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
        }

        private static SticksSlider at(IEnumerable<SticksHitObject> notes, double time) => notes.OfType<SticksSlider>().Single(note => note.StartTime == time);
        private static double speed(SticksSlider slider) => Enumerable.Range(0, slider.SegmentCount)
            .Max(i => Math.Abs(slider.SegmentArcAngleAt(i)) / slider.SegmentDurationAt(i) * 1000);
        private static string signature(SticksHitObject note) => $"{note.GetType().Name}:{note.StartTime}:{note.GetEndTime()}:{note.Side}:{note.Angle}:"
            + (note is SticksSlider slider ? string.Join(',', slider.SegmentArcAngles) : "");

        private static Beatmap<HitObject> map(params HitObject[] notes)
        {
            var source = new Beatmap<HitObject>();
            source.Difficulty.OverallDifficulty = 7;
            source.Difficulty.SliderMultiplier = 1.4;
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.AddRange(notes);
            return source;
        }

        private static SourceSlider path(double start, float distance, double duration = 500, int repeats = 0) => new SourceSlider
        {
            StartTime = start,
            Duration = duration,
            RepeatCount = repeats,
            Path = new SliderPath(new[] { new PathControlPoint(Vector2.Zero, PathType.LINEAR), new PathControlPoint(new Vector2(0, distance)) }, null),
        };

        private sealed class SourceSlider : HitObject, IHasPosition, IHasPath, IHasDuration, IHasRepeats
        {
            public Vector2 Position { get; set; } = new Vector2(416, 192);
            public float X { get => Position.X; set => Position = new Vector2(value, Y); }
            public float Y { get => Position.Y; set => Position = new Vector2(X, value); }
            public SliderPath Path { get; set; } = new SliderPath();
            public double Distance => Path.Distance;
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
            public int RepeatCount { get; set; }
            public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();
        }
    }
}
