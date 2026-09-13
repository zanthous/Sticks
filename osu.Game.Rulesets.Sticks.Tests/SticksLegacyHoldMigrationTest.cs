using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksLegacyHoldMigrationTest
    {
        [TestCase(StickSide.Left, 0.25)]
        [TestCase(StickSide.Right, 1750.5)]
        public void LegacyHoldMarkerPreservesGeometrySamplesAndScoringEvents(StickSide side, double duration)
        {
            var legacy = new SticksHold
            {
                StartTime = 1000.125,
                Duration = duration,
                Side = side,
                Angle = 359.75f,
                Samples = new[]
                {
                    new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 43),
                    new HitSampleInfo(HitSampleInfo.HIT_CLAP, "soft", volume: 67),
                },
            };
            HitObject carrier = SticksAuthoredBeatmapCodec.CreateLegacyProxy(legacy);
            Assert.That(SticksAuthoredBeatmapCodec.EncodeMarker(legacy), Does.Contain("~h~"), "The fixture exercises the legacy format.");
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(carrier, out SticksHitObject decoded), Is.True);
            Assert.That(decoded, Is.TypeOf<SticksSlider>());
            var slider = (SticksSlider)decoded;

            var beatmap = new Beatmap<SticksHitObject>();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.ControlPointInfo.Add(1600, new TimingControlPoint { BeatLength = 250 });
            legacy.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
            slider.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

            Assert.Multiple(() =>
            {
                Assert.That(slider.IsStationary, Is.True);
                Assert.That(slider.StartTime, Is.EqualTo(legacy.StartTime));
                Assert.That(slider.Duration, Is.EqualTo(legacy.Duration));
                Assert.That(slider.Side, Is.EqualTo(side));
                Assert.That(slider.Angle, Is.EqualTo(legacy.Angle));
                Assert.That(slider.AngleAt(slider.EndTime), Is.EqualTo(legacy.Angle));
                Assert.That(sampleSignature(slider.Samples), Is.EqualTo(sampleSignature(legacy.Samples)));
                Assert.That(events(slider), Is.EqualTo(events(legacy)), "One stationary span keeps the old head, ticks, tail and judgement classes.");
                Assert.That(slider.NestedHitObjects.OfType<SticksSliderRepeat>(), Is.Empty);
                Assert.That(slider.NestedHitObjects.OfType<SticksSliderExtension>(), Is.Empty);
            });

            string marker = SticksAuthoredBeatmapCodec.EncodeMarker(slider);
            Assert.That(marker, Does.Contain("~s~"));
            for (int i = 0; i < 10; i++)
            {
                Assert.That(SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(slider), out decoded), Is.True);
                slider = (SticksSlider)decoded;
                Assert.That(SticksAuthoredBeatmapCodec.EncodeMarker(slider), Is.EqualTo(marker));
            }
        }

        [TestCase("sticks-v1~s~r~359.75~1250.5~0~0.wav", 1)]
        [TestCase("sticks-v1~s~r~359.75~1250.5~0~2.wav", 3)]
        [TestCase("sticks-v2~s~r~359.75~1250.5~0.wav", 1)]
        [TestCase("sticks-v2~s~r~359.75~1250.5~0_0.wav", 2)]
        [TestCase("sticks-v3~s~r~359.75~1250.5~0_0~.25_.75.wav", 2)]
        public void StationarySliderCarriersRoundTripThroughAuthoredConversion(string filename, int segmentCount)
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(markerObject(filename));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset())
            {
                ConversionMode = SticksConversionMode.ParityDuet,
                UseCounterpoint = true,
                AddClickNotes = true,
            };
            Assert.That(converter.CanConvert(), Is.True);
            var slider = (SticksSlider)converter.Convert().HitObjects.Single();
            Assert.Multiple(() =>
            {
                Assert.That(slider.IsStationary, Is.True);
                Assert.That(slider.StartTime, Is.EqualTo(1000));
                Assert.That(slider.Duration, Is.EqualTo(1250.5));
                Assert.That(slider.Angle, Is.EqualTo(359.75));
                Assert.That(slider.Side, Is.EqualTo(StickSide.Right));
                Assert.That(slider.SegmentCount, Is.EqualTo(segmentCount));
                Assert.That(SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(slider), out _), Is.True);
            });
        }

        [TestCase("sticks-v1~s~l~20~1000~0.25~0.wav")]
        [TestCase("sticks-v2~s~l~20~1000~0.25_-0.125.wav")]
        public void SmallEditsAwayFromStationaryRemainSaveable(string filename)
        {
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(markerObject(filename), out SticksHitObject decoded), Is.True);
            var slider = (SticksSlider)decoded;
            Assert.That(slider.IsStationary, Is.False);
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(slider), out decoded), Is.True);
            Assert.That(((SticksSlider)decoded).SegmentArcAngles, Is.EqualTo(slider.SegmentArcAngles));
        }

        [TestCase("sticks-v1~s~l~20~0~0~0.wav")]
        [TestCase("sticks-v1~s~l~20~1000~0~16.wav")]
        [TestCase("sticks-v2~s~l~20~1000~0_90.wav")]
        [TestCase("sticks-v3~s~l~20~1000~0_0~1_0.wav")]
        public void StationarySupportStillRejectsInvalidDurationBoundsAndUntimedPauses(string filename)
        {
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(markerObject(filename), out _), Is.False);
        }

        [Test]
        public void NativeLegacyHoldMigrationDoesNotMutateSourceOrLoseCustomSamples()
        {
            var hold = new SticksHold
            {
                StartTime = 1000.25,
                Duration = 1250.5,
                Side = StickSide.Right,
                Angle = 271.125f,
                PrimaryHitAngle = 30,
                SecondaryHitAngle = 15,
                Samples = new HitSampleInfo[]
                {
                    new ConvertHitObjectParser.FileHitSampleInfo("custom-hold.wav", 38),
                    new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, "drum", volume: 61),
                },
            };
            var source = new Beatmap<SticksHitObject>();
            source.HitObjects.Add(hold);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            var converted = (SticksSlider)converter.Convert().HitObjects.Single();

            Assert.Multiple(() =>
            {
                Assert.That(source.HitObjects.Single(), Is.SameAs(hold));
                Assert.That(converted.IsStationary, Is.True);
                Assert.That(converted.StartTime, Is.EqualTo(hold.StartTime));
                Assert.That(converted.Duration, Is.EqualTo(hold.Duration));
                Assert.That(converted.Side, Is.EqualTo(hold.Side));
                Assert.That(converted.Angle, Is.EqualTo(hold.Angle));
                Assert.That(converted.PrimaryHitAngle, Is.EqualTo(hold.PrimaryHitAngle));
                Assert.That(converted.SecondaryHitAngle, Is.EqualTo(hold.SecondaryHitAngle));
                Assert.That(sampleSignature(converted.Samples), Is.EqualTo(sampleSignature(hold.Samples)));
                Assert.That(converted.Samples, Is.Not.SameAs(hold.Samples));
                Assert.That(converted.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>().Single().Filename,
                    Is.EqualTo("custom-hold.wav"));
            });
        }

        private static HitObject markerObject(string filename) => new CarrierHitObject
        {
            StartTime = 1000,
            Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo(filename, 43) },
        };

        private static string[] events(HitObject root) => flatten(root).Select(hitObject =>
            $"{hitObject.StartTime:R}:{hitObject.CreateJudgement().GetType().Name}:{hitObject.CreateJudgement().MaxResult}:"
            + sampleSignature(hitObject.Samples)).ToArray();

        private static IEnumerable<HitObject> flatten(HitObject root)
        {
            foreach (HitObject nested in root.NestedHitObjects)
            {
                yield return nested;
                foreach (HitObject child in flatten(nested))
                    yield return child;
            }
        }

        private sealed class CarrierHitObject : HitObject
        {
        }

        private static string sampleSignature(IEnumerable<HitSampleInfo> samples) =>
            string.Join(';', samples.Select(sample => $"{sample.Name}:{sample.Bank}:{sample.Volume}"));
    }
}
