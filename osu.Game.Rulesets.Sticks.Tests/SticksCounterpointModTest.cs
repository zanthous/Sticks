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
    public class SticksCounterpointModTest
    {
        [Test]
        public void RetiredCounterpointRemainsLoadableAndCompatibleWithCurrentConversionMods()
        {
            var ruleset = new SticksRuleset();
            Mod[] visible = ruleset.GetModsFor(ModType.Conversion).ToArray();
            SticksModCounterpoint counterpoint = ruleset.GetModsFor(ModType.System).OfType<SticksModCounterpoint>().Single();
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(counterpoint.Ranked, Is.False);
                Assert.That(counterpoint.Type, Is.EqualTo(ModType.System));
                Assert.That(ruleset.CreateModFromAcronym("CP"), Is.TypeOf<SticksModCounterpoint>());
                Assert.That(visible.Select(mod => mod.Acronym), Does.Not.Contain("DU").And.Not.Contain("PD").And.Not.Contain("CP"));
                Assert.That(counterpoint.IncompatibleMods, Does.Contain(typeof(SticksModDuet)).And.Contain(typeof(SticksModParityDuet)));
            });
            foreach (Mod mod in new Mod[] { new SticksModParity(), new SticksModEncore(), new SticksModDifficultyAdjust() })
            {
                Assert.That(visible.Select(candidate => candidate.GetType()), Does.Contain(mod.GetType()));
                Assert.That(counterpoint.IncompatibleMods, Does.Not.Contain(mod.GetType()));
                Assert.That(mod.IncompatibleMods, Does.Not.Contain(typeof(SticksModCounterpoint)));
            }
        }

        [TestCase(SticksConversionMode.Standard)]
        [TestCase(SticksConversionMode.Parity)]
        [TestCase(SticksConversionMode.Duet)]
        [TestCase(SticksConversionMode.ParityDuet)]
        public void RetiredCounterpointEnablesItsPassWithoutReplacingBaseMode(SticksConversionMode mode)
        {
            var converter = new SticksBeatmapConverter(sourcePhrase(), new SticksRuleset()) { ConversionMode = mode, UseCounterpoint = false };
            Assert.That(converter.UseCounterpoint, Is.False);
            new SticksModCounterpoint().ApplyToBeatmapConverter(converter);
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(converter.UseCounterpoint, Is.True);
                Assert.That(converter.ConversionMode, Is.EqualTo(mode));
            });
        }

        [TestCase(null)]
        [TestCase(42f)]
        public void ConversionModOrderPreservesFlagsAndActualOutput(float? primaryHitAngle)
        {
            Beatmap<HitObject> source = sourcePhrase();
            string[] expected = null;
            foreach (int[] order in permutations(new[] { 0, 1, 2 }))
            {
                var adjust = new SticksModDifficultyAdjust();
                adjust.DisableReversals.Value = true;
                adjust.PrimaryHitAngle.Value = primaryHitAngle;
                IApplicableToBeatmapConverter[] mods =
                {
                    new SticksModParity(), new SticksModEncore(), adjust,
                };
                var converter = new SticksBeatmapConverter(source, new SticksRuleset());
                foreach (int index in order)
                    mods[index].ApplyToBeatmapConverter(converter);

                NUnitCompatibility.Multiple(() =>
                {
                    Assert.That(converter.UseCounterpoint, Is.True);
                    Assert.That(converter.ConversionMode, Is.EqualTo(SticksConversionMode.ParityDuet));
                    Assert.That(converter.AddClickNotes, Is.True);
                    Assert.That(converter.DisableReversals, Is.True);
                });
                string[] actual = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
                expected ??= actual;
                Assert.That(actual, Is.EqualTo(expected), $"Mod order {string.Join(',', order)} changed conversion.");
            }
        }

        [Test]
        public void DefaultAndParityUseTheCompleteCounterpointArrangement()
        {
            Beatmap<HitObject> source = sourcePhrase();
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            Assert.That(converter.UseCounterpoint, Is.True);
            SticksHitObject[] expected = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            var previousConverter = new SticksBeatmapConverter(source, new SticksRuleset()) { UseCounterpoint = false };
            string[] previousBase = signature(previousConverter.Convert().HitObjects.Cast<SticksHitObject>());
            new SticksModCounterpoint().ApplyToBeatmapConverter(previousConverter);
            Assert.That(signature(expected), Is.EqualTo(signature(previousConverter.Convert().HitObjects.Cast<SticksHitObject>())),
                "The new default must match the previous Counterpoint mod's complete arrangement.");
            Assert.That(signature(expected), Is.Not.EqualTo(previousBase), "The fixture must exercise Counterpoint's additional arrangement pass.");
            SticksParityConversion.Apply(expected, source, CancellationToken.None,
                readableChords: true, fullHitAngle: converter.CounterpointHitAngleFor(source.Difficulty));

            new SticksModParity().ApplyToBeatmapConverter(converter);
            SticksHitObject[] actual = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            Assert.That(signature(actual), Is.EqualTo(signature(expected)),
                "Parity must see every Counterpoint head and endpoint, resolve readable pairs together, and retain their actual history.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AuthoredObjectsRemainExactWithAllConversionMods(bool carrier)
        {
            SticksHitObject[] authored = authoredPhrase();
            IBeatmap source;
            if (carrier)
                source = map(authored.Select(SticksAuthoredBeatmapCodec.CreateLegacyProxy).ToArray());
            else
            {
                var native = new Beatmap<SticksHitObject>();
                native.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
                native.HitObjects.AddRange(authored);
                source = native;
            }

            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            string[] baseline = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            new SticksModCounterpoint().ApplyToBeatmapConverter(converter);
            new SticksModParity().ApplyToBeatmapConverter(converter);
            new SticksModEncore().ApplyToBeatmapConverter(converter);
            new SticksModDifficultyAdjust().ApplyToBeatmapConverter(converter);
            SticksHitObject[] converted = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(converter.IsAuthoredCarrier, Is.EqualTo(carrier));
                Assert.That(converted, Has.Length.EqualTo(authored.Length), "Authored maps must not receive generated accents or phrases.");
                Assert.That(signature(converted), Is.EqualTo(baseline));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConverterReuseDoesNotLeakCounterpointOrMutateTheSource(bool parity)
        {
            Beatmap<HitObject> source = sourcePhrase();
            HitObject[] originalObjects = source.HitObjects.ToArray();
            string[] sourceBefore = sourceSignature(source);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            if (parity)
                new SticksModParity().ApplyToBeatmapConverter(converter);
            converter.UseCounterpoint = false;
            string[] baseline = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());

            new SticksModCounterpoint().ApplyToBeatmapConverter(converter);
            string[] enabled = signature(converter.Convert().HitObjects.Cast<SticksHitObject>());
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(enabled));
            converter.UseCounterpoint = false;
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(baseline));
            converter.UseCounterpoint = true;
            Assert.That(signature(converter.Convert().HitObjects.Cast<SticksHitObject>()), Is.EqualTo(enabled));
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(source.HitObjects, Is.EqualTo(originalObjects));
                Assert.That(sourceSignature(source), Is.EqualTo(sourceBefore));
            });
        }

        private static IEnumerable<int[]> permutations(int[] values)
        {
            if (values.Length == 0)
            {
                yield return Array.Empty<int>();
                yield break;
            }
            foreach (int first in values)
            {
                foreach (int[] rest in permutations(values.Where(value => value != first).ToArray()))
                    yield return new[] { first }.Concat(rest).ToArray();
            }
        }

        private static string[] signature(IEnumerable<SticksHitObject> notes) => notes.Select(note =>
            $"{note.GetType().Name}:{note.StartTime}:{note.GetEndTime()}:{note.Side}:{note.Angle}:{samples(note.Samples)}:"
            + (note is SticksSlider slider
                ? $"{slider.RepeatCount}:{string.Join(',', slider.SegmentArcAngles)}:{string.Join(',', slider.SegmentDurationWeights ?? Array.Empty<double>())}:"
                  + string.Join(';', slider.NodeSamples.Select(samples))
                : string.Empty)).ToArray();

        private static string[] sourceSignature(Beatmap<HitObject> source) => source.HitObjects.Select(note =>
            $"{note.GetType().Name}:{note.StartTime}:{note.GetEndTime()}:{((IHasPosition)note).Position}:{samples(note.Samples)}:"
            + (note is IHasRepeats repeats ? $"{repeats.RepeatCount}:{string.Join(';', repeats.NodeSamples.Select(samples))}" : string.Empty)).ToArray();

        private static string samples(IEnumerable<HitSampleInfo> samples) => string.Join(',', samples.Select(sample => $"{sample.Name}:{sample.Volume}"));

        private static Beatmap<HitObject> sourcePhrase() => map(
            circle(0, 0), slider(1000, 2500, 30, 1), circle(4000, 160), circle(4750, 220),
            slider(6000, 2000, 270, 1), circle(8750, 210), circle(9500, 100),
            slider(11000, 3000, 90, 2), circle(14500, 230), circle(15500, 170));

        private static SticksHitObject[] authoredPhrase()
        {
            var slider = new SticksSlider { StartTime = 1000, Duration = 3000, Side = StickSide.Left, Angle = 13 };
            slider.SetTimedSegments(new[] { 60f, -35f, 70f }, new[] { 1.0, 2.0, 1.0 });
            slider.NodeSamples.Add(new List<HitSampleInfo> { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 41) });
            return new SticksHitObject[]
            {
                slider,
                new SticksFlick { StartTime = 2250, Side = StickSide.Right, Angle = 156 },
                new SticksClick { StartTime = 2500, Side = StickSide.Left },
                new SticksHold { StartTime = 5000, Duration = 2500, Side = StickSide.Right, Angle = 270 },
                new SticksFlick { StartTime = 6250, Side = StickSide.Left, Angle = 84 },
                new SticksFlick { StartTime = 8000, Side = StickSide.Left, Angle = 10 },
                new SticksFlick { StartTime = 8000, Side = StickSide.Right, Angle = 13 },
            };
        }

        private static Beatmap<HitObject> map(params HitObject[] objects)
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.Difficulty.OverallDifficulty = 7;
            source.HitObjects.AddRange(objects);
            return source;
        }

        private static SourceCircle circle(double time, float angle) => new SourceCircle
        {
            StartTime = time,
            Position = position(angle),
            Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 73) },
        };

        private static SourceSlider slider(double time, double duration, float angle, int repeats) => new SourceSlider
        {
            StartTime = time,
            Duration = duration,
            Position = position(angle),
            RepeatCount = repeats,
            Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_FINISH, volume: 61) },
        };

        private static Vector2 position(float angle) => SticksBeatmapConverter.STANDARD_CENTRE
            + new Vector2(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180)) * 160;

        private class SourceCircle : HitObject, IHasPosition
        {
            public Vector2 Position { get; set; }
            public float X { get => Position.X; set => Position = new Vector2(value, Y); }
            public float Y { get => Position.Y; set => Position = new Vector2(X, value); }
        }

        private sealed class SourceSlider : SourceCircle, IHasDuration, IHasRepeats
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
            public int RepeatCount { get; set; }
            public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();
        }
    }
}
