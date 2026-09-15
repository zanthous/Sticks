using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksDuetConversionTest
    {
        [Test]
        public void TestDuetPreservesBothObjectsOfEveryBaselineChordInMixedPhrases()
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 375 });
            for (int i = 0; i < 64; i++)
            {
                double time = 1125 + i * 375;
                float angle = i * 67 % 360;
                HitObject note = i % 7 == 0
                    ? nativeSlider(time, 187.5, angle)
                    : new PositionedHitObject { StartTime = time, Position = position(angle) };
                if (i % 4 == 0)
                    note.Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 61) };
                source.HitObjects.Add(note);
            }

            SticksHitObject[] standard = new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = SticksConversionMode.Standard, UseCounterpoint = false }.Convert()
                .HitObjects.Cast<SticksHitObject>().ToArray();
            SticksHitObject[] duet = convert(source);
            SticksHitObject[] baselineChords = standard.GroupBy(note => note.StartTime)
                .Where(group => group.Select(note => note.Side).Distinct().Count() == 2)
                .SelectMany(group => group).ToArray();

            Assert.That(baselineChords.Length, Is.GreaterThanOrEqualTo(4), "The fixture must exercise multiple baseline chords.");
            string[] duetSignatures = signature(duet);
            foreach (SticksHitObject note in baselineChords)
            {
                Assert.That(duetSignatures, Does.Contain(signature(new[] { note }).Single()),
                    $"Duet changed or removed the baseline {note.Side} chord object at {note.StartTime}.");
                SticksHitObject preserved = duet.Single(candidate => candidate.StartTime == note.StartTime && candidate.Side == note.Side);
                Assert.That(preserved.Samples.Select(sample => (sample.Name, sample.Volume)),
                    Is.EqualTo(note.Samples.Select(sample => (sample.Name, sample.Volume))));
            }

            assertPlayable(duet);
        }

        [Test]
        public void TestDuetDoesNotConsumeOneHeadOfASourceChordAsAPhraseTail()
        {
            // Without chord protection, the first four anchors fit a nested Duet phrase
            // whose held outer voice would absorb just one of the two final source heads.
            Beatmap<HitObject> source = phrase(new[] { 1000d, 1625, 2250, 2875, 2875 },
                new[] { 0f, 90, 150, 0, 180 });
            SticksHitObject[] standard = new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = SticksConversionMode.Standard, UseCounterpoint = false }.Convert()
                .HitObjects.Cast<SticksHitObject>().ToArray();
            SticksHitObject[] baselineChord = standard.Where(note => note.StartTime == 2875).ToArray();
            SticksHitObject[] duet = convert(source);

            Assert.Multiple(() =>
            {
                Assert.That(baselineChord, Has.Length.EqualTo(2));
                Assert.That(baselineChord.Select(note => note.Side).Distinct().Count(), Is.EqualTo(2));
                Assert.That(signature(duet.Where(note => note.StartTime == 2875)), Is.EqualTo(signature(baselineChord)));
            });
            assertPlayable(duet);
        }

        [Test]
        public void TestReturningOuterAnchorProducesStationarySliderAroundIndependentSlider()
        {
            // This off-downbeat 5/4-spacing phrase is unclaimed by the baseline converter.
            Beatmap<HitObject> source = phrase(new[] { 1000d, 1625, 2250, 2875 }, new[] { 0f, 90, 150, 0 });

            SticksHitObject[] converted = convert(source);
            SticksSlider hold = converted.OfType<SticksSlider>().Single(note => note.IsStationary);
            SticksSlider slider = converted.OfType<SticksSlider>().Single(note => !note.IsStationary);

            Assert.Multiple(() =>
            {
                Assert.That(converted, Has.Length.EqualTo(2));
                Assert.That(hold.StartTime, Is.EqualTo(1000));
                Assert.That(hold.EndTime, Is.EqualTo(2875));
                Assert.That(hold.Angle, Is.EqualTo(0).Within(0.001));
                Assert.That(slider.StartTime, Is.EqualTo(1625));
                Assert.That(slider.EndTime, Is.EqualTo(2250));
                Assert.That(slider.Angle, Is.EqualTo(90).Within(0.001));
                Assert.That(slider.ArcAngle, Is.EqualTo(60).Within(0.001));
                Assert.That(slider.Side, Is.Not.EqualTo(hold.Side));
            });
            assertPlayable(converted);
        }

        [Test]
        public void TestInterleavedVoicesBecomeContrarySlidersWithInteriorTicks()
        {
            Beatmap<HitObject> source = unclaimedInterleavedPhrase();

            SticksHitObject[] converted = convert(source);
            SticksSlider[] sliders = converted.OfType<SticksSlider>().Where(slider => slider.StartTime >= 600).ToArray();
            foreach (SticksSlider slider in sliders)
                slider.ApplyDefaults(source.ControlPointInfo, source.Difficulty);

            Assert.Multiple(() =>
            {
                Assert.That(converted, Has.Length.EqualTo(3));
                Assert.That(sliders, Has.Length.EqualTo(2));
                Assert.That(sliders.Select(slider => slider.StartTime), Is.EqualTo(new[] { 600d, 900 }));
                Assert.That(sliders.Select(slider => slider.EndTime), Is.EqualTo(new[] { 1800d, 2100 }));
                Assert.That(sliders[0].SegmentArcAngles, Is.EqualTo(new[] { 30f, 30 }).Within(0.001));
                Assert.That(sliders[1].SegmentArcAngles, Is.EqualTo(new[] { -30f, -30 }).Within(0.001));
                Assert.That(sliders[0].Side, Is.Not.EqualTo(sliders[1].Side));
                Assert.That(sliders[0].NestedHitObjects.OfType<SticksSliderTick>().Select(tick => tick.StartTime), Does.Contain(1200d));
                Assert.That(sliders[1].NestedHitObjects.OfType<SticksSliderTick>().Select(tick => tick.StartTime), Does.Contain(1500d));
                Assert.That(sliders[0].AngleAt(1200), Is.EqualTo(30).Within(0.001));
                Assert.That(sliders[1].AngleAt(1500), Is.EqualTo(150).Within(0.001));
            });
            assertPlayable(converted);
        }

        [TestCase(1)]
        [TestCase(1.5)]
        public void TestMovingInterleavedAnchorsKeepCheckpointsAndSamplesBetweenRegularTicks(double tickRate)
        {
            Beatmap<HitObject> source = unclaimedInterleavedPhrase();
            source.Difficulty.SliderTickRate = tickRate;
            source.HitObjects[3].Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 41) };
            source.HitObjects[4].Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 42) };

            SticksHitObject[] converted = convert(source);
            SticksSlider[] voices = converted.OfType<SticksSlider>().Where(note => note.StartTime >= 600).ToArray();
            Assert.That(voices, Has.Length.EqualTo(2));
            foreach (SticksSlider slider in voices)
            {
                slider.ApplyDefaults(source.ControlPointInfo, source.Difficulty);
                double anchorTime = slider.StartTime + 600;
                HitObject anchor = source.HitObjects.Single(note => note.StartTime == anchorTime);
                SticksSliderTick checkpoint = slider.NestedHitObjects.OfType<SticksSliderTick>().Single(note => Math.Abs(note.StartTime - anchorTime) < 0.001);
                Assert.Multiple(() =>
                {
                    Assert.That(slider.HasTimedSegments, Is.True);
                    Assert.That(checkpoint.Angle, Is.EqualTo(slider.AngleAt(anchorTime)).Within(0.001));
                    Assert.That(checkpoint.Samples.Select(sample => (sample.Name, sample.Volume)),
                        Is.EqualTo(anchor.Samples.Select(sample => (sample.Name, sample.Volume))));
                });
            }
            assertPlayable(converted);
        }

        [TestCase(40, 140)]
        [TestCase(0, 180)]
        [TestCase(65, 130)]
        public void TestInterleavedPathsKeepTurnsReturnsAndDwellsAtTheirSourceTimes(float firstEnd, float secondEnd)
        {
            // A single fitted arc would miss the first interior anchor by 45 degrees
            // in the first case. Returning endpoints and stationary final legs are
            // still moving gestures and must not be collapsed into holds.
            float[] angles = { 0, 180, 65, 130, firstEnd, secondEnd };
            Beatmap<HitObject> source = unclaimedInterleavedPhrase(angles);
            source.HitObjects[3].Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 41) };
            source.HitObjects[4].Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 42) };
            SticksHitObject[] converted = convert(source);
            SticksSlider[] voices = converted.OfType<SticksSlider>().Where(note => note.StartTime >= 600).ToArray();

            Assert.That(voices, Has.Length.EqualTo(2));
            for (int voice = 0; voice < voices.Length; voice++)
            {
                SticksSlider slider = voices[voice];
                slider.ApplyDefaults(source.ControlPointInfo, source.Difficulty);
                Assert.Multiple(() =>
                {
                    Assert.That(slider.HasTimedSegments, Is.True);
                    Assert.That(slider.SegmentDurationWeights, Is.EqualTo(new[] { 0.5, 0.5 }));
                    Assert.That(slider.StartTime, Is.EqualTo(600 + voice * 300));
                    Assert.That(slider.EndTime, Is.EqualTo(1800 + voice * 300));
                    Assert.That(slider.AngleAt(slider.StartTime + 300), Is.EqualTo((angles[voice] + angles[voice + 2]) / 2).Within(0.001));
                });

                for (int anchorIndex = 0; anchorIndex < 3; anchorIndex++)
                {
                    double time = 600 + voice * 300 + anchorIndex * 600;
                    Assert.That(slider.AngleAt(time), Is.EqualTo(angles[voice + anchorIndex * 2]).Within(0.001));
                    SticksHitObject checkpoint = slider.NestedHitObjects.Cast<SticksHitObject>().Single(note => Math.Abs(note.StartTime - time) < 0.001);
                    Assert.That(checkpoint.Angle, Is.EqualTo(angles[voice + anchorIndex * 2]).Within(0.001));
                    if (anchorIndex == 1)
                    {
                        HitObject anchor = source.HitObjects.Single(note => note.StartTime == time);
                        Assert.That(checkpoint.Samples.Select(sample => (sample.Name, sample.Volume)),
                            Is.EqualTo(anchor.Samples.Select(sample => (sample.Name, sample.Volume))));
                    }
                }
            }
            assertPlayable(converted);
        }

        [TestCase(1, 0)]
        [TestCase(1.5, 0)]
        [TestCase(1, 10)]
        [TestCase(1.5, 10)]
        public void TestLowMotionVoiceRetainsFlickRhythmBesideMovingSlider(double tickRate, float drift)
        {
            Beatmap<HitObject> source = unclaimedInterleavedPhrase(new[] { 0f, 180, drift / 2, 150, drift, 120 });
            source.Difficulty.SliderTickRate = tickRate;
            source.HitObjects[3].Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 41) };
            SticksHitObject[] converted = convert(source);

            SticksSlider slider = converted.OfType<SticksSlider>().Single(note => note.StartTime >= 600);
            SticksFlick[] pulses = converted.OfType<SticksFlick>().Where(note => note.StartTime >= 600).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(converted.OfType<SticksSlider>().Where(note => note.IsStationary), Is.Empty);
                Assert.That(slider.StartTime, Is.EqualTo(900));
                Assert.That(slider.EndTime, Is.EqualTo(2100));
                Assert.That(slider.SegmentArcAngles, Is.EqualTo(new[] { -30f, -30 }).Within(0.001));
                Assert.That(pulses.Select(note => note.StartTime), Is.EqualTo(new[] { 600d, 1200, 1800 }));
                Assert.That(pulses.Select(note => note.Angle), Is.EqualTo(new[] { 0, drift / 2, drift }).Within(0.001));
                Assert.That(pulses, Has.All.Matches<SticksFlick>(note => note.Side != slider.Side));
                Assert.That(pulses[1].Samples.Single().Volume, Is.EqualTo(41));
            });
            assertPlayable(converted);
        }

        [TestCase(65, 2)]
        [TestCase(80, 1)]
        public void TestInterleavedCandidateRejectsAnOverfastSegmentDespiteSlowOverallMovement(float interiorAngle, int expectedVoices)
        {
            Beatmap<HitObject> source = unclaimedInterleavedPhrase(new[] { 0f, 180, interiorAngle, 130, 40, 140 });
            SticksHitObject[] converted = convert(source);

            Assert.That(converted.OfType<SticksSlider>().Count(note => note.StartTime >= 600), Is.EqualTo(expectedVoices));
            assertPlayable(converted);
        }

        [Test]
        public void TestDisableReversalsDeclinesTurningInterleavedPathsWithoutStraighteningThem()
        {
            Beatmap<HitObject> source = unclaimedInterleavedPhrase(new[] { 0f, 180, 65, 130, 40, 140 });
            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset())
            {
                ConversionMode = SticksConversionMode.Duet,
                DisableReversals = true,
                UseCounterpoint = false,
            }.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

            // The single-sustain accompaniment remains eligible, while both turning
            // source voices must not be accepted and then silently straightened.
            Assert.That(converted.OfType<SticksSlider>().Count(note => note.StartTime >= 600), Is.EqualTo(1));
            Assert.That(converted.OfType<SticksFlick>().Select(note => note.StartTime),
                Is.SupersetOf(new[] { 900d, 1200, 1500, 1800 }));
            foreach (SticksSlider slider in converted.OfType<SticksSlider>())
                Assert.That(slider.SegmentArcAngles.Where(arc => Math.Abs(arc) > 0.001).Select(Math.Sign).Distinct().Count(), Is.LessThanOrEqualTo(1));
            assertPlayable(converted);
        }

        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        public void TestSyncopatedAccompanimentRetainsIndependentSourceDirections(int count)
        {
            double[] times = { 0, 375, 750, 1375, 1750, 2125, 2750, 3125, 3500 };
            float[] angles = { 0, 20, 135, 170, 60, 80, 40, 110, 70 };
            Beatmap<HitObject> source = phrase(times.Take(count).ToArray(), angles.Take(count).ToArray());

            SticksHitObject[] converted = convert(source);
            SticksSlider sustain = converted.OfType<SticksSlider>().Single();
            SticksFlick[] accompaniment = converted.OfType<SticksFlick>().ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(converted, Has.Length.EqualTo(count - 1));
                Assert.That(sustain.StartTime, Is.Zero);
                Assert.That(sustain.EndTime, Is.EqualTo(times[count - 1]));
                Assert.That(sustain.ArcAngle, Is.EqualTo(angles[count - 1]).Within(0.001));
                Assert.That(accompaniment.Select(note => note.StartTime), Is.EqualTo(times.Skip(1).Take(count - 2)));
                Assert.That(accompaniment, Has.All.Matches<SticksFlick>(note => note.Side != sustain.Side));
                for (int i = 0; i < accompaniment.Length; i++)
                    Assert.That(accompaniment[i].Angle, Is.EqualTo(angles[i + 1]).Within(0.001));
            });
            assertPlayable(converted);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestChordPhraseKeepsParallelAccentsOrMirroredJumps(bool jumping)
        {
            float[] angles = jumping ? new[] { 0f, 90, 0, 90 } : new[] { 0f, 10, 20, 30 };
            Beatmap<HitObject> source = phrase(new[] { 500d, 1000, 1500, 2000 }, angles);
            if (!jumping)
            {
                source.HitObjects[0].Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 42) };
                source.HitObjects[2].Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_FINISH, volume: 64) };
            }
            // The preceding slider keeps this from being a baseline phrase-start hold.
            source.HitObjects.Insert(0, nativeSlider(0, 100, 270));

            SticksHitObject[] converted = convert(source);
            SticksHitObject[] chords = converted.Where(note => note.StartTime >= 500).ToArray();

            Assert.That(chords, Has.Length.EqualTo(8));
            Assert.That(chords, Has.All.TypeOf<SticksFlick>());
            int index = 0;
            foreach (var chord in chords.GroupBy(note => note.StartTime))
            {
                SticksHitObject first = chord.First();
                SticksHitObject second = chord.Last();
                Assert.Multiple(() =>
                {
                    Assert.That(chord.Count(), Is.EqualTo(2));
                    Assert.That(second.Side, Is.Not.EqualTo(first.Side));
                    Assert.That(first.Angle, Is.EqualTo(angles[index]).Within(0.001));
                    Assert.That(second.Angle, Is.EqualTo(jumping ? 180 - angles[index] : angles[index]).Within(0.001));
                });
                if (!jumping && index % 2 == 0)
                    Assert.That(chord.Select(note => note.Samples.Single().Volume), Has.All.EqualTo(index == 0 ? 42 : 64));
                index++;
            }
            assertPlayable(converted);
        }

        [TestCase(0, true, false)]
        [TestCase(0, true, true)]
        [TestCase(90, false, false)]
        [TestCase(90, false, true)]
        public void TestNativeSliderPairPreservesDurationRepeatsAndSamples(float angle, bool mirrored, bool disableReversals)
        {
            Beatmap<HitObject> source = phrase(Array.Empty<double>(), Array.Empty<float>());
            var native = nativeSlider(1000, 2000, angle);
            native.RepeatCount = 1;
            native.Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 60) };
            native.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 31) });
            native.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 42) });
            native.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_FINISH, volume: 53) });
            source.HitObjects.Add(native);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset())
            {
                ConversionMode = SticksConversionMode.Duet,
                DisableReversals = disableReversals,
                UseCounterpoint = false,
            };

            SticksHitObject[] converted = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            SticksSlider[] sliders = converted.OfType<SticksSlider>().ToArray();

            Assert.That(sliders, Has.Length.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(sliders.Select(slider => slider.Duration), Has.All.EqualTo(2000));
                Assert.That(sliders.Select(slider => slider.StartTime), Has.All.EqualTo(1000));
                Assert.That(sliders.Select(slider => slider.RepeatCount), Has.All.EqualTo(disableReversals ? 0 : 1));
                Assert.That(sliders[1].Side, Is.Not.EqualTo(sliders[0].Side));
                Assert.That(sliders[1].Angle, Is.EqualTo(mirrored ? 180 - sliders[0].Angle : sliders[0].Angle).Within(0.001));
                Assert.That(sliders[1].ArcAngle, Is.EqualTo(mirrored ? -sliders[0].ArcAngle : sliders[0].ArcAngle).Within(0.001));
                Assert.That(sliders[1].Samples.Single().Volume, Is.EqualTo(60));
                Assert.That(sliders[1].Samples, Is.Not.SameAs(sliders[0].Samples));
                Assert.That(sliders[1].NodeSamples.Select(samples => samples.Single().Name),
                    Is.EqualTo(sliders[0].NodeSamples.Select(samples => samples.Single().Name)));
                Assert.That(sliders[1].NodeSamples.Select(samples => samples.Single().Volume),
                    Is.EqualTo(sliders[0].NodeSamples.Select(samples => samples.Single().Volume)));
            });
            assertPlayable(converted);
        }

        [TestCase(-100, 1)]
        [TestCase(0, 1)]
        [TestCase(100, 1)]
        [TestCase(260, 1)]
        [TestCase(261, 2)]
        public void TestNativePairReservesTailAndRecoveryMargin(double tailGap, int expectedSliders)
        {
            Beatmap<HitObject> source = phrase(new[] { 2500 + tailGap }, new[] { 90f });
            source.HitObjects.Insert(0, nativeSlider(1000, 1500, 0));

            SticksHitObject[] converted = convert(source);

            Assert.Multiple(() =>
            {
                Assert.That(converted.OfType<SticksSlider>().Count(), Is.EqualTo(expectedSliders));
                Assert.That(converted.OfType<SticksFlick>().Count(note => note.StartTime == 2500 + tailGap), Is.EqualTo(1));
            });
            assertPlayable(converted);
        }

        [TestCase(260, 1)]
        [TestCase(261, 2)]
        public void TestAccentedNativePairReservesHeadRecoveryMargin(double headGap, int expectedSliders)
        {
            Beatmap<HitObject> source = phrase(new[] { 2000 - headGap }, new[] { 90f });
            NativeSlider native = nativeSlider(2000, 1500, 0);
            native.Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP) };
            source.HitObjects.Add(native);

            SticksHitObject[] converted = convert(source);

            Assert.That(converted.OfType<SticksSlider>().Count(), Is.EqualTo(expectedSliders));
            assertPlayable(converted);
        }

        [Test]
        public void TestOverfastSourceGeometryStaysAsFlicks()
        {
            Beatmap<HitObject> source = phrase(new[] { 500d, 875, 1250, 1625 }, new[] { 0f, 180, 180, 0 });
            source.HitObjects.Insert(0, nativeSlider(0, 100, 270));

            SticksHitObject[] converted = convert(source);
            SticksHitObject[] fastPhrase = converted.Where(note => note.StartTime >= 500).ToArray();

            Assert.That(fastPhrase, Has.Length.EqualTo(4));
            Assert.That(fastPhrase, Has.All.TypeOf<SticksFlick>());
            assertPlayable(converted);
        }

        [Test]
        public void TestReuseAcrossModesIsDeterministicAndDurationPartnersStayChronological()
        {
            Beatmap<HitObject> source = phrase(new[] { 0d, 500, 1000, 1500, 2000, 2500 }, new[] { 0f, 180, 30, 150, 60, 120 });
            source.HitObjects.Add(nativeSlider(6000, 2000, 0));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = SticksConversionMode.Standard, UseCounterpoint = false };
            string[] standard = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            new SticksModDuet().ApplyToBeatmapConverter(converter);
            SticksHitObject[] first = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            string[] repeated = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            converter.ConversionMode = SticksConversionMode.Standard;
            string[] standardAgain = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            converter.ConversionMode = SticksConversionMode.Duet;
            string[] duetAgain = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());

            Assert.Multiple(() =>
            {
                Assert.That(first.OfType<SticksSlider>().Count(note => note.StartTime == 6000), Is.EqualTo(2));
                Assert.That(first.Select(note => note.StartTime), Is.Ordered);
                Assert.That(repeated, Is.EqualTo(signature(first)));
                Assert.That(duetAgain, Is.EqualTo(signature(first)));
                Assert.That(standardAgain, Is.EqualTo(standard));
                Assert.That(source.HitObjects.Count, Is.EqualTo(7));
            });
            assertPlayable(first);
        }

        private static void assertPlayable(SticksHitObject[] objects)
        {
            Assert.That(objects.Select(note => note.StartTime), Is.Ordered);
            foreach (var voice in objects.GroupBy(note => note.Side))
            {
                SticksHitObject[] ordered = voice.OrderBy(note => note.StartTime).ToArray();
                for (int i = 1; i < ordered.Length; i++)
                    Assert.That(ordered[i].StartTime, Is.GreaterThan(endTime(ordered[i - 1])),
                        $"{voice.Key} has overlapping gestures at {ordered[i - 1].StartTime} and {ordered[i].StartTime}.");
            }

            foreach (SticksSlider slider in objects.OfType<SticksSlider>())
            {
                for (int segment = 0; segment < slider.SegmentCount; segment++)
                    Assert.That(Math.Abs(slider.SegmentArcAngleAt(segment)) / slider.SegmentDurationAt(segment) * 1000,
                        Is.LessThanOrEqualTo(SticksBeatmapConverter.MAX_GENERATED_SLIDER_ANGULAR_VELOCITY + 0.001));
            }
        }

        private static double endTime(SticksHitObject note) => note.StartTime + (note is IHasDuration duration ? duration.Duration : 0);

        private static string[] signature(IEnumerable<SticksHitObject> objects) => objects.Select(note =>
            $"{note.GetType().Name}:{note.StartTime}:{endTime(note)}:{note.Side}:{note.Angle}:"
            + (note is SticksSlider slider
                ? $"{string.Join(",", slider.SegmentArcAngles)}:{string.Join(",", slider.SegmentDurationWeights ?? Array.Empty<double>())}"
                : string.Empty)).ToArray();

        // These regressions isolate the historical duet pass before the Counterpoint arrangement.
        private static SticksHitObject[] convert(Beatmap<HitObject> source) => new SticksBeatmapConverter(source, new SticksRuleset()) { UseCounterpoint = false }
            .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

        private static Beatmap<HitObject> unclaimedInterleavedPhrase(float[] angles = null)
        {
            Beatmap<HitObject> source = phrase(new[] { 600d, 900, 1200, 1500, 1800, 2100 },
                angles ?? new[] { 0f, 180, 30, 150, 60, 120 }, 600);
            // The short preceding slider prevents baseline phrase-start synthesis. The
            // two voices finish before the next baseline downbeat and avoid rapid streams.
            source.HitObjects.Insert(0, nativeSlider(300, 10, 270));
            return source;
        }

        private static Beatmap<HitObject> phrase(double[] times, float[] angles, double beatLength = 500)
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = beatLength });
            for (int i = 0; i < times.Length; i++)
                source.HitObjects.Add(new PositionedHitObject { StartTime = times[i], Position = position(angles[i]) });
            return source;
        }

        private static Vector2 position(float angle)
        {
            float radians = angle * MathF.PI / 180;
            return SticksBeatmapConverter.STANDARD_CENTRE + new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * 160;
        }

        private static NativeSlider nativeSlider(double time, double duration, float angle) => new NativeSlider
        {
            StartTime = time,
            Duration = duration,
            Position = position(angle),
        };

        private class PositionedHitObject : HitObject, IHasPosition
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

        private sealed class NativeSlider : PositionedHitObject, IHasDuration, IHasRepeats
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
            public int RepeatCount { get; set; }
            public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();
        }
    }
}
