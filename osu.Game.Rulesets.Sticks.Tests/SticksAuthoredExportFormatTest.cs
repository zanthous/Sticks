using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps.Formats;
using osu.Game.Beatmaps.Timing;
using osu.Game.IO;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksAuthoredExportFormatTest
    {
        private const string exported_beatmap = """
                                                        osu file format v14

                                                        [General]
                                                        AudioFilename: validation.wav
                                                        AudioLeadIn: 0
                                                        PreviewTime: -1
                                                        Countdown: 0
                                                        SampleSet: Normal
                                                        StackLeniency: 0.7
                                                        Mode: 0

                                                        [Editor]
                                                        BeatDivisor: 4
                                                        GridSize: 4
                                                        TimelineZoom: 1

                                                        [Metadata]
                                                        Title:Authored export validation
                                                        TitleUnicode:Authored export validation
                                                        Artist:Sticks
                                                        ArtistUnicode:Sticks
                                                        Creator:Zanthous
                                                        Version:Round trip
                                                        Source:
                                                        Tags:sticks-v1

                                                        [Difficulty]
                                                        HPDrainRate:5
                                                        CircleSize:5
                                                        OverallDifficulty:5
                                                        ApproachRate:5
                                                        SliderMultiplier:1.4
                                                        SliderTickRate:1

                                                        [Events]

                                                        [TimingPoints]
                                                        0,500,4,2,0,100,1,0

                                                        [HitObjects]
                                                        416,192,1000.125,1,0,0:0:0:100:sticks-v1~f~l~359.875.wav
                                                        151,192,1000.125,1,0,0:0:0:100:sticks-v1~f~r~180.wav
                                                        256,192,2000,8,0,2750.5,0:0:0:100:sticks-v1~h~l~90.25~750.5.wav
                                                        256,192,3000,8,0,4250.25,0:0:0:100:sticks-v1~s~r~270~1250.25~-135.5~2.wav
                                                        """;

        [Test]
        public void TestGeneratedOsuTextDecodesAndConvertsLosslessly()
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(exported_beatmap));
            using var reader = new LineBufferedReader(stream);
            var source = new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader);

            Assert.That(source.HitObjects, Has.All.Matches<HitObject>(hitObject =>
                hitObject.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>().Any(sample => sample.Filename.StartsWith(SticksAuthoredBeatmapCodec.MARKER_PREFIX))));

            Assert.Multiple(() =>
            {
                Assert.That(source.HitObjects[0], Is.Not.InstanceOf<IHasDuration>());
                Assert.That(source.HitObjects[1], Is.Not.InstanceOf<IHasDuration>());
                Assert.That(source.HitObjects[2], Is.InstanceOf<IHasDuration>());
                Assert.That(((IHasDuration)source.HitObjects[2]).EndTime, Is.EqualTo(2750.5));
                Assert.That(source.HitObjects[3], Is.InstanceOf<IHasDuration>());
                Assert.That(((IHasDuration)source.HitObjects[3]).EndTime, Is.EqualTo(4250.25));
            });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(source.BeatmapInfo.Ruleset.OnlineID, Is.Zero);
                Assert.That(source.Metadata.AudioFile, Is.EqualTo("validation.wav"));
                Assert.That(source.Metadata.Title, Is.EqualTo("Authored export validation"));
                Assert.That(source.ControlPointInfo.TimingPointAt(1000).BeatLength, Is.EqualTo(500));

                Assert.That(converted, Has.Length.EqualTo(4));

                Assert.That(converted[0], Is.TypeOf<SticksFlick>());
                Assert.That(converted[0].StartTime, Is.EqualTo(1000.125));
                Assert.That(converted[0].Side, Is.EqualTo(StickSide.Left));
                Assert.That(converted[0].Angle, Is.EqualTo(359.875f));
                Assert.That(((SticksFlick)converted[0]).SyncedNoteSide, Is.EqualTo(StickSide.Right));
                Assert.That(((SticksFlick)converted[0]).SyncedNoteAngle, Is.EqualTo(180));

                Assert.That(converted[1], Is.TypeOf<SticksFlick>());
                Assert.That(converted[1].Side, Is.EqualTo(StickSide.Right));
                Assert.That(converted[1].Angle, Is.EqualTo(180));

                Assert.That(converted[2], Is.TypeOf<SticksHold>());
                Assert.That(converted[2].Side, Is.EqualTo(StickSide.Left));
                Assert.That(converted[2].Angle, Is.EqualTo(90.25f));
                Assert.That(((SticksHold)converted[2]).Duration, Is.EqualTo(750.5));

                Assert.That(converted[3], Is.TypeOf<SticksSlider>());
                Assert.That(converted[3].Side, Is.EqualTo(StickSide.Right));
                Assert.That(converted[3].Angle, Is.EqualTo(270));
                Assert.That(((SticksSlider)converted[3]).Duration, Is.EqualTo(1250.25));
                Assert.That(((SticksSlider)converted[3]).ArcAngle, Is.EqualTo(-135.5f));
                Assert.That(((SticksSlider)converted[3]).RepeatCount, Is.EqualTo(2));
                Assert.That(converted, Has.All.Matches<SticksHitObject>(hitObject => hitObject.Samples.Any(sample => sample.Name == "hitnormal")));
            });
        }

        [Test]
        public void TestPackageExportStripsDecoderRecognisedOnlineIdsWithoutReencodingCarrierData()
        {
            const string storedCarrier = "osu file format v128\r\n[Metadata]\r\u00a0BeatmapID\u00a0:\u00a097531\rBeatmapSetID\u2003:\u200324680\n"
                                         + "Title:Authored carrier\r\n[HitObjects]\r416,192,1000,1,0,0:0:0:100:sticks-v1~f~l~0.wav";
            const string expectedCarrier = "osu file format v128\r\n[Metadata]\rTitle:Authored carrier\r\n[HitObjects]\r"
                                           + "416,192,1000,1,0,0:0:0:100:sticks-v1~f~l~0.wav";

            byte[] preamble = Encoding.UTF8.GetPreamble();
            byte[] storedBytes = preamble.Concat(Encoding.UTF8.GetBytes(storedCarrier)).ToArray();
            byte[] expectedBytes = preamble.Concat(Encoding.UTF8.GetBytes(expectedCarrier)).ToArray();
            byte[] packagedBytes = SticksBeatmapPackageExporter.StripOnlineIds(storedBytes);

            Assert.Multiple(() =>
            {
                Assert.That(packagedBytes, Is.EqualTo(expectedBytes));
                Assert.That(packagedBytes.Take(preamble.Length), Is.EqualTo(preamble));
                Assert.That(Encoding.UTF8.GetString(packagedBytes), Does.Contain("Title:Authored carrier"));
                Assert.That(Encoding.UTF8.GetString(packagedBytes), Does.Contain("sticks-v1~f~l~0.wav"));
            });
        }

        [Test]
        public void TestPackageExportReturnsOriginalBytesWhenNoOnlineIdsExist()
        {
            byte[] storedBytes = Encoding.UTF8.GetBytes("osu file format v128\r\n[Metadata]\r\nTitle:Unlinked carrier\r\n");

            byte[] packagedBytes = SticksBeatmapPackageExporter.StripOnlineIds(storedBytes);

            Assert.That(packagedBytes, Is.SameAs(storedBytes));
        }

        [Test]
        public void TestPackageExportRetainsBomWhenFirstLineIsOnlineId()
        {
            byte[] preamble = Encoding.UTF8.GetPreamble();
            byte[] storedBytes = preamble.Concat(Encoding.UTF8.GetBytes("BeatmapID:97531\r\nTitle:Carrier\r\n")).ToArray();
            byte[] expectedBytes = preamble.Concat(Encoding.UTF8.GetBytes("Title:Carrier\r\n")).ToArray();

            byte[] packagedBytes = SticksBeatmapPackageExporter.StripOnlineIds(storedBytes);

            Assert.That(packagedBytes, Is.EqualTo(expectedBytes));
        }

        [Test]
        public void TestPackageExportDoesNotStripMetadataKeysTheDecoderWouldIgnore()
        {
            byte[] storedBytes = Encoding.UTF8.GetBytes("[Metadata]\nbeatmapID:97531\nBeatmapIdentifier:24680\n");

            byte[] packagedBytes = SticksBeatmapPackageExporter.StripOnlineIds(storedBytes);

            Assert.That(packagedBytes, Is.SameAs(storedBytes));
        }
    }
}
