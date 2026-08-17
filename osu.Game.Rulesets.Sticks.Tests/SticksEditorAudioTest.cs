using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksEditorAudioTest
    {
        [Test]
        public void TestEditorMarkerIsNeverOfferedForHeadPlayback()
        {
            SticksHitObject[] objects =
            {
                new SticksFlick(),
                new SticksHold { Duration = 1000 },
                new SticksSlider { Duration = 1000, ArcAngle = 180 },
            };

            foreach (SticksHitObject hitObject in objects)
            {
                hitObject.Samples = new HitSampleInfo[]
                {
                    new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 64),
                    new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 52),
                };
                hitObject.EnsureLegacyEditorMarker();

                Assert.That(hitObject.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>(), Is.Not.Empty);

                IEnumerable<HitSampleInfo> playable = hitObject switch
                {
                    SticksFlick flick => new DrawableSticksFlick(flick).GetSamples(),
                    SticksHold hold => new DrawableSticksHold(hold).GetSamples(),
                    SticksSlider slider => new DrawableSticksSlider(slider).GetSamples(),
                    _ => throw new AssertionException("Unexpected test object."),
                };

                assertMarkerFree(playable, HitSampleInfo.HIT_NORMAL, HitSampleInfo.HIT_CLAP);
                Assert.That(playable.Single(sample => sample.Name == HitSampleInfo.HIT_NORMAL).Volume, Is.EqualTo(64));
            }
        }

        [Test]
        public void TestEditorMarkerCreatesRealHoldSlidingSamples()
        {
            var hold = new SticksHold
            {
                Duration = 1000,
                Samples = new HitSampleInfo[]
                {
                    new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 63),
                    new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 47),
                },
            };
            hold.EnsureLegacyEditorMarker();

            IList<HitSampleInfo> sliding = hold.CreatePlayableSlidingSamples();

            assertMarkerFree(sliding, "sliderslide", "sliderwhistle");
            Assert.Multiple(() =>
            {
                Assert.That(sliding.Single(sample => sample.Name == "sliderslide").Volume, Is.EqualTo(63));
                Assert.That(sliding.Single(sample => sample.Name == "sliderwhistle").Volume, Is.EqualTo(47));
            });
        }

        [Test]
        public void TestEditorMarkerDoesNotLeakIntoSliderTicksRepeatsOrTail()
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 2000,
                RepeatCount = 1,
                ArcAngle = 720,
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 71) },
            };
            slider.EnsureLegacyEditorMarker();

            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 500 });
            slider.ApplyDefaults(controlPoints, new BeatmapDifficulty { SliderTickRate = 1 });

            Assert.That(slider.NestedHitObjects, Is.Not.Empty);
            Assert.That(slider.NestedHitObjects.SelectMany(hitObject => hitObject.Samples)
                              .OfType<ConvertHitObjectParser.FileHitSampleInfo>(), Is.Empty);

            foreach (SticksSliderTick tick in slider.NestedHitObjects.OfType<SticksSliderTick>())
                assertMarkerFree(tick.Samples, "slidertick");

            foreach (SticksSliderExtension extension in slider.NestedHitObjects.OfType<SticksSliderExtension>())
                assertMarkerFree(extension.Samples, "slidertick");

            assertMarkerFree(slider.NestedHitObjects.OfType<SticksSliderRepeat>().Single().Samples, HitSampleInfo.HIT_NORMAL);
            assertMarkerFree(slider.NestedHitObjects.OfType<SticksSliderTail>().Single().Samples, HitSampleInfo.HIT_NORMAL);
        }

        [Test]
        public void TestRegularNormalWinsOverDefensiveDuplicateMarker()
        {
            var flick = new SticksFlick
            {
                Samples = new HitSampleInfo[]
                {
                    new ConvertHitObjectParser.FileHitSampleInfo("sticks-v1~f~l~0.wav", 35),
                    new HitSampleInfo(HitSampleInfo.HIT_NORMAL, HitSampleInfo.BANK_DRUM, volume: 82),
                },
            };

            IList<HitSampleInfo> playable = flick.CreatePlayableSamples();

            assertMarkerFree(playable, HitSampleInfo.HIT_NORMAL);
            Assert.Multiple(() =>
            {
                Assert.That(playable.Single().Bank, Is.EqualTo(HitSampleInfo.BANK_DRUM));
                Assert.That(playable.Single().Volume, Is.EqualTo(82));
            });
        }

        private static void assertMarkerFree(IEnumerable<HitSampleInfo> samples, params string[] expectedNames)
        {
            HitSampleInfo[] materialised = samples.ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(materialised.Select(sample => sample.Name), Is.EqualTo(expectedNames));
                Assert.That(materialised.OfType<ConvertHitObjectParser.FileHitSampleInfo>(), Is.Empty);
                Assert.That(materialised.Any(SticksAuthoredBeatmapCodec.IsMarker), Is.False);
            });
        }
    }
}
