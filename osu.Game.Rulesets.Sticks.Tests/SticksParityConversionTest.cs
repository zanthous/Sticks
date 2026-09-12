using System;
using System.Linq;
using System.Threading;
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
    public class SticksParityConversionTest
    {
        [Test]
        public void TestRepeatedSourceDirectionsUseIndependentMirroredStickPreferences()
        {
            SticksHitObject[] objects = Enumerable.Range(0, 12)
                                                 .SelectMany(index => new SticksHitObject[]
                                                 {
                                                     flick(index * 250, 0),
                                                     flick(index * 250, 90, StickSide.Right),
                                                 }).ToArray();

            SticksHitObject[] leftOnly = Enumerable.Range(0, 12).Select(index => flick(index * 250, 0)).ToArray();
            SticksHitObject[] rightOnly = Enumerable.Range(0, 12).Select(index => flick(index * 250, 90, StickSide.Right)).ToArray();

            apply(objects);
            apply(leftOnly);
            apply(rightOnly);

            Assert.Multiple(() =>
            {
                Assert.That(objects.Where(hitObject => hitObject.Side == StickSide.Left).Select(hitObject => hitObject.Angle),
                    Is.EqualTo(leftOnly.Select(hitObject => hitObject.Angle)));
                Assert.That(objects.Where(hitObject => hitObject.Side == StickSide.Right).Select(hitObject => hitObject.Angle),
                    Is.EqualTo(rightOnly.Select(hitObject => hitObject.Angle)));
                Assert.That(leftOnly[1].Angle, Is.InRange(100, 140));
                for (int i = 0; i < leftOnly.Length; i++)
                {
                    Assert.That(turn(90, rightOnly[i].Angle), Is.EqualTo(-turn(0, leftOnly[i].Angle)).Within(0.001));
                    if (i >= 2)
                        Assert.That(turn(leftOnly[i - 2].Angle, leftOnly[i].Angle), Is.Zero.Within(0.001));
                }
                Assert.That(objects.Select(hitObject => hitObject.StartTime),
                    Is.EqualTo(Enumerable.Range(0, 12).SelectMany(index => new[] { index * 250d, index * 250d })));
            });
        }

        [Test]
        public void TestSmoothSourceMotionDoesNotCollapseToTwoDirectionsOrAnEightDirectionGrid()
        {
            float[] angles = { 13, 15, 18, 23, 31, 42, 56, 74, 96, 123, 155, 193 };
            SticksHitObject[] objects = angles.Select((angle, index) => flick(index * 250, angle)).ToArray();

            apply(objects);

            float[] turns = objects.Skip(1).Select((note, index) => turn(objects[index].Angle, note.Angle)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(turns, Has.All.GreaterThan(90));
                Assert.That(turns.Select(value => Math.Round(value, 1)).Distinct().Count(), Is.GreaterThan(6));
                Assert.That(objects.Select(note => Math.Round(note.Angle, 1)).Distinct().Count(), Is.EqualTo(objects.Length));
                Assert.That(objects.Select(note => Math.Round(note.Angle % 45, 1)).Distinct().Count(), Is.GreaterThan(6));
                Assert.That(objects.Skip(2).Select((note, index) => Math.Abs(turn(objects[index].Angle, note.Angle))), Has.All.GreaterThan(20));
            });
        }

        [Test]
        public void TestParityAdaptsGraduallyWhenTheSameMotionBecomesSparse()
        {
            SticksHitObject[] objects = Enumerable.Range(0, 32)
                                                 .Select(index => flick(index < 16 ? index * 125 : 1875 + (index - 15) * 750, index * 4))
                                                 .ToArray();
            apply(objects);

            float dense = Math.Abs(turn(objects[14].Angle, objects[15].Angle));
            float firstSparse = Math.Abs(turn(objects[15].Angle, objects[16].Angle));
            float settledSparse = Math.Abs(turn(objects[30].Angle, objects[31].Angle));
            Assert.Multiple(() =>
            {
                Assert.That(dense - settledSparse, Is.GreaterThan(25));
                Assert.That(firstSparse, Is.GreaterThan(settledSparse + 1));
                Assert.That(firstSparse, Is.LessThan(dense - 1));
                Assert.That(settledSparse, Is.GreaterThan(4), "Sparse phrases retain a softer parity bias before the rest threshold.");
            });
        }

        [Test]
        public void TestWideSourceMotionReducesTheBiasForTheNextGesture()
        {
            SticksHitObject[] narrowContext = Enumerable.Range(0, 12).Select(index => flick(index * 250, index * 2)).ToArray();
            SticksHitObject[] wideContext = Enumerable.Range(0, 12).Select(index => flick(index * 250, index * 100)).ToArray();
            narrowContext[^1].Angle = narrowContext[^2].Angle + 30;
            wideContext[^1].Angle = wideContext[^2].Angle + 30;

            apply(narrowContext);
            apply(wideContext);

            float afterNarrow = Math.Abs(turn(narrowContext[^2].Angle, narrowContext[^1].Angle));
            float afterWide = Math.Abs(turn(wideContext[^2].Angle, wideContext[^1].Angle));
            Assert.Multiple(() =>
            {
                Assert.That(afterNarrow, Is.GreaterThan(afterWide + 10));
                Assert.That(afterWide, Is.GreaterThan(30));
            });
        }

        [Test]
        public void TestRestRepeatsIdenticalPhrasesWithoutInventingVariation()
        {
            float[] phrase = { 13, 15, 18, 23, 31, 42 };
            SticksHitObject[] objects = Enumerable.Range(0, 3)
                                                 .SelectMany(section => phrase.Select((angle, index) => flick(section * 3000 + index * 250, angle)))
                                                 .ToArray();
            apply(objects);

            for (int section = 1; section < 3; section++)
                Assert.That(objects.Skip(section * phrase.Length).Take(phrase.Length).Select(note => note.Angle),
                    Is.EqualTo(objects.Take(phrase.Length).Select(note => note.Angle)));
        }

        [Test]
        public void TestRotationMirrorAndTempoChangesPreserveTheConvertedPattern()
        {
            float[] angles = { 13, 13, 31, 74, 50, 230, 242, 240, 10, 12 };
            double[] times = { 0, 125, 375, 875, 1125, 1375, 2500, 2750, 3125, 3250 };
            SticksHitObject[] original = angles.Select((angle, index) => flick(times[index], angle)).ToArray();
            SticksHitObject[] rotated = angles.Select((angle, index) => flick(times[index], angle + 37)).ToArray();
            SticksHitObject[] mirrored = angles.Select((angle, index) => flick(times[index], -angle, StickSide.Right)).ToArray();
            SticksHitObject[] faster = angles.Select((angle, index) => flick(times[index] * 0.75, angle)).ToArray();

            apply(original);
            apply(rotated);
            apply(mirrored);
            SticksParityConversion.Apply(faster, source(375), CancellationToken.None);

            Assert.Multiple(() =>
            {
                for (int i = 0; i < original.Length; i++)
                {
                    Assert.That(turn(original[i].Angle + 37, rotated[i].Angle), Is.Zero.Within(0.001));
                    Assert.That(turn(-original[i].Angle, mirrored[i].Angle), Is.Zero.Within(0.001));
                    Assert.That(turn(original[i].Angle, faster[i].Angle), Is.Zero.Within(0.001));
                }
            });
        }

        [TestCase(0, 10)]
        [TestCase(0, -10)]
        [TestCase(350, 5)]
        [TestCase(-350, 355)]
        [TestCase(0, 150)]
        [TestCase(720, 540)]
        public void TestSourceTurnDirectionIsPreservedWithoutSnapping(float previous, float requested)
        {
            SticksHitObject[] objects = { flick(0, previous), flick(250, requested) };
            float sourceTurn = turn(previous, requested);

            apply(objects);
            float convertedTurn = turn(objects[0].Angle, objects[1].Angle);

            Assert.Multiple(() =>
            {
                if (Math.Abs(sourceTurn) < 179.999f)
                {
                    Assert.That(Math.Sign(convertedTurn), Is.EqualTo(Math.Sign(sourceTurn)));
                    Assert.That(Math.Abs(convertedTurn), Is.GreaterThan(Math.Abs(sourceTurn)).And.LessThan(180));
                    Assert.That(Math.Abs(convertedTurn), Is.Not.EqualTo(135).Within(0.001));
                }
                else
                    Assert.That(Math.Abs(convertedTurn), Is.EqualTo(180).Within(0.001));
                Assert.That(objects.Select(hitObject => hitObject.Angle), Has.All.InRange(0, 359.999f));
            });
        }

        [TestCase(500, 999, false)]
        [TestCase(500, 1000, true)]
        [TestCase(250, 500, true)]
        [TestCase(750, 1000, false)]
        public void TestTwoBeatRestResetsParity(double beatLength, double gap, bool resets)
        {
            SticksHitObject[] objects = { flick(0, 0), flick(gap, 10) };

            SticksParityConversion.Apply(objects, source(beatLength), CancellationToken.None);

            if (resets)
                Assert.That(objects[1].Angle, Is.EqualTo(10));
            else
                Assert.That(objects[1].Angle, Is.GreaterThan(10).And.LessThan(180));
        }

        [Test]
        public void TestRestIsMeasuredFromHoldEndAndOtherStickDoesNotPreventReset()
        {
            SticksHitObject[] objects =
            {
                new SticksHold { StartTime = 0, Duration = 3000, Angle = 0, Side = StickSide.Left },
                flick(3000, 270, StickSide.Right),
                flick(3250, 10),
                flick(4200, 90, StickSide.Right),
                flick(4250, 35),
            };

            apply(objects);

            Assert.Multiple(() =>
            {
                Assert.That(objects[2].Angle, Is.GreaterThan(10).And.LessThan(180));
                Assert.That(objects[4].Angle, Is.EqualTo(35));
                Assert.That(((SticksHold)objects[0]).Duration, Is.EqualTo(3000));
            });
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void TestSliderHistoryMatchesAHoldAtItsActualRepeatEndpoint(int repeatCount)
        {
            var slider = new SticksSlider { StartTime = 1000, Duration = 2000, Angle = 0, ArcAngle = 90, RepeatCount = repeatCount };
            SticksHitObject[] objects = { slider, flick(3250, 0) };
            SticksHitObject[] endpointControl =
            {
                new SticksHold { StartTime = slider.StartTime, Duration = slider.Duration, Angle = slider.AngleAt(slider.EndTime) },
                flick(3250, 0),
            };

            apply(objects);
            apply(endpointControl);

            Assert.Multiple(() =>
            {
                Assert.That(objects[1].Angle, Is.EqualTo(endpointControl[1].Angle).Within(0.001));
                Assert.That(slider.RepeatCount, Is.EqualTo(repeatCount));
                Assert.That(slider.ArcAngle, Is.EqualTo(90));
            });
        }

        [Test]
        public void TestCustomSliderRotationPreservesEntirePathAndHitsounds()
        {
            var slider = new SticksSlider
            {
                StartTime = 250,
                Duration = 1500,
                Angle = 0,
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 42) },
            };
            slider.SetCustomSegments(new[] { 60f, -30f, 90f });
            slider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 24) });
            double[] sampleTimes = Enumerable.Range(0, 21).Select(index => slider.StartTime + slider.Duration * index / 20).ToArray();
            float[] originalPath = sampleTimes.Select(slider.AngleAt).ToArray();
            SticksHitObject[] objects = { flick(0, 0), slider, flick(2000, 255) };

            apply(objects);

            Assert.Multiple(() =>
            {
                Assert.That(slider.Angle, Is.InRange(100, 140));
                Assert.That(slider.SegmentArcAngles, Is.EqualTo(new[] { 60f, -30f, 90f }));
                Assert.That(slider.HasCustomSegments, Is.True);
                Assert.That(slider.Duration, Is.EqualTo(1500));
                Assert.That(slider.StartTime, Is.EqualTo(250));
                Assert.That(sampleTimes.Select((time, index) => slider.AngleAt(time) - originalPath[index]), Has.All.EqualTo(slider.Angle).Within(0.001));
                Assert.That(turn(slider.AngleAt(slider.EndTime), objects[2].Angle), Is.GreaterThan(135).And.LessThan(180),
                    "The next turn must use the source endpoint for direction and the rotated endpoint for placement.");
                Assert.That(slider.Samples.Single().Name, Is.EqualTo(HitSampleInfo.HIT_CLAP));
                Assert.That(slider.Samples.Single().Volume, Is.EqualTo(42));
                Assert.That(slider.NodeSamples.Single().Single().Name, Is.EqualTo(HitSampleInfo.HIT_WHISTLE));
            });
        }

        [Test]
        public void TestOverlappingFlickDoesNotReplaceActiveSliderHistoryOrSectionContext()
        {
            SticksHitObject[] objects =
            {
                flick(0, 10),
                new SticksSlider { StartTime = 250, Duration = 1500, Angle = 20, ArcAngle = 90 },
                flick(500, 200),
                flick(750, 330),
                flick(2000, 0),
                flick(2250, 30),
            };
            SticksHitObject[] withoutOverlap =
            {
                flick(0, 10),
                new SticksSlider { StartTime = 250, Duration = 1500, Angle = 20, ArcAngle = 90 },
                flick(2000, 0),
                flick(2250, 30),
            };

            apply(objects);
            apply(withoutOverlap);

            Assert.Multiple(() =>
            {
                Assert.That(objects[2].Angle, Is.EqualTo(200));
                Assert.That(objects[3].Angle, Is.EqualTo(330));
                Assert.That(objects.Where(note => note.StartTime >= 2000).Select(note => note.Angle),
                    Is.EqualTo(withoutOverlap.Where(note => note.StartTime >= 2000).Select(note => note.Angle)));
            });
        }

        [Test]
        public void TestOrderIsChronologicalAndRepeatedConversionIsDeterministic()
        {
            SticksHitObject[] ordered = Enumerable.Range(0, 24).Select(index => flick(index * 125, index * 47 % 360)).ToArray();
            SticksHitObject[] reversed = ordered.Reverse().Select(hitObject => flick(hitObject.StartTime, hitObject.Angle)).ToArray();

            apply(ordered);
            apply(reversed);

            Assert.That(reversed.Reverse().Select(hitObject => hitObject.Angle), Is.EqualTo(ordered.Select(hitObject => hitObject.Angle)));
        }

        [Test]
        public void TestCancellationBeforeMutation()
        {
            SticksHitObject[] objects = { flick(0, 0), flick(250, 10) };

            Assert.Throws<OperationCanceledException>(() => SticksParityConversion.Apply(objects, source(), new CancellationToken(true)));
            Assert.That(objects[1].Angle, Is.EqualTo(10));
        }

        [Test]
        public void TestParityModAppliesAfterConverterConstructionAndKeepsStandardDefault()
        {
            Beatmap<HitObject> beatmap = source();
            beatmap.HitObjects.Add(new PositionedHitObject { StartTime = 1000, Position = new Vector2(512, 192) });
            beatmap.HitObjects.Add(new PositionedHitObject { StartTime = 1500, Position = new Vector2(512, 192) });
            var converter = new SticksBeatmapConverter(beatmap, new SticksRuleset());

            Assert.That(converter.ConversionMode, Is.EqualTo(SticksConversionMode.Standard));
            SticksHitObject[] standard = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            new SticksModParity().ApplyToBeatmapConverter(converter);
            SticksHitObject[] parity = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            converter.ConversionMode = SticksConversionMode.Standard;
            SticksHitObject[] standardAgain = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(standard.Select(hitObject => hitObject.Angle), Is.EqualTo(new[] { 0, 0 }));
                Assert.That(parity[0].Angle, Is.EqualTo(0));
                Assert.That(parity[1].Angle, Is.GreaterThan(90).And.LessThan(135));
                Assert.That(parity.Select(hitObject => hitObject.Side), Is.EqualTo(standard.Select(hitObject => hitObject.Side)));
                Assert.That(parity.Select(hitObject => hitObject.StartTime), Is.EqualTo(standard.Select(hitObject => hitObject.StartTime)));
                Assert.That(standardAgain.Select(hitObject => hitObject.Angle), Is.EqualTo(standard.Select(hitObject => hitObject.Angle)));
            });
        }

        [Test]
        public void TestSyncedLinksUseConvertedParityAngles()
        {
            Beatmap<HitObject> beatmap = source();
            foreach (double time in new[] { 1000d, 1500 })
            {
                beatmap.HitObjects.Add(new PositionedHitObject { StartTime = time, Position = new Vector2(512, 192) });
                beatmap.HitObjects.Add(new PositionedHitObject { StartTime = time, Position = new Vector2(512, 192) });
            }

            SticksHitObject[] converted = new SticksBeatmapConverter(beatmap, new SticksRuleset())
            {
                ConversionMode = SticksConversionMode.Parity,
            }.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            foreach (var chord in converted.GroupBy(hitObject => hitObject.StartTime))
            {
                SticksHitObject owner = chord.Single(hitObject => hitObject.SyncedNoteSide.HasValue);
                SticksHitObject partner = chord.Single(hitObject => hitObject.Side != owner.Side);
                Assert.Multiple(() =>
                {
                    Assert.That(owner.SyncedNoteSide, Is.EqualTo(partner.Side));
                    Assert.That(owner.SyncedNoteAngle, Is.EqualTo(partner.Angle));
                });
            }
        }

        [TestCase(SticksConversionMode.Parity)]
        [TestCase(SticksConversionMode.Duet)]
        [TestCase(SticksConversionMode.ParityDuet)]
        public void TestExperimentalModesPreserveAuthoredCarriers(SticksConversionMode mode)
        {
            Beatmap<HitObject> beatmap = source();
            var slider = new SticksSlider { StartTime = 2000, Duration = 1500, Angle = 50, Side = StickSide.Right };
            slider.SetCustomSegments(new[] { 60f, -30f, 90f });
            SticksHitObject[] originals = { flick(1000, 5), flick(1500, 10), slider };
            beatmap.HitObjects.AddRange(originals.Select(SticksAuthoredBeatmapCodec.CreateLegacyProxy));
            var converter = new SticksBeatmapConverter(beatmap, new SticksRuleset()) { ConversionMode = mode };

            SticksHitObject[] converted = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converter.IsAuthoredCarrier, Is.True);
                Assert.That(converted.Select(hitObject => hitObject.Angle), Is.EqualTo(originals.Select(hitObject => hitObject.Angle)));
                Assert.That(converted.Select(hitObject => hitObject.Side), Is.EqualTo(originals.Select(hitObject => hitObject.Side)));
                Assert.That(converted.Select(hitObject => hitObject.StartTime), Is.EqualTo(originals.Select(hitObject => hitObject.StartTime)));
                Assert.That(((SticksSlider)converted[2]).SegmentArcAngles, Is.EqualTo(slider.SegmentArcAngles));
                Assert.That(((SticksSlider)converted[2]).Duration, Is.EqualTo(slider.Duration));
            });
        }

        [Test]
        public void TestExperimentalModsAreRegisteredUnrankedAndMutuallyExclusive()
        {
            Mod[] mods = new SticksRuleset().GetModsFor(ModType.Conversion).ToArray();
            var parity = mods.OfType<SticksModParity>().Single();
            var duet = mods.OfType<SticksModDuet>().Single();
            var converter = new SticksBeatmapConverter(source(), new SticksRuleset());
            duet.ApplyToBeatmapConverter(converter);

            Assert.Multiple(() =>
            {
                Assert.That(parity.Acronym, Is.EqualTo("PA"));
                Assert.That(duet.Acronym, Is.EqualTo("DU"));
                Assert.That(parity.Ranked, Is.False);
                Assert.That(duet.Ranked, Is.False);
                Assert.That(parity.IncompatibleMods, Does.Contain(typeof(SticksModDuet)));
                Assert.That(duet.IncompatibleMods, Does.Contain(typeof(SticksModParity)));
                Assert.That(converter.ConversionMode, Is.EqualTo(SticksConversionMode.Duet));
            });
        }

        private static float turn(float from, float to) => SticksHitObject.DeltaAngle(from, to);

        private static SticksFlick flick(double time, float angle, StickSide side = StickSide.Left) => new SticksFlick
        {
            StartTime = time,
            Angle = angle,
            Side = side,
        };

        private static Beatmap<HitObject> source(double beatLength = 500)
        {
            var beatmap = new Beatmap<HitObject>();
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = beatLength });
            return beatmap;
        }

        private static void apply(SticksHitObject[] hitObjects) => SticksParityConversion.Apply(hitObjects, source(), CancellationToken.None);

        private sealed class PositionedHitObject : HitObject, IHasPosition
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
    }
}
