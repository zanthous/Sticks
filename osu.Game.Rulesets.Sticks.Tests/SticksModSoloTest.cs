using System;
using System.Collections.Generic;
using System.Linq;
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
    public class SticksModSoloTest
    {
        [Test]
        public void SoloIsAvailableAsAConversionMod()
        {
            var ruleset = new SticksRuleset();
            Assert.That(ruleset.GetModsFor(ModType.Conversion), Has.Exactly(1).TypeOf<SticksModSolo>());
            Assert.That(ruleset.CreateModFromAcronym("SO"), Is.TypeOf<SticksModSolo>());
            Assert.That(new SticksModSolo().Ranked, Is.False);
        }

        [Test]
        public void SoloKeepsSourceHeadsAndSlidersWithoutArrangementsAndCanBeDisabledAgain()
        {
            Beatmap<HitObject> source = phrase();
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { LimitBeginnerCoordination = false };
            string[] baseline = signature(convert(converter));
            new SticksModSolo().ApplyToBeatmapConverter(converter);
            SticksHitObject[] solo = convert(converter);

            Assert.That(solo.Select(note => note.StartTime), Is.EqualTo(source.HitObjects.Select(note => note.StartTime)));
            Assert.That(solo.Select(note => note.Side).Distinct().Count(), Is.EqualTo(2), "Sequential use of both sticks remains available.");
            Assert.That(baseline.Length, Is.GreaterThan(solo.Length), "The fixture must normally receive extra arrangement notes.");
            assertNoOverlap(solo);
            for (int i = 0; i < solo.Length; i++)
            {
                var original = (SourceCircle)source.HitObjects[i];
                Vector2 position = original.Position - SticksBeatmapConverter.STANDARD_CENTRE;
                float angle = MathF.Atan2(position.Y, position.X) * 180 / MathF.PI;
                Assert.That(SticksHitObject.DeltaAngle(angle, solo[i].Angle), Is.Zero.Within(0.001));
                if (original is SourceSlider slider)
                {
                    Assert.That(solo[i], Is.TypeOf<SticksSlider>());
                    Assert.That(((SticksSlider)solo[i]).Duration, Is.EqualTo(slider.Duration));
                }
                else
                    Assert.That(solo[i], Is.TypeOf<SticksFlick>());
            }

            Assert.That(signature(convert(converter)), Is.EqualTo(signature(solo)));
            converter.SoloConversion = false;
            Assert.That(signature(convert(converter)), Is.EqualTo(baseline));
        }

        [TestCase(1500)]
        [TestCase(1000)]
        [TestCase(0.02)]
        public void SourceOverlapsKeepLaterAttacksAndTrimThePathWithoutSpeedingItUp(double nextHead)
        {
            var original = new SourceSlider { StartTime = 0, Duration = 4000, RepeatCount = 3, Position = new Vector2(416, 192) };
            original.Samples.Add(new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 60));
            for (int i = 0; i < 5; i++)
                original.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 10 + i) });

            var isolated = new SticksBeatmapConverter(map(original), new SticksRuleset()) { SoloConversion = true };
            var fullPath = (SticksSlider)convert(isolated).Single();
            Assert.That(fullPath.RepeatCount, Is.EqualTo(3));

            Beatmap<HitObject> source = map(original, circle(0, 90), circle(nextHead, 45),
                new SourceSlider { StartTime = nextHead + 100, Duration = 2000, Position = new Vector2(256, 32) },
                circle(nextHead + 200, 180), circle(nextHead + 300, 270));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { SoloConversion = true };
            SticksHitObject[] notes = convert(converter);
            var clipped = (SticksSlider)notes[0];

            assertNoOverlap(notes);
            Assert.That(notes.Select(note => note.StartTime), Is.EqualTo(source.HitObjects.Select(note => note.StartTime).Distinct()));
            Assert.That(clipped.EndTime, Is.EqualTo(nextHead));
            for (int i = 0; i <= 20; i++)
            {
                double time = nextHead * i / 20;
                Assert.That(clipped.AngleAt(time), Is.EqualTo(fullPath.AngleAt(time)).Within(0.001));
            }
            Assert.That(clipped.NodeSamples[0].Single().Volume, Is.EqualTo(10));
            Assert.That(clipped.NodeSamples.Last().Single().Volume, Is.EqualTo(nextHead == 1000 ? 11 : 60),
                "Only a release on a real source node keeps that node's sound.");
            Assert.That(original.Duration, Is.EqualTo(4000));
            Assert.That(original.RepeatCount, Is.EqualTo(3));
            Assert.That(original.NodeSamples, Has.Count.EqualTo(5));

            clipped.ApplyDefaults(source.ControlPointInfo, source.Difficulty);
            Assert.That(clipped.NestedHitObjects.All(note => note.GetEndTime() <= nextHead + 0.001), Is.True);
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(clipped), out var decoded), Is.True);
            Assert.That(((SticksSlider)decoded).Duration, Is.EqualTo(clipped.Duration));
        }

        [Test]
        public void SoloCombinesWithParityEncoreAndDifficultyAdjustInEitherOrder()
        {
            Beatmap<HitObject> source = phrase();
            var adjust = new SticksModDifficultyAdjust();
            adjust.DisableReversals.Value = true;
            IApplicableToBeatmapConverter[] mods = { new SticksModSolo(), new SticksModParity(), new SticksModEncore(), adjust };
            var forward = new SticksBeatmapConverter(source, new SticksRuleset());
            var reverse = new SticksBeatmapConverter(source, new SticksRuleset());
            foreach (var mod in mods)
                mod.ApplyToBeatmapConverter(forward);
            foreach (var mod in mods.Reverse())
                mod.ApplyToBeatmapConverter(reverse);

            SticksHitObject[] notes = convert(forward);
            Assert.That(signature(convert(reverse)), Is.EqualTo(signature(notes)));
            assertNoOverlap(notes);
            Assert.That(notes.OfType<SticksClick>().Any(), Is.True, "Encore still converts isolated accents.");
            Assert.That(notes.OfType<SticksSlider>().All(slider => slider.RepeatCount == 0), Is.True);

            var solo = new SticksBeatmapConverter(source, new SticksRuleset()) { SoloConversion = true, AddClickNotes = true, DisableReversals = true };
            Assert.That(notes.Select(note => note.Angle), Is.Not.EqualTo(convert(solo).Select(note => note.Angle)), "Parity still changes directions.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SoloLeavesAuthoredSimultaneousObjectsUnchanged(bool carrier)
        {
            SticksHitObject[] authored =
            {
                new SticksSlider { StartTime = 1000, Duration = 2000, ArcAngle = 90, Side = StickSide.Left },
                new SticksSlider { StartTime = 1000, Duration = 2000, ArcAngle = -90, Side = StickSide.Right },
                new SticksFlick { StartTime = 2000, Angle = 270, Side = StickSide.Right },
            };
            IBeatmap source = carrier ? map(authored.Select(SticksAuthoredBeatmapCodec.CreateLegacyProxy).ToArray()) : new Beatmap<SticksHitObject> { HitObjects = authored.ToList() };
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            string[] baseline = signature(convert(converter));
            new SticksModSolo().ApplyToBeatmapConverter(converter);
            Assert.That(signature(convert(converter)), Is.EqualTo(baseline));
        }

        private static void assertNoOverlap(SticksHitObject[] notes)
        {
            for (int i = 1; i < notes.Length; i++)
            {
                Assert.That(notes[i].StartTime, Is.GreaterThan(notes[i - 1].StartTime));
                Assert.That(notes[i].StartTime, Is.GreaterThanOrEqualTo(notes[i - 1].GetEndTime()));
            }
        }

        private static SticksHitObject[] convert(SticksBeatmapConverter converter) => converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

        private static string[] signature(SticksHitObject[] notes) => notes.Select(note =>
            $"{note.GetType().Name}:{note.StartTime}:{note.GetEndTime()}:{note.Side}:{note.Angle}:"
            + (note is SticksSlider slider ? $"{string.Join(',', slider.SegmentArcAngles)}:{string.Join(',', slider.SegmentDurationWeights ?? Array.Empty<double>())}" : "")).ToArray();

        private static Beatmap<HitObject> phrase()
        {
            var source = map(circle(0, 0));
            for (int i = 0; i < 8; i++)
            {
                double start = 1000 + i * 5000;
                source.HitObjects.Add(new SourceSlider { StartTime = start, Duration = 2000, RepeatCount = 1, Position = new Vector2(416, 192) });
                source.HitObjects.Add(circle(start + 2500, 20));
                source.HitObjects.Add(circle(start + 3000, 30));
                source.HitObjects.Add(circle(start + 3500, 40));
            }
            return source;
        }

        private static Beatmap<HitObject> map(params HitObject[] notes)
        {
            var source = new Beatmap<HitObject>();
            source.Difficulty.OverallDifficulty = 7;
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.AddRange(notes);
            return source;
        }

        private static SourceCircle circle(double time, float angle) => new SourceCircle
        {
            StartTime = time,
            Position = SticksBeatmapConverter.STANDARD_CENTRE + new Vector2(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180)) * 160,
        };

        private class SourceCircle : HitObject, IHasPosition
        {
            public Vector2 Position { get; set; }
            public float X { get => Position.X; set => Position = new Vector2(value, Y); }
            public float Y { get => Position.Y; set => Position = new Vector2(X, value); }
        }

        private class SourceSlider : SourceCircle, IHasDuration, IHasRepeats
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
            public int RepeatCount { get; set; }
            public IList<IList<HitSampleInfo>> NodeSamples { get; } = new List<IList<HitSampleInfo>>();
        }
    }
}
