using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Online.API;
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
    public class SticksModSurgeTest
    {
        [Test]
        public void ModesUseDifferentSpeedReferencesAndSettingsRoundtrip()
        {
            var source = map(path(1000, 140), path(2500, 280), path(4000, 140));
            var ruleset = new SticksRuleset();
            Assert.That(ruleset.GetModsFor(ModType.Conversion), Has.Exactly(1).TypeOf<SticksModSurge>());
            var sourceMod = new SticksModSurge();
            var relativeMod = new SticksModSurge { SpeedInterpretation = { Value = SticksSliderBurstMode.RelativeEmphasis } };
            var restored = (SticksModSurge)new APIMod(relativeMod).ToMod(ruleset);
            Assert.That(restored.SpeedInterpretation.Value, Is.EqualTo(SticksSliderBurstMode.RelativeEmphasis));
            Assert.That(sourceMod.Ranked, Is.False);

            SticksHitObject[] baseline = convert(source);
            SticksHitObject[] absolute = convert(source, sourceMod);
            SticksHitObject[] relative = convert(source, restored);
            Assert.That(speed(at(baseline, 2500)), Is.EqualTo(90).Within(0.001));
            Assert.That(speed(at(absolute, 2500)), Is.EqualTo(180).Within(0.001));
            Assert.That(speed(at(relative, 2500)), Is.EqualTo(180).Within(0.001));
            Assert.That(speed(at(absolute, 1000)), Is.EqualTo(90).Within(0.001));
            Assert.That(speed(at(relative, 1000)), Is.EqualTo(120).Within(0.001));
            Assert.That(relative.Select(identity), Is.EqualTo(baseline.Select(identity)));
            Assert.That(absolute.Select(identity), Is.EqualTo(baseline.Select(identity)));

            // Changing every source path's scale changes absolute speed, but not its emphasis.
            var scaled = map(path(1000, 420), path(2500, 840), path(4000, 420));
            Assert.That(convert(scaled, restored).OfType<SticksSlider>().Select(speed),
                Is.EqualTo(relative.OfType<SticksSlider>().Select(speed)).Within(0.001));
            Assert.That(convert(scaled, sourceMod).OfType<SticksSlider>().Select(speed),
                Is.Not.EqualTo(absolute.OfType<SticksSlider>().Select(speed)));
        }

        [TestCase(SticksSliderBurstMode.SourceSpeed)]
        [TestCase(SticksSliderBurstMode.RelativeEmphasis)]
        public void SourceSpeedChangesSurviveUnaccentedRunsAndLongSliders(SticksSliderBurstMode mode)
        {
            var notes = Enumerable.Range(1, 6).Select(i => path(i * 1000, 140)).ToList();
            notes.AddRange(new[] { path(8000, 420), path(8500, 420), path(9000, 420), path(11000, 2100, duration: 2500) });
            notes[8].Samples.Add(new HitSampleInfo(HitSampleInfo.HIT_CLAP));
            var source = map(notes.ToArray());
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { SoloConversion = true };
            string[] baseline = converter.Convert().HitObjects.Cast<SticksHitObject>().Select(signature).ToArray();
            new SticksModSurge { SpeedInterpretation = { Value = mode } }.ApplyToBeatmapConverter(converter);
            var converted = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            Assert.That(speed(at(converted, 8000)), Is.GreaterThan(120));
            Assert.That(speed(at(converted, 8500)), Is.EqualTo(speed(at(converted, 8000))).Within(0.001),
                "Equal source speed must not lose emphasis merely because the next slider is unaccented.");
            Assert.That(speed(at(converted, 9000)), Is.EqualTo(speed(at(converted, 8000))).Within(0.001));
            Assert.That(speed(at(converted, 11000)), Is.EqualTo(speed(at(converted, 8000))).Within(0.001),
                "Equal source speed must remain equal on long sustains too.");
            Assert.That(converter.Convert().HitObjects.Cast<SticksHitObject>().Select(signature), Is.EqualTo(converted.Select(signature)));
            converter.SliderBurstMode = null;
            Assert.That(converter.Convert().HitObjects.Cast<SticksHitObject>().Select(signature), Is.EqualTo(baseline));
            Assert.That(notes[6].Path.Distance, Is.EqualTo(420));
        }

        [TestCase(SticksSliderBurstMode.SourceSpeed)]
        [TestCase(SticksSliderBurstMode.RelativeEmphasis)]
        public void ReversalsClippingAndOtherModsKeepTheirTimingAndSpeedBounds(SticksSliderBurstMode mode)
        {
            var original = path(2500, 2800, duration: 1000, repeats: 1);
            var source = map(path(1000, 140), original, path(3100, 140), path(4500, 140));
            var surge = new SticksModSurge { SpeedInterpretation = { Value = mode } };
            var baseline = at(convert(source), 2500);
            var burst = at(convert(source, surge), 2500);
            Assert.That(burst.EndTime, Is.EqualTo(3100), "Solo's clipped endpoint must survive the speed change.");
            Assert.That(burst.SegmentCount, Is.EqualTo(2));
            Assert.That(burst.SegmentEndsWithReversal(0), Is.True);
            Assert.That(speed(burst), Is.GreaterThan(120).And.LessThanOrEqualTo(160.001));
            Assert.That(burst.Angle, Is.EqualTo(baseline.Angle));
            Assert.That(Enumerable.Range(0, burst.SegmentCount).Select(burst.SegmentDurationAt),
                Is.EqualTo(Enumerable.Range(0, baseline.SegmentCount).Select(baseline.SegmentDurationAt)));

            var adjust = new SticksModDifficultyAdjust { DisableReversals = { Value = true } };
            Mod[] mods = { surge, new SticksModParity(), new SticksModEncore(), adjust };
            var forward = convert(source, mods);
            Assert.That(convert(source, mods.Reverse().ToArray()).Select(signature), Is.EqualTo(forward.Select(signature)));
            var continuous = at(forward, 2500);
            Assert.That(continuous.RepeatCount, Is.Zero);
            Assert.That(speed(continuous), Is.GreaterThan(160).And.LessThanOrEqualTo(240.001));
            Assert.That(continuous.EndTime, Is.EqualTo(3100));
        }

        [TestCase(SticksSliderBurstMode.SourceSpeed)]
        [TestCase(SticksSliderBurstMode.RelativeEmphasis)]
        public void OnlySourcePathsChangeAndAuthoredSlidersBypassTheExperiment(SticksSliderBurstMode mode)
        {
            var source = map(path(1000, 140), path(2500, 840, duration: 1000), path(4500, 140));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { LimitBeginnerCoordination = false };
            var baseline = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            var changed = new HashSet<SticksSlider>();
            converter.SourceSliderBurstObserved = (slider, _, _) => changed.Add(slider);
            new SticksModSurge { SpeedInterpretation = { Value = mode } }.ApplyToBeatmapConverter(converter);
            var converted = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            Assert.That(changed, Has.Count.EqualTo(mode == SticksSliderBurstMode.SourceSpeed ? 1 : 3));
            Assert.That(converted.Select(identity), Is.EqualTo(baseline.Select(identity)));
            for (int i = 0; i < converted.Length; i++)
            {
                if (converted[i] is not SticksSlider slider || !changed.Contains(slider))
                    Assert.That(signature(converted[i]), Is.EqualTo(signature(baseline[i])));
            }

            foreach (bool carrier in new[] { false, true })
            {
                var authored = new SticksSlider { StartTime = 1000, Duration = 500, ArcAngle = 30 };
                IBeatmap native = carrier ? map(SticksAuthoredBeatmapCodec.CreateLegacyProxy(authored))
                    : new Beatmap<SticksHitObject> { HitObjects = new List<SticksHitObject> { authored } };
                Assert.That(convert(native, new SticksModSurge { SpeedInterpretation = { Value = mode } }).Select(signature),
                    Is.EqualTo(convert(native).Select(signature)));
            }
        }

        [TestCase(null)]
        [TestCase(SticksSliderBurstMode.SourceSpeed)]
        [TestCase(SticksSliderBurstMode.RelativeEmphasis)]
        public void CompleteSourceAuditIncludesEveryFierySpeedGroup(SticksSliderBurstMode? mode)
        {
            // Fiery's Extreme has 26 source sliders at 255 px/s, 11 at 510, and 8 at 612.
            float[] velocities = Enumerable.Repeat(255f, 26).Concat(Enumerable.Repeat(510f, 11)).Concat(Enumerable.Repeat(612f, 8)).ToArray();
            var source = map(velocities.Select((velocity, i) => path(1000 + i * 1000, velocity * 0.2f, 200)).ToArray());
            source.Difficulty.SliderMultiplier = 1.8;
            var observed = new List<(double SourceSpeed, double ConvertedSpeed)>();
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { SoloConversion = true, SliderBurstMode = mode };
            converter.SourceSliderObserved = (original, slider) => observed.Add(
                (((IHasPath)original).Path.Distance / ((IHasDuration)original).Duration * 1000, speed(slider)));
            converter.Convert();

            Assert.That(observed, Has.Count.EqualTo(45), "An audit of changed sliders alone omits the entire slow group.");
            Assert.That(observed.Take(26).Select(value => value.SourceSpeed), Has.All.EqualTo(255).Within(0.001));
            Assert.That(observed.Skip(26).Take(11).Select(value => value.SourceSpeed), Has.All.EqualTo(510).Within(0.001));
            Assert.That(observed.Skip(37).Select(value => value.SourceSpeed), Has.All.EqualTo(612).Within(0.001));
            double slowest = mode == SticksSliderBurstMode.SourceSpeed ? 63.75 : 120;
            Assert.That(observed.Take(26).Select(value => value.ConvertedSpeed), Has.All.EqualTo(slowest).Within(0.001));

            double middle = mode == SticksSliderBurstMode.RelativeEmphasis ? 180 : mode == SticksSliderBurstMode.SourceSpeed ? 127.5 : 120;
            double fastest = mode == SticksSliderBurstMode.RelativeEmphasis ? 190 : mode == SticksSliderBurstMode.SourceSpeed ? 153 : 120;
            Assert.That(observed.Skip(26).Take(11).Select(value => value.ConvertedSpeed), Has.All.EqualTo(middle).Within(0.001));
            Assert.That(observed.Skip(37).Select(value => value.ConvertedSpeed), Has.All.EqualTo(fastest).Within(0.001));
        }

        [Test]
        public void RelativeFloorDoesNotLowerTheNormalReferenceOrAlterFastEmphasis()
        {
            // Median 280 px/s, with source sliders below, at and above that speed.
            var source = map(path(1000, 14), path(2500, 112), path(4000, 140), path(5500, 140),
                path(7000, 140), path(8500, 280), path(10000, 336));
            var mod = new SticksModSurge { SpeedInterpretation = { Value = SticksSliderBurstMode.RelativeEmphasis } };
            Assert.That(convert(source, mod).OfType<SticksSlider>().Select(speed),
                Is.EqualTo(new double[] { 80, 90, 120, 120, 120, 180, 190 }).Within(0.001));
        }

        [TestCase(SticksSliderBurstMode.SourceSpeed)]
        [TestCase(SticksSliderBurstMode.RelativeEmphasis)]
        public void ExtremeSourceVelocityCannotExceedTheContinuousOrReversalCeiling(SticksSliderBurstMode mode)
        {
            var source = map(path(1000, 140), path(2500, 140), path(4000, 140),
                path(5500, 14000), path(7000, 14000, duration: 1000, repeats: 1));
            var mod = new SticksModSurge { SpeedInterpretation = { Value = mode } };
            var converted = convert(source, mod);
            Assert.That(speed(at(converted, 5500)), Is.GreaterThan(200).And.LessThanOrEqualTo(240.001));
            Assert.That(speed(at(converted, 7000)), Is.EqualTo(160).Within(0.001));
            Assert.That(at(converted, 7000).SegmentEndsWithReversal(0), Is.True);
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
        private static string identity(SticksHitObject note) => $"{note.GetType().Name}:{note.StartTime}:{note.GetEndTime()}:{note.Side}";
        private static string signature(SticksHitObject note) => $"{identity(note)}:{note.Angle}:"
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
