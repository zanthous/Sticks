using System.IO;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    /// <summary>
    /// Verifies that authored Sticks beatmaps retain legacy timing rows which are interpreted
    /// correctly by lazer's stock decoder.
    /// </summary>
    [TestFixture]
    public class SticksTimingImportExportTest
    {
        private const string multi_timing_export = """
                                                           osu file format v14

                                                           [General]
                                                           AudioFilename: timing-validation.ogg
                                                           AudioLeadIn: 0
                                                           PreviewTime: -1
                                                           Countdown: 0
                                                           SampleSet: Normal
                                                           StackLeniency: 0.7
                                                           Mode: 0

                                                           [Editor]
                                                           BeatDivisor: 8
                                                           GridSize: 4
                                                           TimelineZoom: 1

                                                           [Metadata]
                                                           Title:Timing import validation
                                                           Artist:Sticks
                                                           Creator:Zanthous
                                                           Version:Timing round trip

                                                           [Difficulty]
                                                           HPDrainRate:5
                                                           CircleSize:5
                                                           OverallDifficulty:5
                                                           ApproachRate:5
                                                           SliderMultiplier:1.4
                                                           SliderTickRate:1

                                                           [Events]

                                                           [TimingPoints]
                                                           -250.5,600.25,3,1,2,70,1,8
                                                           -250.5,-50,4,3,1,35,0,1
                                                           1000.125,500.5,4,2,0,80,1,0
                                                           1000.125,-80,4,2,2,45,0,9
                                                           2000,NaN,4,2,0,80,0,0
                                                           4500.75,333.333333333333,7,3,4,100,1,1

                                                           [HitObjects]
                                                           416,192,5000,1,0,0:0:0:100:sticks-v1~f~l~0.wav
                                                           """;

        [Test]
        public void TestImportedTimingRowsDecodeInStockLazer()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(multi_timing_export));
            using var reader = new LineBufferedReader(stream);
            var beatmap = new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader);
            var controlPoints = (LegacyControlPointInfo)beatmap.ControlPointInfo;

            Assert.Multiple(() =>
            {
                Assert.That(beatmap.BeatmapInfo.Ruleset.OnlineID, Is.Zero);
                Assert.That(beatmap.BeatmapInfo.BeatDivisor, Is.EqualTo(8));
                Assert.That(controlPoints.TimingPoints, Has.Count.EqualTo(3));

                assertTimingPoint(controlPoints.TimingPoints[0], -250.5, 600.25, 3, true);
                assertTimingPoint(controlPoints.TimingPoints[1], 1000.125, 500.5, 4, false);
                assertTimingPoint(controlPoints.TimingPoints[2], 4500.75, 333.333333333333, 7, false);
            });

            // Inherited rows at the exact timestamp of their red line must win for gameplay.
            Assert.Multiple(() =>
            {
                Assert.That(controlPoints.DifficultyPointAt(-250.5).SliderVelocity, Is.EqualTo(2).Within(0.000001));
                Assert.That(controlPoints.DifficultyPointAt(1000.125).SliderVelocity, Is.EqualTo(1.25).Within(0.000001));
                Assert.That(controlPoints.DifficultyPointAt(2000).GenerateTicks, Is.False);
                Assert.That(controlPoints.DifficultyPointAt(4500.75).SliderVelocity, Is.EqualTo(1).Within(0.000001));

                Assert.That(controlPoints.SamplePointAt(-250.5).SampleBank, Is.EqualTo(HitSampleInfo.BANK_DRUM));
                Assert.That(controlPoints.SamplePointAt(-250.5).SampleVolume, Is.EqualTo(35));
                Assert.That(controlPoints.SamplePointAt(1000.125).SampleBank, Is.EqualTo(HitSampleInfo.BANK_SOFT));
                Assert.That(controlPoints.SamplePointAt(1000.125).SampleVolume, Is.EqualTo(45));
                Assert.That(controlPoints.SamplePointAt(4500.75).SampleBank, Is.EqualTo(HitSampleInfo.BANK_DRUM));
                Assert.That(controlPoints.SamplePointAt(4500.75).SampleVolume, Is.EqualTo(100));

                Assert.That(controlPoints.EffectPointAt(-250.5).KiaiMode, Is.True);
                Assert.That(controlPoints.EffectPointAt(1000.125).KiaiMode, Is.True);
                Assert.That(controlPoints.EffectPointAt(2000).KiaiMode, Is.False);
                Assert.That(controlPoints.EffectPointAt(4500.75).KiaiMode, Is.True);
            });
        }

        [Test]
        public void TestTimingSectionChangesAtExpectedBoundaries()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(multi_timing_export));
            using var reader = new LineBufferedReader(stream);
            var controlPoints = new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader).ControlPointInfo;

            Assert.Multiple(() =>
            {
                Assert.That(controlPoints.TimingPointAt(-1000).Time, Is.EqualTo(-250.5));
                Assert.That(controlPoints.TimingPointAt(1000.124).BeatLength, Is.EqualTo(600.25));
                Assert.That(controlPoints.TimingPointAt(1000.125).BeatLength, Is.EqualTo(500.5));
                Assert.That(controlPoints.TimingPointAt(4500.749).BeatLength, Is.EqualTo(500.5));
                Assert.That(controlPoints.TimingPointAt(4500.75).BeatLength, Is.EqualTo(333.333333333333).Within(0.000000000001));
            });
        }

        private static void assertTimingPoint(TimingControlPoint point, double time, double beatLength, int numerator, bool omitFirstBarLine)
        {
            Assert.That(point.Time, Is.EqualTo(time));
            Assert.That(point.BeatLength, Is.EqualTo(beatLength).Within(0.000000000001));
            Assert.That(point.TimeSignature.Numerator, Is.EqualTo(numerator));
            Assert.That(point.OmitFirstBarLine, Is.EqualTo(omitFirstBarLine));
        }
    }
}
