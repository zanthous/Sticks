// Copyright (c) Zanthous. Licensed under the MIT Licence.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.IO.Serialization;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksLegacyEditorBridgeTest
    {
        [Test]
        public void TestBootstrapTypeIsOnlyDiscoverableRulesetAndRealmIdentityStaysCustom()
        {
            var bootstrap = new SticksRuleset();
            Type[] discoverable = typeof(SticksRuleset).Assembly.GetTypes()
                                                              .Where(type => type.IsPublic && type.IsSubclassOf(typeof(Ruleset)))
                                                              .ToArray();
            Type instantiationType = Type.GetType(bootstrap.RulesetInfo.InstantiationInfo, throwOnError: true)!;
            Ruleset instantiated = bootstrap.RulesetInfo.CreateInstance();

            Assert.Multiple(() =>
            {
                Assert.That(discoverable, Is.EqualTo(new[] { typeof(SticksRuleset) }));
                Assert.That(bootstrap, Is.Not.InstanceOf<ILegacyRuleset>());
                Assert.That(bootstrap.RulesetInfo.ShortName, Is.EqualTo("sticks"));
                Assert.That(bootstrap.RulesetInfo.OnlineID, Is.EqualTo(-1));
                Assert.That(instantiationType, Is.EqualTo(typeof(SticksRuleset.EditorCompatibleSticksRuleset)));
                Assert.That(instantiationType.IsPublic, Is.False);
                Assert.That(instantiationType.IsNestedPublic, Is.True);
                Assert.That(instantiated, Is.InstanceOf<ILegacyRuleset>());
                Assert.That(instantiated.RulesetInfo.ShortName, Is.EqualTo("sticks"));
                Assert.That(instantiated.RulesetInfo.OnlineID, Is.EqualTo(-1));
                Assert.That(((ILegacyRuleset)instantiated).LegacyID, Is.Zero);
            });
        }

        [Test]
        public void TestGameplayConversionNeverActivatesModeZeroIdentity()
        {
            var ruleset = new SticksRuleset();
            var source = new Beatmap
            {
                BeatmapInfo = new BeatmapInfo(new RulesetInfo
                {
                    Available = true,
                    OnlineID = 0,
                    ShortName = "osu",
                    Name = "osu!",
                }),
            };
            source.HitObjects.Add(new TestPositionHitObject { StartTime = 1000 });

            IBeatmap converted = ruleset.CreateBeatmapConverter(source).Convert();
            IBeatmapProcessor? processor = ruleset.CreateBeatmapProcessor(converted);

            Assert.Multiple(() =>
            {
                Assert.That(processor, Is.Null);
                Assert.That(source.BeatmapInfo.Ruleset.OnlineID, Is.Zero);
                Assert.That(converted.BeatmapInfo.Ruleset.ShortName, Is.EqualTo("sticks"));
                Assert.That(converted.BeatmapInfo.Ruleset.OnlineID, Is.EqualTo(-1));
                Assert.That(ruleset.RulesetInfo.OnlineID, Is.EqualTo(-1));
                Assert.That(converted.HitObjects.Single().Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>(), Is.Empty);
            });
        }

        [Test]
        public void TestEditorActivationEncodesAndRoundTripsModeZeroCarriers()
        {
            SticksRuleset ruleset = new SticksRuleset();
            RulesetInfo persistedRuleset = ruleset.RulesetInfo.Clone();
            Beatmap<SticksHitObject> playable = createAuthoredBeatmap(persistedRuleset.Clone());
            var persistedInfo = new BeatmapInfo(persistedRuleset)
            {
                DifficultyName = "Authored Sticks",
            };

            var editorBeatmap = new EditorBeatmap(playable, beatmapInfo: persistedInfo);

            Assert.Multiple(() =>
            {
                Assert.That(persistedRuleset.OnlineID, Is.EqualTo(-1), "The Realm-facing ruleset object must not be mutated.");
                Assert.That(playable.BeatmapInfo.Ruleset.OnlineID, Is.EqualTo(-1), "Gameplay metadata must remain custom.");
                Assert.That(editorBeatmap.BeatmapInfo.Ruleset.ShortName, Is.EqualTo("sticks"));
                Assert.That(editorBeatmap.BeatmapInfo.Ruleset.OnlineID, Is.Zero);
                Assert.That(editorBeatmap.BeatmapInfo.Ruleset, Is.Not.SameAs(persistedRuleset));
            });

            assertSingleMarkerNormalSample(editorBeatmap.HitObjects[0]);
            assertSingleMarkerNormalSample(editorBeatmap.HitObjects[1]);
            assertSingleMarkerNormalSample(editorBeatmap.HitObjects[2]);

            string encoded = encode(editorBeatmap);
            Assert.Multiple(() =>
            {
                Assert.That(encoded, Does.Contain("Mode: 0"));
                Assert.That(encoded, Does.Contain("sticks-v1~f~l~45.wav"));
                Assert.That(encoded, Does.Contain("sticks-v1~h~r~225~750.wav"));
                Assert.That(encoded, Does.Contain("sticks-v1~s~l~90~1000~180~1.wav"));
            });

            IBeatmap decodedCarrier = decode(encoded);
            SticksHitObject[] roundTripped = new SticksBeatmapConverter(decodedCarrier, ruleset)
                                                   .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(decodedCarrier.HitObjects[0], Is.Not.InstanceOf<IHasDuration>());
                Assert.That(((IHasDuration)decodedCarrier.HitObjects[1]).Duration, Is.EqualTo(750));
                Assert.That(((IHasDuration)decodedCarrier.HitObjects[2]).Duration, Is.EqualTo(1000));
                Assert.That(roundTripped.Select(hitObject => hitObject.GetType()), Is.EqualTo(new[]
                {
                    typeof(SticksFlick),
                    typeof(SticksHold),
                    typeof(SticksSlider),
                }));
                Assert.That(((SticksSlider)roundTripped[2]).ArcAngle, Is.EqualTo(180));
                Assert.That(((SticksSlider)roundTripped[2]).RepeatCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void TestMarkerTracksObjectEditsAndSampleReplacementWithoutDoubleNormal()
        {
            SticksRuleset ruleset = new SticksRuleset();
            RulesetInfo persistedRuleset = ruleset.RulesetInfo.Clone();
            Beatmap<SticksHitObject> playable = createAuthoredBeatmap(persistedRuleset.Clone());
            var editorBeatmap = new EditorBeatmap(playable, beatmapInfo: new BeatmapInfo(persistedRuleset));
            var slider = (SticksSlider)editorBeatmap.HitObjects[2];

            var stableSamples = slider.Samples;
            IBeatmapProcessor stableProcessor = SticksLegacyEditorBridge.TryCreateProcessor(editorBeatmap)!;
            stableProcessor.PreProcess();
            Assert.That(slider.Samples, Is.SameAs(stableSamples), "An already-current marker should not rebuild the samples collection.");

            slider.Side = StickSide.Right;
            slider.Angle = 123.456f;
            slider.Duration = 1333.25;
            slider.ArcAngle = -270.5f;
            slider.RepeatCount = 3;

            assertSingleMarkerNormalSample(slider);
            Assert.That(markerFilename(slider), Is.EqualTo("sticks-v1~s~r~123.456~1333.25~-270.5~3.wav"));

            // Simulate the stock sample editor replacing all samples. The editor beatmap processor
            // must repair the carrier marker before the change handler serialises the state.
            slider.Samples = new HitSampleInfo[]
            {
                new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 72),
                new HitSampleInfo(HitSampleInfo.HIT_CLAP),
            };
            IBeatmapProcessor processor = SticksLegacyEditorBridge.TryCreateProcessor(editorBeatmap)!;
            processor.PreProcess();

            assertSingleMarkerNormalSample(slider);
            Assert.Multiple(() =>
            {
                Assert.That(slider.Samples, Has.Exactly(1).Matches<HitSampleInfo>(sample => sample.Name == HitSampleInfo.HIT_CLAP));
                Assert.That(slider.Samples.Single(sample => sample.Name == HitSampleInfo.HIT_NORMAL).Volume, Is.EqualTo(72));
                Assert.That(markerFilename(slider), Is.EqualTo("sticks-v1~s~r~123.456~1333.25~-270.5~3.wav"));
                Assert.That(encode(editorBeatmap), Does.Contain("sticks-v1~s~r~123.456~1333.25~-270.5~3.wav"));
            });
        }

        [Test]
        public void TestStockChangeHandlerUndoAndRedoRoundTripsNativeObjects()
        {
            SticksRuleset ruleset = new SticksRuleset();
            RulesetInfo persistedRuleset = ruleset.RulesetInfo.Clone();
            Beatmap<SticksHitObject> playable = createAuthoredBeatmap(persistedRuleset.Clone());
            var editorBeatmap = new EditorBeatmap(playable, beatmapInfo: new BeatmapInfo(persistedRuleset));
            var changeHandler = new BeatmapEditorChangeHandler(editorBeatmap);
            string initialHash = changeHandler.CurrentStateHash;
            var originalSlider = (SticksSlider)editorBeatmap.HitObjects[2];

            editorBeatmap.SelectedHitObjects.Add(originalSlider);
            editorBeatmap.PerformOnSelection(hitObject =>
            {
                var slider = (SticksSlider)hitObject;
                slider.Side = StickSide.Right;
                slider.Angle = 137.5f;
                slider.Duration = 1625;
                slider.ArcAngle = -315;
                slider.RepeatCount = 2;
            });

            string editedHash = changeHandler.CurrentStateHash;
            Assert.Multiple(() =>
            {
                Assert.That(editedHash, Is.Not.EqualTo(initialHash));
                Assert.That(changeHandler.CanUndo.Value, Is.True);
                Assert.That(changeHandler.CanRedo.Value, Is.False);
                Assert.That(markerFilename(originalSlider), Is.EqualTo("sticks-v1~s~r~137.5~1625~-315~2.wav"));
            });

            changeHandler.RestoreState(-1);
            var undoneSlider = (SticksSlider)editorBeatmap.HitObjects[2];
            Assert.Multiple(() =>
            {
                Assert.That(undoneSlider, Is.Not.SameAs(originalSlider));
                Assert.That(undoneSlider.Side, Is.EqualTo(StickSide.Left));
                Assert.That(undoneSlider.Angle, Is.EqualTo(90));
                Assert.That(undoneSlider.Duration, Is.EqualTo(1000));
                Assert.That(undoneSlider.ArcAngle, Is.EqualTo(180));
                Assert.That(undoneSlider.RepeatCount, Is.EqualTo(1));
                Assert.That(changeHandler.CanRedo.Value, Is.True);
            });

            changeHandler.RestoreState(1);
            var redoneSlider = (SticksSlider)editorBeatmap.HitObjects[2];
            Assert.Multiple(() =>
            {
                Assert.That(redoneSlider.Side, Is.EqualTo(StickSide.Right));
                Assert.That(redoneSlider.Angle, Is.EqualTo(137.5f));
                Assert.That(redoneSlider.Duration, Is.EqualTo(1625));
                Assert.That(redoneSlider.ArcAngle, Is.EqualTo(-315));
                Assert.That(redoneSlider.RepeatCount, Is.EqualTo(2));
                Assert.That(changeHandler.CanUndo.Value, Is.True);
                Assert.That(changeHandler.CanRedo.Value, Is.False);
                assertSingleMarkerNormalSample(redoneSlider);
            });
        }

        [Test]
        public void TestCarrierProjectionIsExcludedFromClipboardJson()
        {
            var flick = new SticksFlick
            {
                Side = StickSide.Right,
                Angle = 271.25f,
            };
            flick.EnsureLegacyEditorMarker();

            var clipboard = new ClipboardContent
            {
                HitObjects = new HitObject[] { flick },
            };
            string json = clipboard.Serialize();
            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("\"side\""));
                Assert.That(json, Does.Contain("\"angle\""));
                Assert.That(json, Does.Not.Contain("\"position\""));
                Assert.That(json, Does.Not.Contain("\"x\""));
                Assert.That(json, Does.Not.Contain("\"y\""));
                Assert.That(json, Does.Contain("sticks-v1~f~r~271.25.wav"));
            });

            SticksFlick roundTripped = (SticksFlick)json.Deserialize<ClipboardContent>().HitObjects.Single();
            // ClipboardContent preserves the authored object type and canonical fields. Samples
            // are deserialised through their base type, then repaired by the editor processor.
            SticksRuleset ruleset = new SticksRuleset();
            RulesetInfo persistedRuleset = ruleset.RulesetInfo.Clone();
            var playable = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(persistedRuleset.Clone()),
            };
            var editorBeatmap = new EditorBeatmap(playable, beatmapInfo: new BeatmapInfo(persistedRuleset));
            editorBeatmap.Add(roundTripped);
            Assert.Multiple(() =>
            {
                Assert.That(roundTripped.Side, Is.EqualTo(StickSide.Right));
                Assert.That(roundTripped.Angle, Is.EqualTo(271.25f));
                Assert.That(markerFilename(roundTripped), Is.EqualTo("sticks-v1~f~r~271.25.wav"));
            });
        }

        [Test]
        public void TestEditorCarrierMarkerDoesNotReplaceSliderTickSound()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1500,
                Side = StickSide.Left,
                Angle = 0,
                ArcAngle = 180,
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 64) },
            };
            slider.EnsureLegacyEditorMarker();

            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 500 });
            slider.ApplyDefaults(controlPoints, new BeatmapDifficulty { SliderTickRate = 1 });

            SticksSliderTick[] ticks = slider.NestedHitObjects.OfType<SticksSliderTick>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(ticks, Has.Length.EqualTo(2));
                Assert.That(ticks.SelectMany(tick => tick.Samples).All(sample => sample.Name == "slidertick"), Is.True);
                Assert.That(ticks.SelectMany(tick => tick.Samples).OfType<ConvertHitObjectParser.FileHitSampleInfo>(), Is.Empty);
                Assert.That(ticks.SelectMany(tick => tick.Samples).All(sample => sample.Volume == 64), Is.True);
            });
        }

        private static Beatmap<SticksHitObject> createAuthoredBeatmap(RulesetInfo ruleset)
        {
            var beatmap = new Beatmap<SticksHitObject>
            {
                BeatmapInfo = new BeatmapInfo(ruleset),
            };
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.HitObjects.Add(new SticksFlick
            {
                StartTime = 1000,
                Side = StickSide.Left,
                Angle = 45,
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) },
            });
            beatmap.HitObjects.Add(new SticksHold
            {
                StartTime = 1500,
                Duration = 750,
                Side = StickSide.Right,
                Angle = 225,
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) },
            });
            beatmap.HitObjects.Add(new SticksSlider
            {
                StartTime = 2500,
                Duration = 1000,
                Side = StickSide.Left,
                Angle = 90,
                ArcAngle = 180,
                RepeatCount = 1,
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL) },
            });
            return beatmap;
        }

        private static string encode(IBeatmap beatmap)
        {
            using var writer = new StringWriter();
            new LegacyBeatmapEncoder(beatmap, null, null).Encode(writer);
            return writer.ToString();
        }

        private static IBeatmap decode(string encoded)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(encoded));
            using var reader = new LineBufferedReader(stream);
            return new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader);
        }

        private static void assertSingleMarkerNormalSample(HitObject hitObject)
        {
            Assert.Multiple(() =>
            {
                Assert.That(hitObject.Samples.Count(sample => sample.Name == HitSampleInfo.HIT_NORMAL), Is.EqualTo(1));
                Assert.That(hitObject.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>().Count(SticksAuthoredBeatmapCodec.IsMarker), Is.EqualTo(1));
            });
        }

        private static string markerFilename(HitObject hitObject) =>
            hitObject.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>().Single(SticksAuthoredBeatmapCodec.IsMarker).Filename;

        private sealed class TestPositionHitObject : HitObject, IHasPosition
        {
            public osuTK.Vector2 Position { get; set; } = new osuTK.Vector2(256, 192);

            public float X
            {
                get => Position.X;
                set => Position = new osuTK.Vector2(value, Position.Y);
            }

            public float Y
            {
                get => Position.Y;
                set => Position = new osuTK.Vector2(Position.X, value);
            }
        }
    }
}
