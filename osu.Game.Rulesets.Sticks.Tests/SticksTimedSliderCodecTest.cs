using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksTimedSliderCodecTest
    {
        [Test]
        [SetCulture("fr-FR")]
        public void TestTimedPathRoundTripsThroughProxyWithoutLosingCheckpoints()
        {
            SticksSlider source = createTimedSlider();
            HitObject proxy = SticksAuthoredBeatmapCodec.CreateLegacyProxy(source);
            string marker = proxy.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>().Single().Filename;

            Assert.That(marker, Does.StartWith(SticksAuthoredBeatmapCodec.TIMED_SEGMENT_MARKER_PREFIX));
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(proxy, out SticksHitObject decodedObject), Is.True);
            var decoded = (SticksSlider)decodedObject;
            assertSamePath(source, decoded);

            // Repeated saves must retain fractional source timing, dwell spans and tiny turns.
            for (int i = 0; i < 20; i++)
            {
                Assert.That(SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(decoded), out decodedObject), Is.True);
                decoded = (SticksSlider)decodedObject;
            }

            Assert.That(SticksAuthoredBeatmapCodec.EncodeMarker(decoded), Is.EqualTo(marker));
            assertSamePath(source, decoded);
        }

        [Test]
        public void TestTimedCarrierSurvivesLegacyTextDecodeAndAuthoredConversion()
        {
            SticksSlider source = createTimedSlider();
            string hitObject = FormattableString.Invariant($"256,192,{source.StartTime:R},8,0,{source.EndTime:R},0:0:0:100:{SticksAuthoredBeatmapCodec.EncodeMarker(source)}");
            string text = "osu file format v14\n\n[General]\nMode:0\n\n[Difficulty]\nCircleSize:5\nOverallDifficulty:5\n"
                          + "SliderMultiplier:1.4\nSliderTickRate:1\n\n[TimingPoints]\n0,500,4,2,0,100,1,0\n\n[HitObjects]\n"
                          + hitObject + "\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            using var reader = new LineBufferedReader(stream);
            var decodedBeatmap = new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader);
            SticksAuthoredBeatmapCodec.MarkerInspection marker = SticksAuthoredBeatmapCodec.InspectMarker(decodedBeatmap.HitObjects.Single());

            Assert.Multiple(() =>
            {
                Assert.That(marker.Status, Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.ValidSupported));
                Assert.That(marker.Version, Is.EqualTo(3));
            });

            var converted = (SticksSlider)new SticksBeatmapConverter(decodedBeatmap, new SticksRuleset()).Convert().HitObjects.Single();
            assertSamePath(source, converted);
        }

        [TestCase("sticks-v3~s~l~0~1000~~1.wav")]
        [TestCase("sticks-v3~s~l~0~1000~90~.5_.5.wav")]
        [TestCase("sticks-v3~s~l~0~1000~90_0~1.wav")]
        [TestCase("sticks-v3~s~l~0~1000~90_0~1_0.wav")]
        [TestCase("sticks-v3~s~l~0~1000~90_0~1_-1.wav")]
        [TestCase("sticks-v3~s~l~0~1000~90~NaN.wav")]
        [TestCase("sticks-v3~s~l~0~1000~90~Infinity.wav")]
        [TestCase("sticks-v3~s~l~0~1000~NaN~1.wav")]
        [TestCase("sticks-v3~s~l~0~1000~1e40~1.wav")]
        [TestCase("sticks-v3~s~l~0~1000~3e38_3e38~1_1.wav")]
        [TestCase("sticks-v3~s~l~0~1000~90_0~1e308_1e308.wav")]
        [TestCase("sticks-v3~s~l~0~1000~90_0~5e-324_1e308.wav")]
        [TestCase("sticks-v3~s~l~0~5e-324~90_0~1_3.wav")]
        [TestCase("sticks-v3~s~l~0~1.7976931348623157e308~90~1.0000000000001.wav")]
        [TestCase("sticks-v3~s~l~0~-1000~90~1.wav")]
        [TestCase("sticks-v3~s~l~0~Infinity~90~1.wav")]
        [TestCase("sticks-v3~s~l~NaN~1000~90~1.wav")]
        [TestCase("sticks-v3~s~x~0~1000~90~1.wav")]
        [TestCase("sticks-v3~s~l~0~1000~90~1~extra.wav")]
        [TestCase("sticks-v3~f~l~0.wav")]
        public void TestMalformedTimedCarrierFailsClosed(string filename)
        {
            HitObject proxy = markerObject(filename);
            SticksAuthoredBeatmapCodec.MarkerInspection inspection = SticksAuthoredBeatmapCodec.InspectMarker(proxy);

            Assert.Multiple(() =>
            {
                Assert.That(SticksAuthoredBeatmapCodec.IsMarker(proxy.Samples.Single()), Is.True);
                Assert.That(inspection.Status, Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.MalformedSupported));
                Assert.That(inspection.Version, Is.EqualTo(3));
                Assert.That(SticksAuthoredBeatmapCodec.TryDecode(proxy, out _), Is.False);
            });

            var beatmap = new Beatmap<HitObject>();
            beatmap.HitObjects.Add(proxy);
            Assert.That(new SticksBeatmapConverter(beatmap, new SticksRuleset()).CanConvert(), Is.False);
        }

        [TestCase(SticksSlider.MAX_SEGMENT_COUNT, true)]
        [TestCase(SticksSlider.MAX_SEGMENT_COUNT + 1, false)]
        public void TestTimedCarrierEnforcesSegmentBound(int count, bool valid)
        {
            string arcs = string.Join('_', Enumerable.Range(0, count).Select(index => index == 0 ? "90" : "0"));
            string weights = string.Join('_', Enumerable.Repeat("1", count));
            HitObject proxy = markerObject($"sticks-v3~s~l~0~1000~{arcs}~{weights}.wav");

            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(proxy, out SticksHitObject decoded), Is.EqualTo(valid));
            if (valid)
                Assert.That(((SticksSlider)decoded).SegmentCount, Is.EqualTo(count));
        }

        [TestCase("samples/sticks-v4~s~l~0~1000~90~1.wav")]
        [TestCase(@"samples\sticks-v4~s~l~0~1000~90~1.wav")]
        [TestCase("sticks-v4~p~l~0~2000~180~1~-1~2.wav")]
        public void TestUnsupportedCarrierCannotFallBackToProceduralConversion(string filename)
        {
            HitObject proxy = markerObject(filename);
            SticksAuthoredBeatmapCodec.MarkerInspection inspection = SticksAuthoredBeatmapCodec.InspectMarker(proxy);

            Assert.Multiple(() =>
            {
                Assert.That(inspection.Status, Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.UnsupportedVersion));
                Assert.That(inspection.Version, Is.EqualTo(4));
                Assert.That(inspection.Decoded, Is.Null);
                Assert.That(SticksAuthoredBeatmapCodec.TryDecode(proxy, out _), Is.False);
            });

            var beatmap = new Beatmap<HitObject>();
            beatmap.HitObjects.Add(proxy);
            Assert.That(new SticksBeatmapConverter(beatmap, new SticksRuleset()).CanConvert(), Is.False);
        }

        [Test]
        public void TestLegacySliderMarkersKeepTheirExistingEncodingAndTiming()
        {
            var repeated = new SticksSlider { Duration = 900, Angle = 10, ArcAngle = 90, RepeatCount = 2, Side = StickSide.Left };
            var segmented = new SticksSlider { Duration = 900, Angle = 10, Side = StickSide.Left };
            segmented.SetCustomSegments(new[] { 90f, -180f, 45f });

            Assert.Multiple(() =>
            {
                Assert.That(SticksAuthoredBeatmapCodec.EncodeMarker(repeated), Is.EqualTo("sticks-v1~s~l~10~900~90~2.wav"));
                Assert.That(SticksAuthoredBeatmapCodec.EncodeMarker(segmented), Is.EqualTo("sticks-v2~s~l~10~900~90_-180_45.wav"));
            });

            foreach (SticksSlider source in new[] { repeated, segmented })
            {
                Assert.That(SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(source), out SticksHitObject decoded), Is.True);
                Assert.That(((SticksSlider)decoded).HasTimedSegments, Is.False);
                assertSamePath(source, (SticksSlider)decoded);
            }
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void TestDisableReversalsLeavesTimedSameDirectionPathAndPausesUnchanged(int direction)
        {
            var source = new SticksSlider { StartTime = 1000.125, Duration = 900.123456789, Angle = 25, Side = StickSide.Left };
            source.SetTimedSegments(new[] { 0f, direction * 90f, 0f, direction * 0.25f, direction * 45f },
                new[] { 100.25, 150.125, 250.5, 200.25, 300.125 });

            SticksSlider converted = convertWithoutReversals(source);

            assertSamePath(source, converted);
            Assert.Multiple(() =>
            {
                Assert.That(converted.SegmentDurationWeights, Is.EqualTo(source.SegmentDurationWeights));
                Assert.That(SticksAuthoredBeatmapCodec.EncodeMarker(converted), Is.EqualTo(SticksAuthoredBeatmapCodec.EncodeMarker(source)));
            });
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void TestDisableReversalsChangesDirectionWithoutLosingTimedCheckpoints(int direction)
        {
            var source = new SticksSlider { StartTime = 1000.125, Duration = 900.123456789, Angle = 25, Side = StickSide.Right };
            source.SetTimedSegments(new[] { 0f, direction * 90f, 0f, direction * -0.25f, direction * -45f, 0f },
                new[] { 120.25, 180.125, 200.5, 70.25, 330.125, 100.5 });
            var expected = new SticksSlider { StartTime = source.StartTime, Duration = source.Duration, Angle = source.Angle, Side = source.Side };
            expected.SetTimedSegments(new[] { 0f, direction * 90f, 0f, direction * 0.25f, direction * 45f, 0f },
                source.SegmentDurationWeights);

            SticksSlider converted = convertWithoutReversals(source);

            assertSamePath(expected, converted);
            Assert.Multiple(() =>
            {
                Assert.That(source.SegmentEndsWithReversal(2), Is.True, "The original motion reverses when it leaves the middle pause.");
                Assert.That(Enumerable.Range(0, converted.SegmentCount - 1).Any(converted.SegmentEndsWithReversal), Is.False);
                Assert.That(converted.SegmentDurationWeights, Is.EqualTo(source.SegmentDurationWeights));
                for (int i = 0; i < source.SegmentCount; i++)
                {
                    Assert.That(converted.SegmentStartTimeAt(i), Is.EqualTo(source.SegmentStartTimeAt(i)).Within(1e-9));
                    Assert.That(converted.SegmentDurationAt(i), Is.EqualTo(source.SegmentDurationAt(i)).Within(1e-9));
                }
            });
        }

        [Test]
        public void TestVerifierAcceptsTimedDwellsAndTinyTurns()
        {
            var beatmap = new Beatmap<SticksHitObject>();
            beatmap.HitObjects.Add(createTimedSlider());

            Assert.That(new SticksBeatmapVerifier().Run(new BeatmapVerifierContext(beatmap, null!)), Is.Empty);
        }

        [Test]
        public void TestVerifierRejectsAnUnderflowedActualSegmentDuration()
        {
            var slider = new SticksSlider { Duration = 1000, Side = StickSide.Left };
            slider.SetTimedSegments(new[] { 90f, 0f }, new[] { 1.0, 3.0 });
            slider.Duration = double.Epsilon;
            var beatmap = new Beatmap<SticksHitObject>();
            beatmap.HitObjects.Add(slider);

            string[] messages = new SticksBeatmapVerifier().Run(new BeatmapVerifierContext(beatmap, null!)).Select(issue => issue.ToString()).ToArray();

            Assert.That(messages, Does.Contain("The slider path contains invalid timing or angle data."));
        }

        private static SticksSlider createTimedSlider()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000.125,
                Duration = 900.123456789,
                Angle = 359.1234567f,
                Side = StickSide.Right,
            };
            slider.SetTimedSegments(new[] { 90.123456f, 0f, -0.12345678f, -150.34567f, 45.76543f },
                new[] { 137.12345678, 213.23456789, 54.3456789, 376.4567891, 119.0 });
            return slider;
        }

        private static HitObject markerObject(string filename) => new SticksFlick
        {
            StartTime = 1000,
            Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo(filename, 100) },
        };

        private static SticksSlider convertWithoutReversals(SticksSlider source)
        {
            var beatmap = new Beatmap<HitObject>();
            beatmap.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(source));
            var converter = new SticksBeatmapConverter(beatmap, new SticksRuleset());
            new SticksModDifficultyAdjust { DisableReversals = { Value = true } }.ApplyToBeatmapConverter(converter);
            return (SticksSlider)converter.Convert().HitObjects.Single();
        }

        private static void assertSamePath(SticksSlider expected, SticksSlider actual)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.HasTimedSegments, Is.EqualTo(expected.HasTimedSegments));
                Assert.That(actual.StartTime, Is.EqualTo(expected.StartTime));
                Assert.That(actual.Duration, Is.EqualTo(expected.Duration));
                Assert.That(actual.Angle, Is.EqualTo(expected.Angle));
                Assert.That(actual.Side, Is.EqualTo(expected.Side));
                Assert.That(actual.SegmentArcAngles, Is.EqualTo(expected.SegmentArcAngles));

                for (int i = 0; i < expected.SegmentCount; i++)
                {
                    Assert.That(actual.SegmentStartTimeAt(i), Is.EqualTo(expected.SegmentStartTimeAt(i)).Within(1e-9));
                    Assert.That(actual.SegmentDurationAt(i), Is.EqualTo(expected.SegmentDurationAt(i)).Within(1e-9));

                    foreach (double progress in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
                    {
                        double time = expected.SegmentStartTimeAt(i) + progress * expected.SegmentDurationAt(i);
                        Assert.That(actual.AngleAt(time), Is.EqualTo(expected.AngleAt(time)).Within(1e-5));
                    }
                }
            });
        }
    }
}
