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

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksNativeConversionBypassTest
    {
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void NativeGeometryAndPatternsRemainExact(bool genericHitObjectContainer, bool parity, bool encore)
        {
            SticksHitObject[] notes = nativeNotes();
            IBeatmap source;
            if (genericHitObjectContainer)
            {
                var beatmap = new Beatmap<HitObject>();
                beatmap.HitObjects.AddRange(notes);
                source = beatmap;
            }
            else
            {
                var beatmap = new Beatmap<SticksHitObject>();
                beatmap.HitObjects.AddRange(notes);
                source = beatmap;
            }

            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            string[] expected = signature(notes);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            if (parity)
                new SticksModParity().ApplyToBeatmapConverter(converter);
            if (encore)
                new SticksModEncore().ApplyToBeatmapConverter(converter);

            Assert.That(converter.IsAuthoredCarrier, Is.False, "This covers native objects without carrier metadata.");
            for (int iteration = 0; iteration < 2; iteration++)
            {
                SticksHitObject[] converted = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(converted.OfType<SticksHold>(), Is.Empty, "Legacy native holds become stationary sliders.");
                    Assert.That(signature(converted), Is.EqualTo(expected), "Authored geometry must bypass procedural pattern and angle changes.");
                    Assert.That(signature(notes), Is.EqualTo(expected), "Conversion must not mutate its source's gameplay geometry.");
                    Assert.That(converted.Single(note => note.StartTime == 1000 && note.Side == StickSide.Left).SyncedNoteAngle,
                        Is.EqualTo(19), "Shared visual links should still describe the unchanged authored chord.");
                });
            }
        }

        private static SticksHitObject[] nativeNotes()
        {
            var timedSlider = new SticksSlider
            {
                StartTime = 5000,
                Duration = 1600,
                Side = StickSide.Left,
                Angle = 78,
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 41) },
            };
            timedSlider.SetTimedSegments(new[] { 50f, -35, 60 }, new[] { 3d, 1, 4 });
            timedSlider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 32) });
            timedSlider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, volume: 43) });
            timedSlider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 54) });
            timedSlider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_FINISH, volume: 65) });

            return new SticksHitObject[]
            {
                // A nearly coincident chord must keep its authored directions, rather than
                // receiving the cleanup used on procedurally generated chords.
                new SticksSlider { StartTime = 1000, Duration = 1200, Angle = 15, ArcAngle = 75, RepeatCount = 1 },
                new SticksFlick { StartTime = 1000, Angle = 19, Side = StickSide.Right },
                new SticksFlick { StartTime = 2400, Angle = 17 },
                new SticksFlick { StartTime = 2700, Angle = 19 },
                // A long isolated slider must not acquire a procedural opposite-stick partner.
                timedSlider,
                new SticksClick { StartTime = 7000, Side = StickSide.Right },
                new SticksHold { StartTime = 8000, Duration = 1500, Angle = 63, Side = StickSide.Right },
            };
        }

        private static string[] signature(IEnumerable<SticksHitObject> notes) => notes.Select(note =>
            $"{(note is SticksHold ? nameof(SticksSlider) : note.GetType().Name)}:{note.StartTime:R}:{note.GetEndTime():R}:{note.Side}:{note.Angle:R}:{samples(note.Samples)}:"
            + (note is SticksSlider slider
                ? $"{slider.RepeatCount}:{slider.HasTimedSegments}:{string.Join(',', slider.SegmentArcAngles)}:"
                  + $"{string.Join(',', slider.SegmentDurationWeights ?? Array.Empty<double>())}:{string.Join(';', slider.NodeSamples.Select(samples))}"
                : note is SticksHold ? "0:False:0::" : string.Empty)).ToArray();

        private static string samples(IEnumerable<HitSampleInfo> samples) =>
            string.Join(',', samples.Select(sample => $"{sample.Name}:{sample.Bank}:{sample.Volume}"));
    }
}
