using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksBeginnerConversionTest
    {
        [Test]
        public void TestBeginnerConversionPreservesSourceAttacksAndSliderDurations()
        {
            Beatmap<HitObject> source = map(0);
            SticksHitObject[] converted = convert(source);

            foreach (HitObject original in source.HitObjects)
            {
                var offset = ((IHasPosition)original).Position - SticksBeatmapConverter.STANDARD_CENTRE;
                float angle = MathF.Atan2(offset.Y, offset.X) * 180 / MathF.PI;
                SticksHitObject head = converted.First(note => note.StartTime == original.StartTime
                    && Math.Abs(SticksHitObject.DeltaAngle(note.Angle, angle)) < 0.001);
                if (original is IHasDuration duration)
                {
                    Assert.That(head, Is.TypeOf<SticksSlider>());
                    Assert.That(((SticksSlider)head).Duration, Is.EqualTo(duration.Duration));
                }
            }
            Assert.That(coordinationWindows(converted), Is.LessThan(coordinationWindows(convert(source, false))));
        }

        [Test]
        public void TestIntroducedCoordinationRampsUpAcrossBeginnerDifficulties()
        {
            Beatmap<HitObject> source = map(5);
            int[] counts = new[] { 0d, 1, 1.5, 1.6, 1.8, 2, 2.5, 3, 4 }.Select(stars =>
            {
                var allowance = new SticksConversionCoordinationAllowance(source, source.HitObjects, stars);
                int count = 0;
                for (int i = 0; i < 48; i++)
                {
                    double start = i * 4000;
                    if (!allowance.CanIntroduce(start))
                        continue;
                    count++;
                }
                return count;
            }).ToArray();
            Assert.That(counts.Take(4), Has.All.Zero, "No introduced coordination through 1.6 source stars.");
            Assert.That(counts[4], Is.EqualTo(48 * 0.15).Within(0.5));
            Assert.That(counts[5], Is.EqualTo(48 * 0.30).Within(0.5));
            Assert.That(counts[6], Is.EqualTo(48 * 0.65).Within(0.5));
            Assert.That(counts[7], Is.EqualTo(48));
            Assert.That(counts[8], Is.EqualTo(counts[7]));
            for (int i = 4; i <= 7; i++)
                Assert.That(counts[i], Is.GreaterThan(counts[i - 1]));
        }

        [Test]
        public void TestSourceSimultaneousHeadsSurviveAndConverterReuseResetsAllowance()
        {
            Beatmap<HitObject> source = map(2);
            var converter = new SticksBeatmapConverter(source, new SticksRuleset());
            string[] first = converter.Convert().HitObjects.Cast<SticksHitObject>().Select(note => $"{note.StartTime}:{note.GetType().Name}:{note.Side}:{note.Angle}").ToArray();
            Assert.That(converter.Convert().HitObjects.Cast<SticksHitObject>().Select(note => $"{note.StartTime}:{note.GetType().Name}:{note.Side}:{note.Angle}"), Is.EqualTo(first));

            source.Difficulty.OverallDifficulty = 1;
            source.HitObjects.Add(new SourceCircle { StartTime = source.HitObjects[^1].StartTime, Position = new Vector2(96, 192) });
            SticksHitObject[] pair = converter.Convert().HitObjects.Cast<SticksHitObject>().Where(note => note.StartTime == source.HitObjects[^1].StartTime).ToArray();
            Assert.That(pair, Has.Length.EqualTo(2));
            Assert.That(pair.Select(note => note.Side).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void TestAllowanceIsSharedAndSilenceDoesNotEarnExtraPatterns()
        {
            Beatmap<HitObject> source = map(3);
            var allowance = new SticksConversionCoordinationAllowance(source, source.HitObjects, 2);
            double first = source.HitObjects.Select(note => note.StartTime).First(time => allowance.CanIntroduce(time));

            Assert.That(allowance.CanIntroduce(first + 500), Is.True, "Every planner sees the same permitted phrase.");

            var isolated = new Beatmap<HitObject>();
            isolated.Difficulty.OverallDifficulty = 2;
            isolated.HitObjects.Add(new SourceCircle { StartTime = 1000000 });
            Assert.That(new SticksConversionCoordinationAllowance(isolated, isolated.HitObjects, 1).CanIntroduce(1000000), Is.False);

            source.Difficulty.OverallDifficulty = 4;
            allowance = new SticksConversionCoordinationAllowance(source, source.HitObjects, 3);

            Assert.That(allowance.CanIntroduce(first), Is.True, "Higher difficulty planning is unrestricted.");
        }

        private static int coordinationWindows(SticksHitObject[] notes) => notes.Where(first => notes.Any(second =>
                first.Side != second.Side && (Math.Abs(first.StartTime - second.StartTime) < 0.01
                    || first.StartTime < endTime(second) - 0.01 && second.StartTime < endTime(first) - 0.01)))
            .Select(note => (int)(note.StartTime / 4000)).Distinct().Count();

        [Test]
        public void TestSourceStarsIgnoreCachedMetadataAndRespectCancellation()
        {
            Beatmap<HitObject> source = map(5);
            source.BeatmapInfo.StarRating = 100;
            double stars = SticksConversionCoordinationAllowance.CalculateSourceStars(source, CancellationToken.None);
            source.BeatmapInfo.StarRating = -1;
            Assert.That(SticksConversionCoordinationAllowance.CalculateSourceStars(source, CancellationToken.None), Is.EqualTo(stars));
            Assert.Throws<OperationCanceledException>(() => SticksConversionCoordinationAllowance.CalculateSourceStars(source, new CancellationToken(true)));
        }

        [Test]
        public void TestHigherSourceStarsKeepFullConversionEvenAtLowOD()
        {
            Beatmap<HitObject> source = map(0);
            foreach (HitObject note in source.HitObjects)
            {
                note.StartTime *= 0.1;
                if (note is SourceSlider slider)
                    slider.Duration *= 0.1;
            }
            Assert.That(SticksConversionCoordinationAllowance.CalculateSourceStars(source, CancellationToken.None), Is.GreaterThanOrEqualTo(3));
            string[] signature(SticksHitObject[] notes) => notes.Select(note => $"{note.GetType().Name}:{note.StartTime}:{note.Side}:{note.Angle}:"
                + (note is SticksSlider slider ? $"{slider.Duration}:{string.Join(",", slider.SegmentArcAngles)}" : "")).ToArray();
            Assert.That(signature(convert(source)), Is.EqualTo(signature(convert(source, false))));
        }

        private static double endTime(SticksHitObject note) => note.StartTime + (note is IHasDuration duration ? duration.Duration : 0);

        private static SticksHitObject[] convert(Beatmap<HitObject> source, bool beginner = true) => new SticksBeatmapConverter(source, new SticksRuleset()) { LimitBeginnerCoordination = beginner }.Convert().HitObjects.Cast<SticksHitObject>().ToArray();

        private static Beatmap<HitObject> map(float od)
        {
            var source = new Beatmap<HitObject>();
            source.Difficulty.OverallDifficulty = od;
            source.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            for (int i = 0; i < 48; i++)
            {
                source.HitObjects.Add(new SourceSlider { StartTime = i * 4000, Duration = 1500, Position = new Vector2(416, 192) });
                source.HitObjects.Add(new SourceCircle { StartTime = i * 4000 + 2000, Position = new Vector2(256, 32) });
                source.HitObjects.Add(new SourceCircle { StartTime = i * 4000 + 3000, Position = new Vector2(96, 192) });
            }
            return source;
        }

        private class SourceCircle : HitObject, IHasPosition
        {
            public Vector2 Position { get; set; }
            public float X { get => Position.X; set => Position = new Vector2(value, Y); }
            public float Y { get => Position.Y; set => Position = new Vector2(X, value); }
        }

        private class SourceSlider : SourceCircle, IHasDuration
        {
            public double Duration { get; set; }
            public double EndTime => StartTime + Duration;
        }
    }
}
