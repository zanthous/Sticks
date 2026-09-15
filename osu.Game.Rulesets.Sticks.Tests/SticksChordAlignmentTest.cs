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
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksChordAlignmentTest
    {
        [TestCase(15, 19, 17)]
        [TestCase(15, 20, 17.5f)]
        [TestCase(359, 1, 0)]
        [TestCase(0, 360, 0)]
        public void TestNearbyChordUsesCircularMidpointRegardlessOfInputOrder(float leftAngle, float rightAngle, float expected)
        {
            foreach (bool reverse in new[] { false, true })
            {
                SticksHitObject[] chord = { flick(0, leftAngle), flick(0, rightAngle, StickSide.Right) };
                SticksBeatmapConverter.AlignNearbyChordHeads(reverse ? chord.Reverse() : chord);
                Assert.Multiple(() =>
                {
                    Assert.That(chord.Select(note => note.Angle), Is.EqualTo(new[] { expected, expected }));
                    Assert.That(Math.Abs(SticksHitObject.DeltaAngle(leftAngle, chord[0].Angle)), Is.LessThanOrEqualTo(2.5f));
                    Assert.That(Math.Abs(SticksHitObject.DeltaAngle(rightAngle, chord[1].Angle)), Is.LessThanOrEqualTo(2.5f));
                });

                SticksBeatmapConverter.AlignNearbyChordHeads(chord);
                Assert.That(chord.Select(note => note.Angle), Is.EqualTo(new[] { expected, expected }), "Repeated conversion cleanup must be stable.");
            }
        }

        [TestCase(5.001f)]
        [TestCase(45)]
        [TestCase(180)]
        public void TestDistinctChordDirectionsRemainUnchanged(float separation)
        {
            SticksHitObject[] chord = { flick(0, 0), flick(0, separation, StickSide.Right) };
            SticksBeatmapConverter.AlignNearbyChordHeads(chord);
            Assert.That(chord.Select(note => note.Angle), Is.EqualTo(new[] { 0f, separation }));
        }

        [TestCase(0.009, true)]
        [TestCase(0.01, false)]
        [TestCase(1, false)]
        public void TestOnlySimultaneousHeadsAreAligned(double offset, bool aligned)
        {
            SticksHitObject[] objects = { flick(0, 15), flick(offset, 19, StickSide.Right) };
            SticksBeatmapConverter.AlignNearbyChordHeads(objects.Reverse());
            Assert.Multiple(() =>
            {
                Assert.That(objects.Select(note => note.Angle), Is.EqualTo(aligned ? new[] { 17f, 17 } : new[] { 15f, 19 }));
                Assert.That(objects.Select(note => note.StartTime), Is.EqualTo(new[] { 0d, offset }));
            });
        }

        [Test]
        public void TestSameStickAndThreeHeadGroupsRemainUnchanged()
        {
            SticksHitObject[] objects =
            {
                flick(0, 15), flick(0, 19),
                flick(1000, 15), flick(1000, 17, StickSide.Right), flick(1000, 19),
            };
            float[] original = objects.Select(note => note.Angle).ToArray();
            SticksBeatmapConverter.AlignNearbyChordHeads(objects);
            Assert.That(objects.Select(note => note.Angle), Is.EqualTo(original));
        }

        [Test]
        public void TestGroupingIsAnchoredToFirstHeadInsteadOfChainingNearTimestamps()
        {
            SticksHitObject[] objects = { flick(0, 15), flick(0.008, 19, StickSide.Right), flick(0.016, 20) };
            SticksBeatmapConverter.AlignNearbyChordHeads(objects.Reverse());
            SticksBeatmapConverter.AssignSyncedNoteLinks(objects);
            Assert.Multiple(() =>
            {
                Assert.That(objects.Select(note => note.Angle), Is.EqualTo(new[] { 17f, 17, 20 }));
                Assert.That(objects[0].SyncedNoteAngle, Is.EqualTo(17));
                Assert.That(objects[2].SyncedNoteSide, Is.Null);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestSliderAlignmentRotatesCompletePathAndPreservesTimingAndSamples(bool customSegments)
        {
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 825,
                Angle = 359,
                Side = StickSide.Left,
                ArcAngle = 60,
                RepeatCount = 2,
                Samples = new[] { new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: 60) },
            };
            if (customSegments)
                slider.SetCustomSegments(new[] { 90f, -30, 45 });
            for (int i = 0; i <= slider.SegmentCount; i++)
                slider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 30 + i) });

            float[] arcs = slider.SegmentArcAngles.ToArray();
            double[] segmentStarts = Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentStartTimeAt).ToArray();
            double[] segmentEnds = Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentEndTimeAt).ToArray();
            double[] times = Enumerable.Range(0, 34).Select(index => slider.StartTime + slider.Duration * index / 33).ToArray();
            float[] angles = times.Select(slider.AngleAt).ToArray();
            var samples = slider.Samples;
            var nodeSamples = slider.NodeSamples.ToArray();
            SticksHitObject[] chord = { slider, flick(1000, 1, StickSide.Right) };

            SticksBeatmapConverter.AlignNearbyChordHeads(chord);

            Assert.Multiple(() =>
            {
                Assert.That(chord.Select(note => note.Angle), Is.EqualTo(new[] { 0f, 0 }));
                Assert.That(slider.SegmentArcAngles, Is.EqualTo(arcs));
                Assert.That(slider.HasCustomSegments, Is.EqualTo(customSegments));
                Assert.That(slider.RepeatCount, Is.EqualTo(2));
                Assert.That(slider.StartTime, Is.EqualTo(1000));
                Assert.That(slider.Duration, Is.EqualTo(825));
                Assert.That(Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentStartTimeAt), Is.EqualTo(segmentStarts));
                Assert.That(Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentEndTimeAt), Is.EqualTo(segmentEnds));
                Assert.That(slider.Samples, Is.SameAs(samples));
                for (int i = 0; i < nodeSamples.Length; i++)
                    Assert.That(slider.NodeSamples[i], Is.SameAs(nodeSamples[i]));
                for (int i = 0; i < times.Length; i++)
                    Assert.That(SticksHitObject.DeltaAngle(angles[i], slider.AngleAt(times[i])), Is.EqualTo(1).Within(0.001));
            });
        }

        [TestCase("")]
        [TestCase("PA")]
        [TestCase("DU")]
        [TestCase("PD")]
        public void TestGameplayConversionAlignsChordBeforeNestedHeadsAreCreated(string acronym)
        {
            // A slider and a simultaneous circle retain their source directions during
            // normal chord planning, reproducing an almost-shared head in every mode.
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            source.HitObjects.Add(new SourceSlider { StartTime = 1000, Duration = 500, Position = position(15) });
            source.HitObjects.Add(new SourceCircle { StartTime = 1000, Position = position(19) });
            var ruleset = new SticksRuleset();
            Mod[] mods = acronym.Length == 0 ? Array.Empty<Mod>() : new[] { ruleset.CreateModFromAcronym(acronym) };
            SticksHitObject[] chord = new FlatWorkingBeatmap(source)
                .GetPlayableBeatmap(ruleset.RulesetInfo, mods, CancellationToken.None)
                .HitObjects.Cast<SticksHitObject>().ToArray();

            Assert.That(chord, Has.Length.EqualTo(2));
            SticksSlider slider = chord.OfType<SticksSlider>().Single();
            SticksHitObject owner = chord.Single(note => note.SyncedNoteSide.HasValue);
            SticksHitObject partner = chord.Single(note => note.Side != owner.Side);
            Assert.Multiple(() =>
            {
                Assert.That(chord[0].Angle, Is.EqualTo(17).Within(0.001));
                Assert.That(chord[1].Angle, Is.EqualTo(chord[0].Angle), "Shared rendering needs exactly equal final angles.");
                Assert.That(owner.SyncedNoteAngle, Is.EqualTo(partner.Angle));
                Assert.That(slider.NestedHitObjects.OfType<SticksSliderHead>().Single().Angle, Is.EqualTo(slider.Angle));
            });
        }

        [TestCase(SticksConversionMode.Standard)]
        [TestCase(SticksConversionMode.Parity)]
        [TestCase(SticksConversionMode.Duet)]
        [TestCase(SticksConversionMode.ParityDuet)]
        public void TestAuthoredNearbyChordKeepsItsExactDirections(SticksConversionMode mode)
        {
            var source = new Beatmap<HitObject>();
            source.HitObjects.AddRange(new[] { flick(1000, 15), flick(1000, 19, StickSide.Right) }
                                      .Select(SticksAuthoredBeatmapCodec.CreateLegacyProxy));
            var converter = new SticksBeatmapConverter(source, new SticksRuleset()) { ConversionMode = mode };
            SticksHitObject[] chord = converter.Convert().HitObjects.Cast<SticksHitObject>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(converter.IsAuthoredCarrier, Is.True);
                Assert.That(chord.Select(note => note.Angle), Is.EqualTo(new[] { 15f, 19 }));
                Assert.That(chord.Single(note => note.SyncedNoteSide.HasValue).SyncedNoteAngle, Is.EqualTo(19));
            });
        }

        private static SticksFlick flick(double time, float angle, StickSide side = StickSide.Left) =>
            new SticksFlick { StartTime = time, Angle = angle, Side = side };

        private static Vector2 position(float angle) => SticksBeatmapConverter.STANDARD_CENTRE
            + 160 * new Vector2(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180));

        private class SourceCircle : HitObject, IHasPosition
        {
            public Vector2 Position { get; set; }
            public float X { get => Position.X; set => Position = new Vector2(value, Y); }
            public float Y { get => Position.Y; set => Position = new Vector2(X, value); }
        }

        private sealed class SourceSlider : SourceCircle, IHasDuration
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
        }
    }
}
