using System;
using System.Collections.Generic;
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
    public class SticksModParityDuetTest
    {
        [Test]
        public void TestDuetChordPhraseRetainsBothVoicesWithParitySpacing()
        {
            Beatmap<HitObject> source = chordPhrase();
            SticksHitObject[] standard = convert(source, SticksConversionMode.Standard);
            SticksHitObject[] duet = convert(source, SticksConversionMode.Duet);
            SticksHitObject[] combined = assertComposition(source, duet);
            SticksHitObject[] chords = combined.Where(note => note.StartTime >= 500).ToArray();

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(chords, Has.Length.EqualTo(8));
                Assert.That(chordCount(combined), Is.EqualTo(chordCount(duet)));
                Assert.That(chordCount(combined), Is.GreaterThan(chordCount(standard)));
                Assert.That(combined.Select(note => note.Angle), Is.Not.EqualTo(duet.Select(note => note.Angle)));
            });

            foreach (var voice in chords.GroupBy(note => note.Side))
            {
                SticksHitObject[] notes = voice.ToArray();
                SticksHitObject[] originalNotes = duet.Where(note => note.StartTime >= 500 && note.Side == voice.Key).ToArray();
                for (int i = 1; i < notes.Length; i++)
                    assertWiderSourceTurn(originalNotes[i - 1], originalNotes[i], notes[i - 1], notes[i]);
            }
        }

        [Test]
        public void TestParityIncludesAccompanimentAddedInsideNativeSlider()
        {
            // Two local half-beat intervals establish the extra pulse. The previous
            // opposite-stick note is close enough that parity must include this new hit.
            Beatmap<HitObject> source = map(circle(0, 90), circle(250, 135), slider(750, 500), circle(1750, 210), circle(2000, 270));
            SticksHitObject[] standard = convert(source, SticksConversionMode.Standard);
            SticksHitObject[] duet = convert(source, SticksConversionMode.Duet);
            SticksHitObject[] combined = assertComposition(source, duet);
            SticksFlick accent = combined.OfType<SticksFlick>().Single(note => note.StartTime == 1000);
            SticksFlick originalAccent = duet.OfType<SticksFlick>().Single(note => note.StartTime == 1000);
            SticksSlider primary = combined.OfType<SticksSlider>().Single();
            SticksHitObject previous = combined.Last(note => note.Side == accent.Side && note.StartTime < accent.StartTime);
            SticksHitObject originalPrevious = duet.Last(note => note.Side == accent.Side && note.StartTime < accent.StartTime);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(standard.Any(note => note.StartTime == accent.StartTime), Is.False);
                Assert.That(combined.Length, Is.GreaterThan(standard.Length));
                Assert.That(accent.Side, Is.Not.EqualTo(primary.Side));
                Assert.That(accent.StartTime, Is.InRange(primary.StartTime, primary.EndTime));
                Assert.That(accent.Angle, Is.Not.EqualTo(originalAccent.Angle));
                assertWiderSourceTurn(originalPrevious, originalAccent, previous, accent);
            });
        }

        [Test]
        public void TestParityRotatesExactInterleavedPathsWithoutChangingTheirTimingOrTurns()
        {
            Beatmap<HitObject> source = map(slider(300, 10, 270), circle(600, 0), circle(900, 180),
                circle(1200, 65), circle(1500, 130), circle(1800, 40), circle(2250, 140));
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 600 });
            SticksHitObject[] duet = convert(source, SticksConversionMode.Duet);
            SticksHitObject[] combined = assertComposition(source, duet);
            SticksSlider[] voices = combined.OfType<SticksSlider>().Where(note => note.StartTime >= 600).ToArray();

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(voices, Has.Length.EqualTo(2));
                Assert.That(voices.Select(note => note.StartTime), Is.EqualTo(new[] { 600d, 900 }));
                Assert.That(voices.Select(note => note.EndTime), Is.EqualTo(new[] { 1800d, 2250 }));
                Assert.That(voices[0].SegmentArcAngles, Is.EqualTo(new[] { 65f, -25 }).Within(0.001));
                Assert.That(voices[1].SegmentArcAngles, Is.EqualTo(new[] { -50f, 10 }).Within(0.001));
                Assert.That(voices[0].SegmentDurationWeights, Is.EqualTo(new[] { 0.5, 0.5 }));
                Assert.That(voices[1].SegmentDurationWeights, Is.EqualTo(new[] { 600d / 1350, 750d / 1350 }).Within(0.000001));
                Assert.That(voices.Select(note => note.Side).Distinct().Count(), Is.EqualTo(2));
                Assert.That(voices.Any(note => Math.Abs(note.Angle - duet.Single(original => original.StartTime == note.StartTime).Angle) > 0.001), Is.True);
            });

            foreach (SticksSlider converted in voices)
            {
                SticksSlider original = duet.OfType<SticksSlider>().Single(note => note.StartTime == converted.StartTime);
                float rotation = converted.Angle - original.Angle;
                converted.ApplyDefaults(source.ControlPointInfo, source.Difficulty);
                original.ApplyDefaults(source.ControlPointInfo, source.Difficulty);
                Assert.That(converted.NestedHitObjects.Select(note => note.StartTime), Is.EqualTo(original.NestedHitObjects.Select(note => note.StartTime)));
                for (int segment = 0; segment < converted.SegmentCount; segment++)
                {
                    foreach (double time in new[]
                             {
                                 original.SegmentStartTimeAt(segment),
                                 original.SegmentStartTimeAt(segment) + original.SegmentDurationAt(segment) * 0.5,
                                 original.SegmentEndTimeAt(segment),
                             })
                        Assert.That(converted.AngleAt(time) - original.AngleAt(time), Is.EqualTo(rotation).Within(0.001));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestNativeSliderPartnersUseBothStickHistoriesAndFinalRepeatEndpoints(bool disableReversals)
        {
            NativeSlider native = slider(1000, 2000);
            native.RepeatCount = 1;
            native.Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 60) };
            native.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 31) });
            native.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 42) });
            native.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_FINISH, volume: 53) });
            Beatmap<HitObject> source = map(circle(500, 0), circle(500, 0), native, circle(3500, 0), circle(3500, 0));
            SticksHitObject[] duet = convert(source, SticksConversionMode.Duet, disableReversals);
            SticksHitObject[] combined = assertComposition(source, duet, disableReversals);
            SticksSlider[] sliders = combined.OfType<SticksSlider>().ToArray();

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(sliders, Has.Length.EqualTo(2));
                Assert.That(sliders.Select(note => note.Side).Distinct().Count(), Is.EqualTo(2));
                Assert.That(sliders.Select(note => note.RepeatCount), Has.All.EqualTo(disableReversals ? 0 : 1));
                Assert.That(combined.Select(note => note.Angle), Is.Not.EqualTo(duet.Select(note => note.Angle)));
            });

            foreach (SticksSlider convertedSlider in sliders)
            {
                SticksHitObject before = combined.Single(note => note.Side == convertedSlider.Side && note.StartTime == 500);
                SticksHitObject after = combined.Single(note => note.Side == convertedSlider.Side && note.StartTime == 3500);
                SticksSlider originalSlider = duet.OfType<SticksSlider>().Single(note => note.Side == convertedSlider.Side);
                SticksHitObject originalBefore = duet.Single(note => note.Side == convertedSlider.Side && note.StartTime == 500);
                SticksHitObject originalAfter = duet.Single(note => note.Side == convertedSlider.Side && note.StartTime == 3500);
                float rotation = convertedSlider.Angle - originalSlider.Angle;

                NUnitCompatibility.Multiple(() =>
                {
                    assertWiderSourceTurn(originalBefore, originalSlider, before, convertedSlider);
                    assertWiderSourceTurn(originalSlider, originalAfter, convertedSlider, after);
                    Assert.That(convertedSlider.NodeSamples.Select(samples => samples.Single().Volume),
                        Is.EqualTo(disableReversals ? new[] { 31, 53 } : new[] { 31, 42, 53 }));
                    foreach (double time in new[] { 1000d, 1500, 2000, 2500, 3000 })
                        Assert.That(convertedSlider.AngleAt(time) - originalSlider.AngleAt(time), Is.EqualTo(rotation).Within(0.001));
                });
            }
        }

        [Test]
        public void TestDefaultConversionKeepsFormerDuetPatternsWithCounterpoint()
        {
            Beatmap<HitObject> source = chordPhrase();
            source.HitObjects.Add(slider(5000, 1000));
            addHighDifficultySection(source);
            SticksHitObject[] legacy = convert(source, SticksConversionMode.Standard);
            SticksHitObject[] expected = new SticksBeatmapConverter(source, new SticksRuleset()) { UseCounterpoint = true }.Convert()
                .HitObjects.Cast<SticksHitObject>().ToArray();
            SticksHitObject[] converted = new SticksBeatmapConverter(source, new SticksRuleset()).Convert()
                .HitObjects.Cast<SticksHitObject>().ToArray();

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(signature(converted), Is.EqualTo(signature(expected)));
                Assert.That(chordCount(converted), Is.GreaterThan(chordCount(legacy)));
                Assert.That(converted.OfType<SticksSlider>().Count(note => note.StartTime == 5000), Is.EqualTo(2));
            });
        }

        [TestCase("DU", SticksConversionMode.Duet)]
        [TestCase("PD", SticksConversionMode.ParityDuet)]
        public void TestRetiredModsRemainLoadableForSavedScores(string acronym, SticksConversionMode mode)
        {
            var ruleset = new SticksRuleset();
            Mod mod = ruleset.CreateModFromAcronym(acronym);
            Beatmap<HitObject> source = chordPhrase();
            var converter = new SticksBeatmapConverter(source, ruleset);
            ((IApplicableToBeatmapConverter)mod).ApplyToBeatmapConverter(converter);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(mod.Type, Is.EqualTo(ModType.System));
                Assert.That(mod.Ranked, Is.False);
                Assert.That(converter.UseCounterpoint, Is.False, "Retired mods must retain their historical replay conversion.");
                Assert.That(ruleset.GetModsFor(ModType.Conversion).Select(candidate => candidate.Acronym), Does.Not.Contain(acronym));
                Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()),
                    Is.EqualTo(signature(convert(source, mode))));
            });
        }

        [Test]
        public void TestReusingConverterAcrossModesDoesNotLeakParityOrDuetAdditions()
        {
            Beatmap<HitObject> source = chordPhrase();
            source.HitObjects.Add(slider(5000, 500));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = SticksConversionMode.Standard, UseCounterpoint = false };
            string[] standard = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            new SticksModDuet().ApplyToBeatmapConverter(converter);
            string[] duet = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            new SticksModParityDuet().ApplyToBeatmapConverter(converter);
            string[] combined = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(combined));
            new SticksModParity().ApplyToBeatmapConverter(converter);
            converter.Convert();
            new SticksModParityDuet().ApplyToBeatmapConverter(converter);
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(combined));
            new SticksModDuet().ApplyToBeatmapConverter(converter);
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(duet));
            converter.ConversionMode = SticksConversionMode.Standard;
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(standard));
            Assert.That(source.HitObjects, Has.Count.EqualTo(6));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestGameplayPipelineUsesTwoStickBaseBeforeCreatingSliderNestedObjects(bool parity)
        {
            Beatmap<HitObject> source = map(circle(500, 0), circle(500, 0), slider(1000, 2000));
            addHighDifficultySection(source);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            if (parity)
                new SticksModParity().ApplyToBeatmapConverter(converter);
            SticksHitObject[] expected = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            foreach (SticksHitObject note in expected)
                note.ApplyDefaults(source.ControlPointInfo, source.Difficulty);
            var working = new FlatWorkingBeatmap(source);
            SticksHitObject[] playable = working.GetPlayableBeatmap(new SticksRuleset().RulesetInfo,
                parity ? new Mod[] { new SticksModParity() } : Array.Empty<Mod>(), CancellationToken.None).HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.That(signature(playable), Is.EqualTo(signature(expected)));
            Assert.That(playable.OfType<SticksSlider>().Count(), Is.EqualTo(2));
            foreach (SticksSlider converted in playable.OfType<SticksSlider>())
            {
                SticksSlider original = expected.OfType<SticksSlider>().Single(note => note.Side == converted.Side);
                Assert.That(converted.NestedHitObjects, Is.Not.Empty);
                Assert.That(signature(converted.NestedHitObjects.Cast<SticksHitObject>()),
                    Is.EqualTo(signature(original.NestedHitObjects.Cast<SticksHitObject>())));
            }
        }

        private static SticksHitObject[] assertComposition(Beatmap<HitObject> source, SticksHitObject[] duet, bool disableReversals = false)
        {
            SticksHitObject[] expected = convert(source, SticksConversionMode.Duet, disableReversals);
            SticksParityConversion.Apply(expected, source, CancellationToken.None);
            SticksBeatmapConverter.AlignNearbyChordHeads(expected);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { DisableReversals = disableReversals };
            new SticksModParityDuet().ApplyToBeatmapConverter(converter);
            SticksHitObject[] combined = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(combined.Select(shape), Is.EqualTo(duet.Select(shape)), "Duet timing, stick assignment, paths and samples must survive parity.");
                Assert.That(signature(combined), Is.EqualTo(signature(expected)), "Parity must run after all Duet objects have been emitted.");
            });
            return combined;
        }

        private static string[] signature(IEnumerable<SticksHitObject> notes) => notes.Select(note =>
            $"{shape(note)}:{note.Angle}").ToArray();

        private static string shape(SticksHitObject note) =>
            $"{note.GetType().Name}:{note.StartTime}:{note.Side}:{(note is IHasDuration duration ? duration.Duration : 0)}:{samples(note.Samples)}:"
            + (note is SticksSlider sliderObject
                ? $"{sliderObject.RepeatCount}:{sliderObject.HasCustomSegments}:{string.Join(",", sliderObject.SegmentArcAngles)}:"
                  + $"{string.Join(",", sliderObject.SegmentDurationWeights ?? Array.Empty<double>())}:{string.Join(";", sliderObject.NodeSamples.Select(samples))}"
                : string.Empty);

        private static string samples(IEnumerable<HitSampleInfo> samples) => string.Join(",", samples.Select(sample => $"{sample.Name}:{sample.Volume}"));
        private static void assertWiderSourceTurn(SticksHitObject originalPrevious, SticksHitObject originalNext,
                                                SticksHitObject convertedPrevious, SticksHitObject convertedNext)
        {
            float sourceTurn = SticksHitObject.DeltaAngle(endpoint(originalPrevious), originalNext.Angle);
            float convertedTurn = SticksHitObject.DeltaAngle(endpoint(convertedPrevious), convertedNext.Angle);
            if (Math.Abs(sourceTurn) >= 179.999f)
                Assert.That(Math.Abs(convertedTurn), Is.EqualTo(180).Within(0.001));
            else
            {
                Assert.That(Math.Abs(convertedTurn), Is.GreaterThan(Math.Abs(sourceTurn)).And.LessThan(180),
                    "Parity should increase relative spacing from the actual previous endpoint without imposing a fixed minimum.");
                if (Math.Abs(sourceTurn) > 0.001f)
                    Assert.That(Math.Sign(convertedTurn), Is.EqualTo(Math.Sign(sourceTurn)));
            }
        }

        private static float endpoint(SticksHitObject note) => note is SticksSlider sliderObject ? sliderObject.AngleAt(sliderObject.EndTime) : note.Angle;
        private static int chordCount(IEnumerable<SticksHitObject> notes) => notes.GroupBy(note => note.StartTime).Count(group => group.Select(note => note.Side).Distinct().Count() == 2);

        private static SticksHitObject[] convert(Beatmap<HitObject> source, SticksConversionMode mode, bool disableReversals = false) =>
            new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = mode, DisableReversals = disableReversals, UseCounterpoint = false }
                .Convert().HitObjects.Cast<SticksHitObject>().ToArray();

        private static void addHighDifficultySection(Beatmap<HitObject> source)
        {
            // Keep full-arrangement pipeline fixtures above the beginner range using
            // actual source difficulty, without relying on their OD or cached stars.
            for (int i = 0; i < 96; i++)
                source.HitObjects.Add(circle(20000 + i * 125, i % 2 == 0 ? 0 : 180));
            Assert.That(SticksConversionCoordinationAllowance.CalculateSourceStars(source, CancellationToken.None), Is.GreaterThanOrEqualTo(3));
        }

        private static Beatmap<HitObject> chordPhrase()
        {
            Beatmap<HitObject> source = map(slider(0, 100, 270), circle(500, 0), circle(1000, 10), circle(1500, 20), circle(2000, 30));
            source.HitObjects[1].Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 42) };
            source.HitObjects[3].Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_FINISH, volume: 64) };
            return source;
        }

        private static Beatmap<HitObject> map(params HitObject[] objects)
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.AddRange(objects);
            return source;
        }

        private static SourceCircle circle(double time, float angle) => new SourceCircle { StartTime = time, Position = position(angle) };
        private static NativeSlider slider(double time, double duration, float angle = 0) => new NativeSlider { StartTime = time, Duration = duration, Position = position(angle) };
        private static Vector2 position(float angle) => SticksBeatmapConverter.STANDARD_CENTRE + 160 * new Vector2(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180));

        private class SourceCircle : HitObject, IHasPosition
        {
            public Vector2 Position { get; set; }
            public float X { get => Position.X; set => Position = new Vector2(value, Y); }
            public float Y { get => Position.Y; set => Position = new Vector2(X, value); }
        }

        private sealed class NativeSlider : SourceCircle, IHasDuration, IHasRepeats
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
            public int RepeatCount { get; set; }
            public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();
        }
    }
}
