// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public partial class SticksBeatmapConverterTest
    {
        [Test]
        public void TestConvertsFlicksAndSliderWithCoordinatedSticks()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1000, Position = new Vector2(512, 192) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1000, Position = new Vector2(256, 384) });
            source.HitObjects.Add(new TestDurationHitObject { StartTime = 2000, Duration = 2000, Position = new Vector2(0, 192) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 2500, Position = new Vector2(256, 0) });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            var slider = (SticksSlider)converted[2];
            slider.ApplyDefaults(source.ControlPointInfo, source.Difficulty);
            var tail = slider.NestedHitObjects.OfType<SticksSliderTail>().Single();
            SticksSliderTick[] ticks = slider.NestedHitObjects.OfType<SticksSliderTick>().ToArray();
            var drawableSlider = new TestDrawableSticksSlider(slider);
            drawableSlider.Apply(slider);

            Assert.Multiple(() =>
            {
                Assert.That(converted, Has.Length.EqualTo(4));
                Assert.That(converted.Take(2), Has.All.TypeOf<SticksFlick>());
                Assert.That(converted[0].Side, Is.Not.EqualTo(converted[1].Side));
                Assert.That(slider.Duration, Is.EqualTo(2000));
                Assert.That(System.Math.Abs(slider.ArcAngle), Is.GreaterThanOrEqualTo(30));
                Assert.That(slider.TickInterval, Is.EqualTo(500));
                Assert.That(ticks.Select(tick => tick.StartTime), Is.EqualTo(new[] { 2500, 3000, 3500 }));
                Assert.That(ticks, Has.All.Matches<SticksSliderTick>(tick => tick.Side == slider.Side));
                Assert.That(ticks, Has.All.Matches<SticksSliderTick>(tick => tick.Samples.Single().Name == "slidertick"));
                Assert.That(tail.StartTime, Is.EqualTo(slider.EndTime));
                Assert.That(tail.SliderStartTime, Is.EqualTo(slider.StartTime));
                Assert.That(tail.PreemptDuration, Is.EqualTo(slider.Duration + tail.ApproachDuration));
                Assert.That(tail.Side, Is.EqualTo(slider.Side));
                Assert.That(tail.Angle, Is.EqualTo(slider.AngleAt(slider.EndTime)).Within(0.001));
                Assert.That(drawableSlider.AttachedNestedObjects, Is.EqualTo(5));
                Assert.That(converted[3].Side, Is.Not.EqualTo(slider.Side));
                Assert.That(converted, Has.All.Matches<SticksHitObject>(hitObject => hitObject.Samples.Count > 0));
            });
        }

        [Test]
        public void TestNormalisesConvertedHitsounds()
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(new TestPositionedHitObject
            {
                StartTime = 1000,
                Position = new Vector2(512, 192),
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 27) },
            });

            SticksHitObject converted = (SticksHitObject)new SticksBeatmapConverter(source, new SticksRuleset()).Convert().HitObjects.Single();

            Assert.Multiple(() =>
            {
                Assert.That(converted.Samples.Select(sample => sample.Name), Is.EqualTo(new[] { HitSampleInfo.HIT_NORMAL }));
                Assert.That(converted.Samples.Single().Volume, Is.EqualTo(100));
            });
        }

        [Test]
        public void TestNormalisesConvertedSliderNodeHitsounds()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            var sourceSlider = new TestRepeatDurationHitObject
            {
                StartTime = 1000,
                Duration = 1500,
                RepeatCount = 1,
                Position = new Vector2(512, 192),
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 24) },
            };
            sourceSlider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 31) });
            sourceSlider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 42) });
            sourceSlider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 53) });
            source.HitObjects.Add(sourceSlider);

            var converted = (SticksSlider)new SticksBeatmapConverter(source, new SticksRuleset()).Convert().HitObjects.Single();
            converted.ApplyDefaults(source.ControlPointInfo, source.Difficulty);

            HitObject[] audibleNested = converted.NestedHitObjects
                                                  .Where(hitObject => hitObject is SticksSliderTick or SticksSliderRepeat or SticksSliderTail)
                                                  .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted.Samples.Select(sample => sample.Name), Is.EqualTo(new[] { HitSampleInfo.HIT_NORMAL }));
                Assert.That(converted.Samples.Single().Volume, Is.EqualTo(100));
                Assert.That(converted.NodeSamples, Is.Empty);
                Assert.That(audibleNested.SelectMany(hitObject => hitObject.Samples), Is.Not.Empty);
                Assert.That(audibleNested.SelectMany(hitObject => hitObject.Samples),
                    Has.All.Matches<HitSampleInfo>(sample =>
                        (sample.Name == HitSampleInfo.HIT_NORMAL || sample.Name == "slidertick") && sample.Volume == 100));
            });
        }

        [Test]
        public void TestAuthoredObjectsRoundTripThroughLegacyProxyMarkers()
        {
            SticksHitObject[] authored =
            {
                new SticksFlick { StartTime = 1000, Side = StickSide.Left, Angle = 15 },
                new SticksHold { StartTime = 2000, Duration = 750, Side = StickSide.Right, Angle = 120 },
                new SticksSlider { StartTime = 3000, Duration = 1250, Side = StickSide.Left, Angle = 300, ArcAngle = -135, RepeatCount = 2 },
            };

            SticksHitObject[] decoded = authored.Select(SticksAuthoredBeatmapCodec.CreateLegacyProxy)
                                                .Select(proxy =>
                                                {
                                                    Assert.That(SticksAuthoredBeatmapCodec.TryDecode(proxy, out SticksHitObject result), Is.True);
                                                    return result;
                                                })
                                                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(decoded[0], Is.TypeOf<SticksFlick>());
                Assert.That(decoded[0].StartTime, Is.EqualTo(1000));
                Assert.That(decoded[0].Side, Is.EqualTo(StickSide.Left));
                Assert.That(decoded[0].Angle, Is.EqualTo(15));

                var hold = (SticksHold)decoded[1];
                Assert.That(hold.Duration, Is.EqualTo(750));
                Assert.That(hold.Side, Is.EqualTo(StickSide.Right));
                Assert.That(hold.Angle, Is.EqualTo(120));

                var slider = (SticksSlider)decoded[2];
                Assert.That(slider.Duration, Is.EqualTo(1250));
                Assert.That(slider.ArcAngle, Is.EqualTo(-135));
                Assert.That(slider.RepeatCount, Is.EqualTo(2));
            });
        }

        [Test]
        public void TestSegmentedSliderRoundTripsThroughLegacyProxyMarker()
        {
            var authored = new SticksSlider
            {
                StartTime = 3000,
                Duration = 3500,
                Side = StickSide.Left,
                Angle = 10,
            };
            authored.SetCustomSegments(new[] { 90f, -180f, 45f });

            HitObject proxy = SticksAuthoredBeatmapCodec.CreateLegacyProxy(authored);
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(proxy, out SticksHitObject decodedObject), Is.True);
            var decoded = (SticksSlider)decodedObject;

            Assert.Multiple(() =>
            {
                Assert.That(proxy.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>().Single().Filename,
                    Is.EqualTo("sticks-v2~s~l~10~3500~90_-180_45.wav"));
                Assert.That(decoded.HasCustomSegments, Is.True);
                Assert.That(decoded.SegmentArcAngles, Is.EqualTo(new[] { 90f, -180f, 45f }));
                Assert.That(decoded.Duration, Is.EqualTo(3500));
                Assert.That(decoded.SegmentDurationAt(0), Is.EqualTo(1000).Within(0.001));
                Assert.That(decoded.SegmentDurationAt(1), Is.EqualTo(2000).Within(0.001));
                Assert.That(decoded.SegmentDurationAt(2), Is.EqualTo(500).Within(0.001));
            });
        }

        [Test]
        public void TestAuthoredChordConvertsExactlyAndReceivesLink()
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(new SticksFlick
            {
                StartTime = 1000,
                Side = StickSide.Left,
                Angle = 0,
            }));
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(new SticksFlick
            {
                StartTime = 1000,
                Side = StickSide.Right,
                Angle = 135,
            }));

            SticksFlick[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                      .Convert().HitObjects.OfType<SticksFlick>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted, Has.Length.EqualTo(2));
                Assert.That(converted[0].Angle, Is.EqualTo(0));
                Assert.That(converted[1].Angle, Is.EqualTo(135));
                Assert.That(converted[0].SyncedNoteSide, Is.EqualTo(StickSide.Right));
                Assert.That(converted[0].SyncedNoteAngle, Is.EqualTo(135));
            });
        }

        [Test]
        public void TestAuthoredSliderAndHoldChordsReceiveDrawableLinks()
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(new SticksSlider
            {
                StartTime = 1000,
                Duration = 1000,
                Side = StickSide.Left,
                Angle = 0,
                ArcAngle = 90,
            }));
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(new SticksFlick
            {
                StartTime = 1000,
                Side = StickSide.Right,
                Angle = 135,
            }));
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(new SticksHold
            {
                StartTime = 3000,
                Duration = 1000,
                Side = StickSide.Left,
                Angle = 45,
            }));
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(new SticksFlick
            {
                StartTime = 3000,
                Side = StickSide.Right,
                Angle = 225,
            }));

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            SticksSlider slider = converted.OfType<SticksSlider>().Single();
            SticksHold hold = converted.OfType<SticksHold>().Single();
            var drawableSlider = new DrawableSticksSlider(slider);
            var drawableHold = new DrawableSticksHold(hold);

            Assert.Multiple(() =>
            {
                Assert.That(slider.SyncedNoteSide, Is.EqualTo(StickSide.Right));
                Assert.That(slider.SyncedNoteAngle, Is.EqualTo(135));
                Assert.That(hold.SyncedNoteSide, Is.EqualTo(StickSide.Right));
                Assert.That(hold.SyncedNoteAngle, Is.EqualTo(225));
                Assert.That(typeof(DrawableSticksSlider).GetField("syncedNoteLink", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawableSlider),
                    Is.TypeOf<SticksSyncedNoteLink>());
                Assert.That(typeof(DrawableSticksHold).GetField("syncedNoteLink", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawableHold),
                    Is.TypeOf<SticksSyncedNoteLink>());
            });
        }

        [Test]
        public void TestAuthoredMarkerSurvivesLegacyBeatmapDecode()
        {
            const string beatmapText = """
                                       osu file format v14

                                       [General]
                                       AudioFilename: audio.mp3
                                       Mode: 0

                                       [Metadata]
                                       Title:Authored marker test
                                       Artist:Test
                                       Creator:Test
                                       Version:Test

                                       [Difficulty]
                                       HPDrainRate:5
                                       CircleSize:4
                                       OverallDifficulty:5
                                       ApproachRate:5
                                       SliderMultiplier:1.4
                                       SliderTickRate:1

                                       [TimingPoints]
                                       0,500,4,2,0,100,1,0

                                       [HitObjects]
                                       256,352,1234,1,0,0:0:0:80:sticks-v1~s~l~270~1250.5~-135.25~2.wav
                                       """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(beatmapText));
            using var reader = new LineBufferedReader(stream);
            Beatmap decodedBeatmap = new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader);
            var converted = (SticksSlider)new SticksBeatmapConverter(decodedBeatmap, new SticksRuleset())
                                           .Convert().HitObjects.Single();

            Assert.Multiple(() =>
            {
                Assert.That(converted.StartTime, Is.EqualTo(1234));
                Assert.That(converted.Side, Is.EqualTo(StickSide.Left));
                Assert.That(converted.Angle, Is.EqualTo(270));
                Assert.That(converted.Duration, Is.EqualTo(1250.5));
                Assert.That(converted.ArcAngle, Is.EqualTo(-135.25));
                Assert.That(converted.RepeatCount, Is.EqualTo(2));
                Assert.That(converted.Samples, Has.Count.EqualTo(1));
                Assert.That(converted.Samples[0].Name, Is.EqualTo(HitSampleInfo.HIT_NORMAL));
                Assert.That(converted.Samples[0].Volume, Is.EqualTo(80));
            });
        }

        [Test]
        public void TestOfficialTutorialConversionRemainsBeginnerDifficulty()
        {
            const string beatmapText = """
                                       osu file format v14

                                       [General]
                                       AudioFilename: audio.mp3
                                       Mode: 0

                                       [Metadata]
                                       Title:new beginnings
                                       Artist:nekodex
                                       Creator:pishifat
                                       Version:tutorial

                                       [Difficulty]
                                       HPDrainRate:0
                                       CircleSize:2
                                       OverallDifficulty:0
                                       ApproachRate:2
                                       SliderMultiplier:0.7
                                       SliderTickRate:1

                                       [TimingPoints]
                                       -28,461.538461538462,4,1,0,100,1,0

                                       [HitObjects]
                                       255,184,3664,69,4,3:1:0:0:
                                       95,66,20279,5,4,0:0:0:0:
                                       415,66,22125,1,4,0:3:0:0:
                                       399,194,23048,1,2,1:3:0:0:
                                       415,322,23971,1,4,0:1:0:0:
                                       95,322,25818,1,4,0:3:0:0:
                                       255,34,27202,5,2,0:3:0:0:
                                       255,34,27664,1,4,0:0:0:0:
                                       255,322,29510,1,4,0:3:0:0:
                                       367,258,30433,1,2,0:3:0:0:
                                       479,194,31356,1,4,0:0:0:0:
                                       257,62,33202,1,4,0:3:0:0:
                                       145,126,34125,1,2,0:3:0:0:
                                       34,191,35048,1,4,0:2:0:0:
                                       333,159,57202,6,0,P|256:40|175:157,1,335.999989746094,4|4,0:0|0:2,3:0:0:0:
                                       175,159,60894,2,4,P|159:241|175:338,1,167.999994873047,4|2,0:1|0:3,3:2:0:0:
                                       338,324,62741,2,4,P|350:242|334:160,2,167.999994873047,4|2|4,1:2|1:3|3:2,0:2:0:0:
                                       256,192,75664,12,0,79356,3:3:0:0:
                                       467,109,101510,38,0,B|381:142|277:94|346:61|243:12|155:51,1,335.999989746094,4|4,0:0|0:3,3:0:0:0:
                                       36,191,104279,1,4,0:3:0:0:
                                       63,279,104741,1,2,0:3:0:0:
                                       94,365,105202,2,0,P|167:369|248:309,1,167.999994873047,4|2,0:0|0:3,3:0:0:0:
                                       136,166,107048,2,0,P|126:84|168:13,1,167.999994873047,4|2,0:3|0:3,3:0:0:0:
                                       260,24,108433,1,2,0:3:0:0:
                                       351,36,108894,6,0,L|394:373,1,335.999989746094,4|4,0:0|0:3,3:0:0:0:
                                       211,337,111664,1,6,0:3:0:0:
                                       28,306,112587,2,0,L|9:125,2,167.999994873047,4|2|4,0:3|0:3|0:3,3:0:0:0:
                                       211,337,115356,2,0,L|305:355,1,83.9999974365235,2|2,0:3|0:3,3:0:0:0:
                                       384,337,116279,6,0,B|404:253|336:153|316:226|250:134|273:40,1,335.999989746094,4|4,0:0|0:3,3:0:0:0:
                                       456,15,119048,1,2,0:3:0:0:
                                       476,197,119972,2,0,B|404:244|287:215|349:171|239:140|159:194,1,335.999989746094,4|4,0:0|0:3,3:0:0:0:
                                       40,336,122741,1,2,0:3:0:0:
                                       221,370,123664,6,0,P|152:187|27:138,1,335.999989746094,4|4,0:1|0:3,3:0:0:0:
                                       256,192,126433,12,4,129202,3:2:0:0:
                                       """;

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(beatmapText));
            using var reader = new LineBufferedReader(stream);
            Beatmap source = new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader);
            IBeatmap converted = new SticksBeatmapConverter(source, new SticksRuleset()).Convert();
            SticksDifficultyBreakdown difficulty = SticksDifficultyCalculator.CalculateDifficulty(
                converted.HitObjects.Cast<SticksHitObject>(),
                overallDifficulty: converted.Difficulty.OverallDifficulty);
            string sliderSummary = string.Join(", ", converted.HitObjects.OfType<SticksSlider>()
                .Select(slider => $"{slider.Duration:0}ms/{slider.TotalAngularDistance / (slider.Duration / 1000):0}deg-s"));

            Assert.That(difficulty.StarRating, Is.LessThan(2), $"The official tutorial difficulty was {difficulty}. Sliders: {sliderSummary}");
        }

        [Test]
        public void TestDifficultyAdjustCanDisableAuthoredReversals()
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(new SticksSlider
            {
                StartTime = 1000,
                Duration = 3000,
                RepeatCount = 2,
                Side = StickSide.Right,
                Angle = 45,
                ArcAngle = -90,
            }));

            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            var mod = new SticksModDifficultyAdjust { DisableReversals = { Value = true } };
            mod.ApplyToBeatmapConverter(converter);
            var converted = (SticksSlider)converter.Convert().HitObjects.Single();

            Assert.Multiple(() =>
            {
                Assert.That(converted.RepeatCount, Is.Zero);
                Assert.That(converted.ArcAngle, Is.EqualTo(-270));
            });
        }

        [TestCase("sticks-v1~f~l~1e40.wav")]
        [TestCase("sticks-v1~s~r~0~1000~1e40~0.wav")]
        public void TestAuthoredMarkerRejectsValuesOutsideFloatRange(string marker)
        {
            var source = new TestPositionedHitObject
            {
                Samples = new[] { new osu.Game.Rulesets.Objects.Legacy.ConvertHitObjectParser.FileHitSampleInfo(marker, 100) },
            };

            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(source, out _), Is.False);
        }

        [Test]
        public void TestFutureAuthoredMarkerFailsClosed()
        {
            var source = new Beatmap<HitObject>();
            var hitObject = new TestPositionedHitObject
            {
                StartTime = 1000,
                Position = new Vector2(416, 192),
                Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo("sticks-v3~f~l~0.wav", 100) },
            };
            source.HitObjects.Add(hitObject);

            SticksAuthoredBeatmapCodec.MarkerInspection inspection = SticksAuthoredBeatmapCodec.InspectMarker(hitObject);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());

            Assert.Multiple(() =>
            {
                Assert.That(inspection.Status, Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.UnsupportedVersion));
                Assert.That(inspection.Version, Is.EqualTo(3));
                Assert.That(converter.CanConvert(), Is.False);
                Assert.That(converter.AuthoredCarrierError, Does.Contain("unsupported marker version v3"));
                Assert.That(() => converter.Convert(), Throws.TypeOf<BeatmapInvalidForRulesetException>()
                                                          .With.Message.Contains("Update Sticks"));
            });
        }

        [Test]
        public void TestOverflowedAuthoredMarkerVersionFailsClosed()
        {
            var source = new Beatmap<HitObject>();
            var hitObject = new TestPositionedHitObject
            {
                StartTime = 1000,
                Position = new Vector2(416, 192),
                Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo("sticks-v999999999999999999999~f~l~0.wav", 100) },
            };
            source.HitObjects.Add(hitObject);

            SticksAuthoredBeatmapCodec.MarkerInspection inspection = SticksAuthoredBeatmapCodec.InspectMarker(hitObject);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());

            Assert.Multiple(() =>
            {
                Assert.That(inspection.Status, Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.UnsupportedVersion));
                Assert.That(inspection.Version, Is.Null);
                Assert.That(converter.CanConvert(), Is.False);
                Assert.That(converter.AuthoredCarrierError, Does.Contain("unsupported marker version vunknown"));
            });
        }

        [Test]
        public void TestMalformedSupportedAuthoredMarkerFailsClosed()
        {
            var source = new Beatmap<HitObject>();
            var hitObject = new TestPositionedHitObject
            {
                StartTime = 1000,
                Position = new Vector2(416, 192),
                Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo("sticks-v1~f~x~0.wav", 100) },
            };
            source.HitObjects.Add(hitObject);

            SticksAuthoredBeatmapCodec.MarkerInspection inspection = SticksAuthoredBeatmapCodec.InspectMarker(hitObject);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());

            Assert.Multiple(() =>
            {
                Assert.That(inspection.Status, Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.MalformedSupported));
                Assert.That(converter.CanConvert(), Is.False);
                Assert.That(converter.AuthoredCarrierError, Does.Contain("malformed v1 marker"));
                Assert.That(() => converter.Convert(), Throws.TypeOf<BeatmapInvalidForRulesetException>());
            });
        }

        [TestCase("sticks-vx~f~l~0.wav")]
        [TestCase("sticks-v~f~l~0.wav")]
        public void TestMalformedReservedNamespaceWithoutNumericVersionFailsClosed(string marker)
        {
            var source = new Beatmap<HitObject>();
            var hitObject = new TestPositionedHitObject
            {
                StartTime = 1000,
                Position = new Vector2(416, 192),
                Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo(marker, 100) },
            };
            source.HitObjects.Add(hitObject);

            SticksAuthoredBeatmapCodec.MarkerInspection inspection = SticksAuthoredBeatmapCodec.InspectMarker(hitObject);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());

            Assert.Multiple(() =>
            {
                Assert.That(inspection.Status, Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.MalformedSupported));
                Assert.That(inspection.Version, Is.Null);
                Assert.That(converter.CanConvert(), Is.False);
                Assert.That(converter.AuthoredCarrierError, Does.Contain("malformed marker"));
            });
        }

        [TestCase("sticks-video.wav")]
        [TestCase("samples/sticks-video.wav")]
        [TestCase(@"samples\sticks-video.wav")]
        public void TestOrdinarySampleSharingMarkerPrefixDoesNotReserveNamespace(string sampleFilename)
        {
            var source = new Beatmap<HitObject>();
            var hitObject = new TestPositionedHitObject
            {
                StartTime = 1000,
                Position = new Vector2(416, 192),
                Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo(sampleFilename, 100) },
            };
            source.HitObjects.Add(hitObject);

            SticksAuthoredBeatmapCodec.MarkerInspection inspection = SticksAuthoredBeatmapCodec.InspectMarker(hitObject);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());

            Assert.Multiple(() =>
            {
                Assert.That(SticksAuthoredBeatmapCodec.IsMarker(hitObject.Samples.Single()), Is.False);
                Assert.That(inspection.Status, Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.None));
                Assert.That(converter.CanConvert(), Is.True);
                Assert.That(converter.AuthoredCarrierError, Is.Null);
            });
        }

        [TestCase("samples/sticks-v1~f~l~90.wav")]
        [TestCase(@"samples\sticks-v1~f~l~90.wav")]
        public void TestAuthoredMarkerPathsSupportEitherDirectorySeparator(string marker)
        {
            var hitObject = new TestPositionedHitObject
            {
                StartTime = 1000,
                Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo(marker, 100) },
            };

            bool decoded = SticksAuthoredBeatmapCodec.TryDecode(hitObject, out var result);

            Assert.Multiple(() =>
            {
                Assert.That(decoded, Is.True);
                Assert.That(result, Is.TypeOf<SticksFlick>());
                Assert.That(result!.Side, Is.EqualTo(StickSide.Left));
                Assert.That(result.Angle, Is.EqualTo(90));
            });
        }

        [TestCase("samples/sticks-v3~f~l~0.wav")]
        [TestCase(@"samples\sticks-v3~f~l~0.wav")]
        public void TestFutureMarkerPathsFailClosedWithEitherDirectorySeparator(string marker)
        {
            var hitObject = new TestPositionedHitObject
            {
                StartTime = 1000,
                Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo(marker, 100) },
            };

            SticksAuthoredBeatmapCodec.MarkerInspection inspection = SticksAuthoredBeatmapCodec.InspectMarker(hitObject);

            Assert.Multiple(() =>
            {
                Assert.That(SticksAuthoredBeatmapCodec.IsMarker(hitObject.Samples.Single()), Is.True);
                Assert.That(inspection.Status, Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.UnsupportedVersion));
                Assert.That(inspection.Version, Is.EqualTo(3));
            });
        }

        [Test]
        public void TestDuplicateAuthoredMarkersFailClosed()
        {
            var source = new Beatmap<HitObject>();
            var hitObject = new TestPositionedHitObject
            {
                StartTime = 1000,
                Position = new Vector2(416, 192),
                Samples = new HitSampleInfo[]
                {
                    new ConvertHitObjectParser.FileHitSampleInfo("sticks-v1~f~l~0.wav", 100),
                    new ConvertHitObjectParser.FileHitSampleInfo("sticks-v1~f~r~180.wav", 100),
                },
            };
            source.HitObjects.Add(hitObject);

            SticksAuthoredBeatmapCodec.MarkerInspection inspection = SticksAuthoredBeatmapCodec.InspectMarker(hitObject);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());

            Assert.Multiple(() =>
            {
                Assert.That(inspection.MarkerCount, Is.EqualTo(2));
                Assert.That(converter.CanConvert(), Is.False);
                Assert.That(converter.AuthoredCarrierError, Does.Contain("has 2 Sticks markers"));
            });
        }

        [Test]
        public void TestMixedAuthoredAndUnmarkedObjectsFailClosed()
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(new SticksFlick
            {
                StartTime = 1000,
                Side = StickSide.Left,
                Angle = 0,
            }));
            source.HitObjects.Add(new TestPositionedHitObject
            {
                StartTime = 1500,
                Position = new Vector2(96, 192),
            });

            var converter = new SticksBeatmapConverter(source, new SticksRuleset());

            Assert.Multiple(() =>
            {
                Assert.That(converter.CanConvert(), Is.False);
                Assert.That(converter.AuthoredCarrierError, Does.Contain("object 2 at 1500ms has no Sticks marker"));
                Assert.That(() => converter.Convert(), Throws.TypeOf<BeatmapInvalidForRulesetException>());
            });
        }

        [Test]
        public void TestAllValidV1AndV2AuthoredMarkersConvertExactly()
        {
            var segmentedSlider = new SticksSlider
            {
                StartTime = 2000,
                Duration = 3000,
                Side = StickSide.Right,
                Angle = 45,
            };
            segmentedSlider.SetCustomSegments(new[] { 90f, -180f, 45f });

            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(new SticksFlick
            {
                StartTime = 1000,
                Side = StickSide.Left,
                Angle = 15,
            }));
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(segmentedSlider));

            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            SticksHitObject[] converted = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converter.CanConvert(), Is.True);
                Assert.That(converter.AuthoredCarrierError, Is.Null);
                Assert.That(converted, Has.Length.EqualTo(2));
                Assert.That(converted[0], Is.TypeOf<SticksFlick>());
                Assert.That(converted[0].Side, Is.EqualTo(StickSide.Left));
                Assert.That(converted[0].Angle, Is.EqualTo(15));
                Assert.That(converted[1], Is.TypeOf<SticksSlider>());
                Assert.That(((SticksSlider)converted[1]).SegmentArcAngles, Is.EqualTo(new[] { 90f, -180f, 45f }));
            });
        }

        [TestCase("sticks-v1~s~l~45~1000~180~16.wav")]
        [TestCase("sticks-v1~s~l~45~1000~0~0.wav")]
        [TestCase("sticks-v1~s~l~45~1000~0.999~0.wav")]
        [TestCase("sticks-v2~s~l~45~1000~90_90_90_90_90_90_90_90_90_90_90_90_90_90_90_90_90.wav")]
        public void TestAuthoredMarkerRejectsInvalidOrSilentlyClampedValues(string marker)
        {
            var source = new Beatmap<HitObject>();
            var hitObject = new TestPositionedHitObject
            {
                StartTime = 1000,
                Position = new Vector2(416, 192),
                Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo(marker, 100) },
            };
            source.HitObjects.Add(hitObject);

            SticksAuthoredBeatmapCodec.MarkerInspection inspection = SticksAuthoredBeatmapCodec.InspectMarker(hitObject);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());

            Assert.Multiple(() =>
            {
                Assert.That(inspection.Status, Is.EqualTo(SticksAuthoredBeatmapCodec.MarkerStatus.MalformedSupported));
                Assert.That(SticksAuthoredBeatmapCodec.TryDecode(hitObject, out _), Is.False);
                Assert.That(converter.CanConvert(), Is.False);
            });
        }

        [Test]
        public void TestOrdinaryStandardMapStillProcedurallyConverts()
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(new TestPositionedHitObject
            {
                StartTime = 1000,
                Position = new Vector2(512, 192),
                Samples = new[] { new ConvertHitObjectParser.FileHitSampleInfo("ordinary-custom-sample.wav", 80) },
            });

            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            var converted = (SticksFlick)converter.Convert().HitObjects.Single();

            Assert.Multiple(() =>
            {
                Assert.That(converter.CanConvert(), Is.True);
                Assert.That(converter.AuthoredCarrierError, Is.Null);
                Assert.That(converted.Angle, Is.EqualTo(0).Within(0.001));
                Assert.That(converted.Samples.Single().Name, Is.EqualTo(HitSampleInfo.HIT_NORMAL));
            });
        }

        [Test]
        public void TestConvertsSourceHoldDurationToDirectionalHold()
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(new TestHoldDurationHitObject
            {
                StartTime = 1000,
                Duration = 1500,
                Position = new Vector2(512, 192),
            });

            var hold = (SticksHold)new SticksBeatmapConverter(source, new SticksRuleset()).Convert().HitObjects.Single();

            Assert.Multiple(() =>
            {
                Assert.That(hold.Duration, Is.EqualTo(1500));
                Assert.That(hold.Angle, Is.EqualTo(0).Within(0.001));
                Assert.That(() => new DrawableSticksHold(hold), Throws.Nothing);
            });
        }

        [Test]
        public void TestStandardPositionsMapToCircleAngles()
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1000, Position = new Vector2(512, 192) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 2000, Position = new Vector2(256, 384) });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted[0].Angle, Is.EqualTo(0).Within(0.001));
                Assert.That(converted[1].Angle, Is.EqualTo(90).Within(0.001));
            });
        }

        [Test]
        public void TestRapidFlicksAlternateSticks()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1000, Position = new Vector2(512, 192) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1125, Position = new Vector2(400, 300) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1250, Position = new Vector2(256, 384) });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted[1].Side, Is.Not.EqualTo(converted[0].Side));
                Assert.That(converted[2].Side, Is.Not.EqualTo(converted[1].Side));
            });
        }

        [Test]
        public void TestRapidFlicksAlternateEvenWhenHalfBeatIsShorterThanThreshold()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 250 });

            for (int i = 0; i < 3; i++)
            {
                source.HitObjects.Add(new TestPositionedHitObject
                {
                    StartTime = 1000 + i * 250,
                    Position = new Vector2(512, 192),
                });
            }

            SticksFlick[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                      .Convert().HitObjects.OfType<SticksFlick>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(SticksBeatmapConverter.RAPID_ALTERNATION_THRESHOLD, Is.GreaterThanOrEqualTo(250));
                Assert.That(converted.Zip(converted.Skip(1)), Has.All.Matches<(SticksFlick First, SticksFlick Second)>(pair => pair.First.Side != pair.Second.Side));
            });
        }

        [Test]
        public void TestRapidFlickIntoSliderUsesOppositeStick()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestPositionedHitObject
            {
                StartTime = 1000,
                Position = new Vector2(512, 192),
            });
            source.HitObjects.Add(new TestDurationHitObject
            {
                StartTime = 1250,
                Duration = 1000,
                Position = new Vector2(512, 192),
            });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(SticksBeatmapConverter.RAPID_ALTERNATION_THRESHOLD,
                    Is.EqualTo(260), "Physical alternation spacing must remain independent of broad miss windows.");
                Assert.That(converted, Has.Length.EqualTo(2));
                Assert.That(converted[0], Is.TypeOf<SticksFlick>());
                Assert.That(converted[1], Is.TypeOf<SticksSlider>());
                Assert.That(converted[0].Side, Is.Not.EqualTo(converted[1].Side));
            });
        }

        [Test]
        public void TestExactTimestampNeverReusesAStick()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

            for (int i = 0; i < 4; i++)
                source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1000, Position = new Vector2(256 + i * 40, 192) });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted, Has.Length.EqualTo(2));
                Assert.That(converted.Select(hitObject => hitObject.Side).Distinct().Count(), Is.EqualTo(2));
            });
        }

        [Test]
        public void TestFlickAtSliderTailUsesOtherStick()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestDurationHitObject
            {
                StartTime = 1000,
                Duration = 1000,
                Position = new Vector2(512, 192),
            });
            source.HitObjects.Add(new TestPositionedHitObject
            {
                StartTime = 2000,
                Position = new Vector2(256, 384),
            });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted, Has.Length.EqualTo(2));
                Assert.That(converted[0], Is.TypeOf<SticksSlider>());
                Assert.That(converted[1], Is.TypeOf<SticksFlick>());
                Assert.That(converted[1].Side, Is.Not.EqualTo(converted[0].Side));
            });
        }

        [Test]
        public void TestFlickApproachNeverAppearsUnderSameStickSlider()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestDurationHitObject
            {
                StartTime = 1000,
                Duration = 1000,
                Position = new Vector2(512, 192),
            });
            source.HitObjects.Add(new TestPositionedHitObject
            {
                StartTime = 1500,
                Position = new Vector2(256, 0),
            });
            source.HitObjects.Add(new TestPositionedHitObject
            {
                StartTime = 2500,
                Position = new Vector2(0, 192),
            });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            SticksSlider slider = converted.OfType<SticksSlider>().Single();
            SticksFlick[] approachingFlicks = converted.OfType<SticksFlick>()
                                                       .Where(flick => flick.StartTime == 2500)
                                                       .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(approachingFlicks, Is.Not.Empty);
                Assert.That(approachingFlicks, Has.All.Matches<SticksFlick>(flick => flick.Side != slider.Side));
                Assert.That(2500 - SticksBeatmapConverter.VISIBILITY_PREEMPT, Is.LessThan(slider.EndTime));
            });
        }

        [Test]
        public void TestVisualReservationsOnBothSticksDoNotDeletePlayableNote()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestDurationHitObject
            {
                StartTime = 1000,
                Duration = 500,
                Position = new Vector2(512, 192),
            });
            source.HitObjects.Add(new TestDurationHitObject
            {
                StartTime = 1600,
                Duration = 500,
                Position = new Vector2(256, 0),
            });
            source.HitObjects.Add(new TestPositionedHitObject
            {
                StartTime = 2200,
                Position = new Vector2(0, 192),
            });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted, Has.Length.EqualTo(3));
                Assert.That(converted.OfType<SticksFlick>().Single().StartTime, Is.EqualTo(2200));
            });
        }

        [Test]
        public void TestRapidSameStickReflickIsRemovedWhenOtherStickIsOccupied()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestDurationHitObject
            {
                StartTime = 1000,
                Duration = 1500,
                Position = new Vector2(512, 192),
            });
            source.HitObjects.Add(new TestPositionedHitObject
            {
                StartTime = 1500,
                Position = new Vector2(256, 0),
            });
            source.HitObjects.Add(new TestPositionedHitObject
            {
                StartTime = 1750,
                Position = new Vector2(256, 0),
            });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            SticksSlider slider = converted.OfType<SticksSlider>().Single();
            SticksFlick[] flicks = converted.OfType<SticksFlick>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(flicks, Has.Length.EqualTo(1));
                Assert.That(flicks, Has.All.Matches<SticksFlick>(flick => flick.Side != slider.Side));
            });
        }

        [Test]
        public void TestCoordinatedDoublePatterns()
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1000, Position = new Vector2(512, 192) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1000, Position = new Vector2(256, 0) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 2000, Position = new Vector2(256, 384) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 2000, Position = new Vector2(0, 192) });

            SticksFlick[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                      .Convert().HitObjects.OfType<SticksFlick>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted[0].Side, Is.Not.EqualTo(converted[1].Side));
                Assert.That(System.Math.Abs(SticksHitObject.DeltaAngle(converted[0].Angle, converted[1].Angle)), Is.EqualTo(180).Within(0.001));
                Assert.That(converted[2].Side, Is.Not.EqualTo(converted[3].Side));
                Assert.That(System.Math.Abs(SticksHitObject.DeltaAngle(converted[2].Angle, converted[3].Angle)), Is.EqualTo(90).Within(0.001));
            });
        }

        [Test]
        public void TestConverterAddsPlayableSyncedChordStreaksAtStrongMusicalPoints()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

            for (int i = 0; i < 40; i++)
            {
                source.HitObjects.Add(new TestPositionedHitObject
                {
                    StartTime = 1000 + i * 500,
                    Position = new Vector2(256 + (i % 4 - 2) * 80, 192),
                });
            }

            SticksFlick[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                      .Convert().HitObjects.OfType<SticksFlick>().ToArray();
            SticksFlick[] linkOwners = converted.Where(flick => flick.SyncedNoteSide.HasValue).ToArray();
            IGrouping<double, SticksFlick>[] generatedChords = converted.GroupBy(flick => flick.StartTime)
                                                                        .Where(group => group.Count() == 2)
                                                                        .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted.Length, Is.GreaterThan(40));
                Assert.That(generatedChords.Length, Is.InRange(8, 15));
                Assert.That(linkOwners, Has.Length.EqualTo(generatedChords.Length));
                Assert.That(generatedChords, Has.All.Matches<IGrouping<double, SticksFlick>>(group => group.Select(flick => flick.Side).Distinct().Count() == 2));
                Assert.That(linkOwners.Select(owner => System.Math.Abs(SticksHitObject.DeltaAngle(owner.Angle, owner.SyncedNoteAngle))).Distinct().Count(), Is.GreaterThan(1));
                Assert.That(generatedChords.Zip(generatedChords.Skip(1), (first, second) => second.Key - first.Key), Has.Some.EqualTo(500));
            });
        }

        [Test]
        public void TestConverterCreatesHoldSectionWithOtherStickAccompaniment()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestDurationHitObject { StartTime = 1000, Duration = 2000, Position = new Vector2(512, 192) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1500, Position = new Vector2(256, 0) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 2000, Position = new Vector2(0, 192) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 2500, Position = new Vector2(256, 384) });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            SticksHold hold = converted.OfType<SticksHold>().Single();
            SticksFlick[] accompaniment = converted.OfType<SticksFlick>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(hold.Duration, Is.EqualTo(2000));
                Assert.That(accompaniment, Has.Length.EqualTo(3));
                Assert.That(accompaniment, Has.All.Matches<SticksFlick>(flick => flick.Side != hold.Side));
                Assert.That(accompaniment.Zip(accompaniment.Skip(1), (first, second) => second.StartTime - first.StartTime), Has.All.EqualTo(500));
            });
        }

        [Test]
        public void TestConverterBuildsHoldPhraseFromOrdinaryNotes()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

            for (int i = 0; i < 12; i++)
            {
                source.HitObjects.Add(new TestPositionedHitObject
                {
                    StartTime = 2000 + i * 500,
                    Position = new Vector2(256 + (i % 3 - 1) * 100, 192),
                });
            }

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            SticksHold hold = converted.OfType<SticksHold>().First();
            SticksFlick[] accompaniment = converted.OfType<SticksFlick>()
                                                   .Where(flick => flick.StartTime > hold.StartTime && flick.StartTime <= hold.EndTime)
                                                   .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(hold.Duration, Is.InRange(1500, 2000));
                Assert.That(accompaniment.Length, Is.GreaterThanOrEqualTo(3));
                Assert.That(accompaniment, Has.All.Matches<SticksFlick>(flick => flick.Side != hold.Side));
            });
        }

        [Test]
        public void TestContinuousSliderCreatesLoopExtensionNotation()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 3000,
                Side = StickSide.Left,
                Angle = 0,
                ArcAngle = 810,
            };
            slider.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());

            SticksSliderExtension[] extensions = slider.NestedHitObjects.OfType<SticksSliderExtension>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(extensions, Has.Length.EqualTo(2));
                Assert.That(extensions.Select(extension => extension.StartTime), Is.EqualTo(new[] { 2333.333333333333, 3666.666666666666 }).Within(0.001));
                Assert.That(extensions, Has.All.Matches<SticksSliderExtension>(extension => extension.Direction == 1));
                Assert.That(extensions, Has.All.Matches<SticksSliderExtension>(extension => extension.Side == StickSide.Left));
            });
        }

        [Test]
        public void TestRareSliderAccompanimentFollowsArc()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestDurationHitObject { StartTime = 1000, Duration = 2000, Position = new Vector2(512, 192) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 1500, Position = new Vector2(0, 192) });
            source.HitObjects.Add(new TestPositionedHitObject { StartTime = 2000, Position = new Vector2(256, 0) });

            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                          .Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            var slider = (SticksSlider)converted[0];
            SticksFlick[] taps = converted.OfType<SticksFlick>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(taps, Has.Length.EqualTo(2));
                Assert.That(taps, Has.All.Matches<SticksFlick>(tap => tap.Side != slider.Side));
                Assert.That(SticksHitObject.DeltaAngle(taps[0].Angle, slider.AngleAt(taps[0].StartTime)), Is.EqualTo(0).Within(0.001));
                Assert.That(SticksHitObject.DeltaAngle(taps[1].Angle, slider.AngleAt(taps[1].StartTime)), Is.EqualTo(0).Within(0.001));
            });
        }

        [Test]
        public void TestRareStreamAlternatesWithinSmallArc()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

            for (int i = 0; i < 6; i++)
            {
                source.HitObjects.Add(new TestPositionedHitObject
                {
                    StartTime = 1000 + i * 125,
                    Position = i % 2 == 0 ? new Vector2(512, 192) : new Vector2(0, 192),
                });
            }

            SticksFlick[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
                                      .Convert().HitObjects.Cast<SticksFlick>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted, Has.Length.EqualTo(6));
                Assert.That(converted.Zip(converted.Skip(1)), Has.All.Matches<(SticksFlick First, SticksFlick Second)>(pair => pair.First.Side != pair.Second.Side));
                Assert.That(System.Math.Abs(SticksHitObject.DeltaAngle(converted[0].Angle, converted[^1].Angle)), Is.EqualTo(30).Within(0.001));
                Assert.That(converted, Has.All.Matches<SticksFlick>(flick => System.Math.Abs(SticksHitObject.DeltaAngle(converted[0].Angle, flick.Angle)) <= 30.001));
            });
        }

        [Test]
        public void TestPreservesChainedSliderReversals()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            var sourceSlider = new TestRepeatDurationHitObject
            {
                StartTime = 1000,
                Duration = 3000,
                RepeatCount = 2,
                Position = new Vector2(512, 192),
            };
            sourceSlider.NodeSamples.Add(new List<HitSampleInfo>());
            sourceSlider.NodeSamples.Add(new List<HitSampleInfo>());
            sourceSlider.NodeSamples.Add(new List<HitSampleInfo>());
            sourceSlider.NodeSamples.Add(new List<HitSampleInfo>());
            source.HitObjects.Add(sourceSlider);

            var slider = (SticksSlider)new SticksBeatmapConverter(source, new SticksRuleset()).Convert().HitObjects.Single();
            slider.ApplyDefaults(source.ControlPointInfo, source.Difficulty);
            SticksSliderRepeat[] repeats = slider.NestedHitObjects.OfType<SticksSliderRepeat>().ToArray();
            SticksSliderTick[] ticks = slider.NestedHitObjects.OfType<SticksSliderTick>().ToArray();
            SticksSliderTail tail = slider.NestedHitObjects.OfType<SticksSliderTail>().Single();

            Assert.Multiple(() =>
            {
                Assert.That(slider.RepeatCount, Is.EqualTo(2));
                Assert.That(slider.SpanCount, Is.EqualTo(3));
                Assert.That(slider.SpanDuration, Is.EqualTo(1000));
                Assert.That(SticksHitObject.DeltaAngle(slider.AngleAt(1000), slider.Angle), Is.EqualTo(0).Within(0.001));
                Assert.That(SticksHitObject.DeltaAngle(slider.AngleAt(2000), slider.Angle + slider.ArcAngle), Is.EqualTo(0).Within(0.001));
                Assert.That(SticksHitObject.DeltaAngle(slider.AngleAt(3000), slider.Angle), Is.EqualTo(0).Within(0.001));
                Assert.That(SticksHitObject.DeltaAngle(slider.AngleAt(4000), slider.Angle + slider.ArcAngle), Is.EqualTo(0).Within(0.001));
                Assert.That(repeats.Select(repeat => repeat.StartTime), Is.EqualTo(new[] { 2000, 3000 }));
                Assert.That(repeats, Has.All.Matches<SticksSliderRepeat>(repeat => repeat.DisplayPreempt == slider.SpanDuration));
                Assert.That(repeats[0].DirectionAfter, Is.EqualTo(-repeats[1].DirectionAfter));
                Assert.That(repeats, Has.All.Matches<SticksSliderRepeat>(repeat => repeat.Samples.Count > 0));
                Assert.That(ticks.Select(tick => tick.StartTime), Is.EqualTo(new[] { 1500, 2500, 3500 }));
                Assert.That(tail.StartTime, Is.EqualTo(4000));
                Assert.That(SticksHitObject.DeltaAngle(tail.Angle, slider.Angle + slider.ArcAngle), Is.EqualTo(0).Within(0.001));
                Assert.That(tail.Samples, Is.Not.Empty);
            });
        }

        [Test]
        public void TestFastSliderReversalsBecomeContinuousMotion()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestRepeatDurationHitObject
            {
                StartTime = 1000,
                Duration = 300,
                RepeatCount = 2,
                Position = new Vector2(512, 192),
            });

            var slider = (SticksSlider)new SticksBeatmapConverter(source, new SticksRuleset()).Convert().HitObjects.Single();
            slider.ApplyDefaults(source.ControlPointInfo, source.Difficulty);

            Assert.Multiple(() =>
            {
                Assert.That(SticksBeatmapConverter.MAX_REVERSAL_ANGULAR_VELOCITY, Is.EqualTo(180));
                Assert.That(SticksBeatmapConverter.MIN_GENERATED_REVERSAL_SPAN_DURATION, Is.EqualTo(250));
                Assert.That(slider.RepeatCount, Is.Zero);
                Assert.That(System.Math.Abs(slider.ArcAngle), Is.EqualTo(36).Within(0.001));
                Assert.That(System.Math.Abs(slider.ArcAngle) / slider.Duration * 1000,
                    Is.LessThanOrEqualTo(SticksBeatmapConverter.MAX_GENERATED_SLIDER_ANGULAR_VELOCITY + 0.001));
                Assert.That(slider.NestedHitObjects.OfType<SticksSliderRepeat>(), Is.Empty);
            });
        }

        [Test]
        public void TestGeneratedSliderArcShortensToRespectMaximumSpeed()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestDurationHitObject
            {
                StartTime = 1000,
                Duration = 100,
                Position = new Vector2(512, 192),
            });

            var slider = (SticksSlider)new SticksBeatmapConverter(source, new SticksRuleset()).Convert().HitObjects.Single();
            double angularVelocity = System.Math.Abs(slider.ArcAngle) / slider.Duration * 1000;

            Assert.Multiple(() =>
            {
                Assert.That(slider.Duration, Is.EqualTo(100), "Conversion should preserve source timing.");
                Assert.That(SticksBeatmapConverter.MAX_GENERATED_SLIDER_ANGULAR_VELOCITY, Is.EqualTo(120));
                Assert.That(System.Math.Abs(slider.ArcAngle), Is.EqualTo(12).Within(0.001));
                Assert.That(angularVelocity, Is.LessThanOrEqualTo(SticksBeatmapConverter.MAX_GENERATED_SLIDER_ANGULAR_VELOCITY + 0.001));
            });
        }

        [Test]
        public void TestGeneratedSliderBelowSpeedCapKeepsMusicalArcLength()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestDurationHitObject
            {
                StartTime = 1000,
                Duration = 2000,
                Position = new Vector2(512, 192),
            });

            var slider = (SticksSlider)new SticksBeatmapConverter(source, new SticksRuleset()).Convert().HitObjects.Single();

            Assert.Multiple(() =>
            {
                Assert.That(System.Math.Abs(slider.ArcAngle), Is.EqualTo(180).Within(0.001));
                Assert.That(System.Math.Abs(slider.ArcAngle) / slider.Duration * 1000,
                    Is.LessThan(SticksBeatmapConverter.MAX_GENERATED_SLIDER_ANGULAR_VELOCITY));
            });
        }

        [Test]
        public void TestAuthoredSliderIsNotAffectedByGeneratedSpeedCap()
        {
            var authored = new SticksSlider
            {
                StartTime = 1000,
                Duration = 100,
                Side = StickSide.Right,
                Angle = 45,
                ArcAngle = 270,
                RepeatCount = 1,
            };
            var source = new Beatmap<HitObject>();
            source.HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(authored));

            var converted = (SticksSlider)new SticksBeatmapConverter(source, new SticksRuleset()).Convert().HitObjects.Single();

            Assert.Multiple(() =>
            {
                Assert.That(converted.Duration, Is.EqualTo(100));
                Assert.That(converted.ArcAngle, Is.EqualTo(270));
                Assert.That(converted.RepeatCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void TestDifficultyAdjustCanDisableReversals()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new TestRepeatDurationHitObject
            {
                StartTime = 1000,
                Duration = 3000,
                RepeatCount = 2,
                Position = new Vector2(512, 192),
            });

            var normal = (SticksSlider)new SticksBeatmapConverter(source, new SticksRuleset()).Convert().HitObjects.Single();
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            var mod = new SticksModDifficultyAdjust { DisableReversals = { Value = true } };
            mod.ApplyToBeatmapConverter(converter);
            var adjusted = (SticksSlider)converter.Convert().HitObjects.Single();
            adjusted.ApplyDefaults(source.ControlPointInfo, source.Difficulty);

            Assert.Multiple(() =>
            {
                Assert.That(adjusted.RepeatCount, Is.Zero);
                Assert.That(adjusted.ArcAngle, Is.EqualTo(normal.ArcAngle * 3).Within(0.001));
                Assert.That(adjusted.NestedHitObjects.OfType<SticksSliderRepeat>(), Is.Empty);
            });
        }

        private class TestPositionedHitObject : HitObject, IHasPosition
        {
            public Vector2 Position { get; set; }

            public float X
            {
                get => Position.X;
                set => Position = new Vector2(value, Y);
            }

            public float Y
            {
                get => Position.Y;
                set => Position = new Vector2(X, value);
            }
        }

        private class TestDurationHitObject : TestPositionedHitObject, IHasDuration
        {
            public double Duration { get; set; }

            public double EndTime => StartTime + Duration;
        }

        private class TestHoldDurationHitObject : TestDurationHitObject
        {
        }

        private class TestRepeatDurationHitObject : TestDurationHitObject, IHasRepeats
        {
            public int RepeatCount { get; set; }

            public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();
        }

        private partial class TestDrawableSticksSlider : DrawableSticksSlider
        {
            public int AttachedNestedObjects { get; private set; }

            public TestDrawableSticksSlider(SticksSlider hitObject)
                : base(hitObject)
            {
            }

            protected override void AddNestedHitObject(DrawableHitObject hitObject)
            {
                base.AddNestedHitObject(hitObject);
                AttachedNestedObjects++;
            }
        }
    }
}
