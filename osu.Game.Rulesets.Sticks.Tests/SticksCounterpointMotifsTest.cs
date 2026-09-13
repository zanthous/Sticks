using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksCounterpointMotifsTest
    {
        private static readonly Vector2[] phrase_positions =
        {
            new Vector2(12, 26), new Vector2(76, 26), new Vector2(92, 74), new Vector2(28, 58),
            new Vector2(60, 10), new Vector2(140, 42), new Vector2(124, 90), new Vector2(44, 122),
        };

        [TestCase(0)]
        [TestCase(37)]
        [TestCase(90)]
        [TestCase(183)]
        [TestCase(270)]
        [TestCase(359)]
        public void TestTranslatedRotatedAndTempoScaledPhraseMatches(float rotation)
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions);
            source.ControlPointInfo.Add(10000, new TimingControlPoint { BeatLength = 375 });
            addPhrase(source, 10000, 375, phrase_positions.Select(point => rotate(point, rotation) + new Vector2(210, 83)).ToArray());
            var motifs = analyse(source);

            Assert.Multiple(() =>
            {
                Assert.That(motifs.KeyAt(0), Is.Not.Null);
                Assert.That(motifs.KeyAt(8), Is.EqualTo(motifs.KeyAt(0)));
                Assert.That(motifs.OccurrencesAt(0), Is.EqualTo(2));
            });
        }

        [Test]
        public void TestSameRhythmWithDifferentGeometryDoesNotMatch()
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions);
            Vector2[] changed = phrase_positions.ToArray();
            changed[3] += new Vector2(0, 40);
            addPhrase(source, 10000, 500, changed);

            Assert.That(analyse(source).KeyAt(8), Is.Not.EqualTo(analyse(source).KeyAt(0)));
        }

        [Test]
        public void TestSpatiallyLargerJumpsRemainDifferentMotifs()
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions);
            addPhrase(source, 10000, 500, phrase_positions.Select(point => point * 2).ToArray());

            Assert.That(analyse(source).KeyAt(8), Is.Not.EqualTo(analyse(source).KeyAt(0)));
        }

        [Test]
        public void TestDifferentRhythmDoesNotMatch()
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions);
            addPhrase(source, 10000, 500, phrase_positions);
            source.HitObjects[11].StartTime += 125;

            Assert.That(analyse(source).KeyAt(8), Is.Not.EqualTo(analyse(source).KeyAt(0)));
        }

        [Test]
        public void TestMillisecondRoundingStillMatchesMusicalFractions()
        {
            var source = map(60000d / 173);
            addPhrase(source, 0, 60000d / 173, phrase_positions);
            addPhrase(source, 10000, 60000d / 173, phrase_positions);
            for (int i = 8; i < 16; i++)
                source.HitObjects[i].StartTime = Math.Round(source.HitObjects[i].StartTime);

            Assert.That(analyse(source).KeyAt(8), Is.EqualTo(analyse(source).KeyAt(0)));
        }

        [Test]
        public void TestShortFragmentsAcrossTimingChangeAreNotJoined()
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions.Take(6).ToArray());
            source.ControlPointInfo.Add(625, new TimingControlPoint { BeatLength = 500 });

            Assert.Multiple(() =>
            {
                Assert.That(analyse(source).KeyAt(0), Is.Null);
                Assert.That(analyse(source).KeyAt(3), Is.Null);
            });
        }

        [Test]
        public void TestShortFragmentsAcrossRestAreNotJoined()
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions.Take(3).ToArray());
            addPhrase(source, 2000, 500, phrase_positions.Take(3).ToArray());

            Assert.That(analyse(source).KeyAt(0), Is.Null);
        }

        [Test]
        public void TestTimingChangeInsideSustainEndsPhrase()
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions);
            source.HitObjects[2] = new SourceSlider { StartTime = 500, Duration = 1000, Position = phrase_positions[2] };
            source.ControlPointInfo.Add(1400, new TimingControlPoint { BeatLength = 400 });

            Assert.That(analyse(source).KeyAt(0), Is.Null);
        }

        [TestCase(0, 500)]
        [TestCase(1, 250)]
        public void TestDurationAndRepeatDifferencesMatter(int repeats, double duration)
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions);
            addPhrase(source, 10000, 500, phrase_positions);
            source.HitObjects[0] = new SourceSlider { StartTime = 0, Duration = 250, Position = phrase_positions[0] };
            source.HitObjects[8] = new SourceSlider { StartTime = 10000, Duration = duration, RepeatCount = repeats, Position = phrase_positions[0] };

            Assert.That(analyse(source).KeyAt(8), Is.Not.EqualTo(analyse(source).KeyAt(0)));
        }

        [Test]
        public void TestDifferentSliderShapeWithIdenticalHeadsDoesNotMatch()
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions);
            addPhrase(source, 10000, 500, phrase_positions);
            source.HitObjects[0] = pathSlider(0, new Vector2(30, 20));
            source.HitObjects[8] = pathSlider(10000, new Vector2(30, -20));

            Assert.That(analyse(source).KeyAt(8), Is.Not.EqualTo(analyse(source).KeyAt(0)));
        }

        [Test]
        public void TestMatchingSliderDurationsAndPathsSurviveTempoAndRotationChanges()
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions);
            source.ControlPointInfo.Add(10000, new TimingControlPoint { BeatLength = 375 });
            Vector2 translation = new Vector2(90, 170);
            addPhrase(source, 10000, 375, phrase_positions.Select(point => rotate(point, 37) + translation).ToArray());
            PathSlider first = pathSlider(0, new Vector2(30, 20));
            PathSlider second = pathSlider(10000, new Vector2(30, 20));
            first.RepeatCount = second.RepeatCount = 1;
            second.Duration = 187.5;
            second.Position = rotate(first.Position, 37) + translation;
            second.Path = new SliderPath(new[]
            {
                new PathControlPoint(Vector2.Zero, PathType.LINEAR),
                new PathControlPoint(rotate(new Vector2(30, 20), 37)),
                new PathControlPoint(rotate(new Vector2(60, 0), 37)),
            }, null);
            source.HitObjects[0] = first;
            source.HitObjects[8] = second;

            Assert.That(analyse(source).KeyAt(8), Is.EqualTo(analyse(source).KeyAt(0)));
        }

        [Test]
        public void TestLongSustainIsNotMistakenForRest()
        {
            var source = map();
            source.HitObjects.Add(new SourceSlider { StartTime = 0, Duration = 2500, Position = phrase_positions[0] });
            addPhrase(source, 2500, 500, phrase_positions.Skip(1).Take(3).ToArray());

            Assert.That(analyse(source).KeyAt(0), Is.Not.Null);
        }

        [Test]
        public void TestMissingPositionsDoNotAliasPositionedHeads()
        {
            var source = map();
            addPhrase(source, 0, 500, Enumerable.Repeat(Vector2.Zero, 8).ToArray());
            for (int i = 0; i < 8; i++)
                source.HitObjects.Add(new SourceUnpositioned { StartTime = 10000 + i * 250 });

            Assert.That(analyse(source).KeyAt(8), Is.Not.EqualTo(analyse(source).KeyAt(0)));
        }

        [Test]
        public void TestBoundedLookaheadAndReadOnlyBehaviour()
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions);
            var first = analyse(source);
            string key = first.KeyAt(0)!;
            Vector2[] positions = source.HitObjects.Cast<SourceCircle>().Select(note => note.Position).ToArray();
            source.HitObjects.Add(new SourceCircle { StartTime = 2000, Position = new Vector2(-100, -100) });
            var second = analyse(source);

            Assert.Multiple(() =>
            {
                Assert.That(second.KeyAt(0), Is.EqualTo(key));
                Assert.That(source.HitObjects.Take(8).Cast<SourceCircle>().Select(note => note.Position), Is.EqualTo(positions));
                Assert.That(second.KeyAt(-1), Is.Null);
                Assert.That(second.KeyAt(source.HitObjects.Count), Is.Null);
                Assert.That(second.OccurrencesAt(-1), Is.Zero);
                Assert.That(second.OccurrencesAt(8), Is.Zero);
            });
        }

        [TestCase(12, 1)]
        [TestCase(16, 2)]
        [TestCase(24, 3)]
        public void TestOccurrenceCountDoesNotCountOverlappingWindows(int heads, int expectedOccurrences)
        {
            var source = map();
            addPhrase(source, 0, 500, Enumerable.Range(0, heads).Select(i => new Vector2(i * 16, 0)).ToArray());
            var motifs = analyse(source);

            Assert.Multiple(() =>
            {
                Assert.That(motifs.KeyAt(0), Is.EqualTo(motifs.KeyAt(1)));
                Assert.That(motifs.OccurrencesAt(0), Is.EqualTo(expectedOccurrences));
                Assert.That(motifs.OccurrencesAt(1), Is.EqualTo(expectedOccurrences));
            });
        }

        [Test]
        public void TestCancelledAnalysisDoesNotBeginReadingSource()
        {
            var source = map();
            addPhrase(source, 0, 500, phrase_positions);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => new SticksCounterpointMotifs(source.HitObjects.ToArray(), source, cancellation.Token));
        }

        private static SticksCounterpointMotifs analyse(Beatmap<HitObject> source) => new SticksCounterpointMotifs(source.HitObjects.ToArray(), source);

        private static Beatmap<HitObject> map(double beatLength = 500)
        {
            var source = new Beatmap<HitObject>();
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = beatLength });
            return source;
        }

        private static void addPhrase(Beatmap<HitObject> source, double start, double beatLength, Vector2[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
                source.HitObjects.Add(new SourceCircle { StartTime = start + i * beatLength / 2, Position = positions[i] });
        }

        private static Vector2 rotate(Vector2 point, float degrees)
        {
            float angle = degrees * MathF.PI / 180;
            return new Vector2(point.X * MathF.Cos(angle) - point.Y * MathF.Sin(angle), point.X * MathF.Sin(angle) + point.Y * MathF.Cos(angle));
        }

        private static PathSlider pathSlider(double start, Vector2 middle) => new PathSlider
        {
            StartTime = start,
            Duration = 250,
            Position = phrase_positions[0],
            Path = new SliderPath(new[] { new PathControlPoint(Vector2.Zero, PathType.LINEAR), new PathControlPoint(middle), new PathControlPoint(new Vector2(60, 0)) }, null),
        };

        private sealed class SourceUnpositioned : HitObject
        {
        }

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

        private sealed class PathSlider : SourceSlider, IHasPath
        {
            public SliderPath Path { get; set; } = new SliderPath();
            public double Distance => Path.Distance;
        }
    }
}
