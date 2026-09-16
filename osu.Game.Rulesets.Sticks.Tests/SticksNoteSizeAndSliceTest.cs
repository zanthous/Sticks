using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksNoteSizeAndSliceTest
    {
        [TestCase(SticksSliceDirection.Neutral)]
        [TestCase(SticksSliceDirection.Clockwise)]
        [TestCase(SticksSliceDirection.Counterclockwise)]
        public void SliceMarkerGrowsDuringApproachAndShrinksOnRewind(SticksSliceDirection direction)
        {
            using var marker = new SticksSliceMarker();
            foreach (float progress in new[] { 0f, 0.25f, 0.5f, 1f, 0.25f, 0f })
            {
                float radius = SticksPlayfield.GUIDE_RADIUS * progress;
                marker.SetState(radius, 90, direction, Color4.CornflowerBlue);
                Assert.That(marker.Scale, Is.EqualTo(new Vector2(progress)));
                Assert.That(marker.Size.X * marker.Scale.X, Is.EqualTo(SticksSlice.RADIUS * 2 * progress));
                Assert.That(marker.Position, Is.EqualTo(SticksPlayfield.PointAt(90, radius)));
            }
        }

        [Test]
        public void SizeSurvivesDefaultsNestedJudgementsModsAndCarrierRoundTrip()
        {
            var slider = new SticksSlider { StartTime = 1000, Duration = 1500, SizeMultiplier = 1.5f };
            slider.SetTimedSegments(new[] { 180f, -90f }, new[] { 500d, 1000d });
            var difficulty = new BeatmapDifficulty { CircleSize = 4 };
            var points = new ControlPointInfo();
            points.Add(0, new TimingControlPoint { BeatLength = 500 });
            slider.ApplyDefaults(points, difficulty);
            slider.ApplyDefaults(points, difficulty);
            Assert.That(slider.PrimaryHitAngle, Is.EqualTo(41.25));
            Assert.That(slider.NestedHitObjects.OfType<SticksHitObject>().All(n => n.SizeMultiplier == 1.5f && n.PrimaryHitAngle == slider.PrimaryHitAngle));
            var mod = new SticksModDifficultyAdjust { PrimaryHitAngle = { Value = 20 } };
            mod.ApplyToHitObject(slider);
            Assert.That(slider.PrimaryHitAngle, Is.EqualTo(30));
            Assert.That(slider.NestedHitObjects.OfType<SticksHitObject>().All(n => n.PrimaryHitAngle == 30));
            string marker = SticksAuthoredBeatmapCodec.EncodeMarker(slider);
            Assert.That(marker, Does.StartWith("sticks-v4~"));
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(slider), out var decoded), marker);
            var restored = (SticksSlider)decoded!;
            restored.ApplyDefaults(points, difficulty);
            Assert.That(restored.SizeMultiplier, Is.EqualTo(1.5));
            Assert.That(restored.PrimaryHitAngle, Is.EqualTo(41.25));
            Assert.That(restored.SegmentArcAngles, Is.EqualTo(slider.SegmentArcAngles));
            Assert.That(restored.SegmentDurationWeights, Is.EqualTo(slider.SegmentDurationWeights));
            restored.SizeMultiplier = 1;
            Assert.That(restored.PrimaryHitAngle, Is.EqualTo(27.5));
            Assert.That(SticksAuthoredBeatmapCodec.EncodeMarker(restored), Does.StartWith("sticks-v3~"));
        }

        [Test]
        public void InspectorRejectsInvalidSizeWithoutChangingTheSelection()
        {
            var note = new SticksFlick { StartTime = 1000 };
            note.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty { CircleSize = 4 });
            foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity, 1000 })
                Assert.That(SticksInspectorEdits.TryPrepare(new[] { note }, new SticksInspectorEdit { SizeMultiplier = invalid }, null, out _, out _), Is.False);
            Assert.That(note.SizeMultiplier, Is.EqualTo(1));
            Assert.That(SticksInspectorEdits.TryPrepare(new[] { note }, new SticksInspectorEdit { SizeMultiplier = 1.75 }, null, out var plans, out _));
            plans.Single().Apply();
            Assert.That(note.PrimaryHitAngle, Is.EqualTo(48.125));
        }

        [TestCase(SticksSliceDirection.Neutral)]
        [TestCase(SticksSliceDirection.Clockwise)]
        [TestCase(SticksSliceDirection.Counterclockwise)]
        public void SliceDirectionRoundTripsAndItsSizeIgnoresCsAndDa(SticksSliceDirection direction)
        {
            var slice = new SticksSlice { StartTime = 1000, Angle = 359, Side = StickSide.Right, Direction = direction };
            Assert.That(SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(slice), out var decoded));
            var restored = (SticksSlice)decoded!;
            Assert.That(restored.Direction, Is.EqualTo(direction));
            Assert.That(restored.Angle, Is.EqualTo(359));
            foreach (float cs in new[] { 0f, 4f, 10f })
            {
                restored.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty { CircleSize = cs });
                new SticksModDifficultyAdjust { PrimaryHitAngle = { Value = 90 } }.ApplyToHitObject(restored);
                Assert.That(restored.PrimaryHitAngle, Is.EqualTo(SticksSlice.HitAngle));
            }
        }

        [Test]
        public void AutoplayCrossesEverySliceDirectionAtItsTimestamp()
        {
            var map = new Beatmap<SticksHitObject>();
            map.HitObjects.AddRange(new[]
            {
                new SticksSlice { StartTime = 1000, Angle = 359, Direction = SticksSliceDirection.Clockwise },
                new SticksSlice { StartTime = 1200, Angle = 30, Direction = SticksSliceDirection.Counterclockwise, Side = StickSide.Right },
                new SticksSlice { StartTime = 1400, Angle = 70 },
            });
            var frames = new SticksAutoGenerator(map).Generate().Frames.Cast<SticksReplayFrame>().ToArray();
            foreach (var slice in map.HitObjects.Cast<SticksSlice>())
            {
                var contacts = frames.Zip(frames.Skip(1)).Select(pair =>
                {
                    Vector2 from = slice.Side == StickSide.Left ? pair.First.LeftStick : pair.First.RightStick;
                    Vector2 to = slice.Side == StickSide.Left ? pair.Second.LeftStick : pair.Second.RightStick;
                    return SticksSliceInput.TryCross(slice, from, to, out double progress)
                        ? pair.First.Time + (pair.Second.Time - pair.First.Time) * progress : double.NaN;
                });
                Assert.That(contacts.Any(time => Math.Abs(time - slice.StartTime) < 0.01), Is.True, slice.Direction.ToString());
            }
        }

        [Test]
        public void SizeChangesAimDemandLocallyWithoutReducingFlickDensity()
        {
            SticksFlick[] notes(float size) => Enumerable.Range(0, 80).Select(i =>
            {
                var note = new SticksFlick { StartTime = 1000 + i * 160, Angle = i * 100 % 360, SizeMultiplier = size };
                note.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty { CircleSize = 4 });
                return note;
            }).ToArray();
            var normal = SticksDifficultyModel.CalculateOrdered(notes(1), 1, 5);
            var large = SticksDifficultyModel.CalculateOrdered(notes(2), 1, 5);
            var small = SticksDifficultyModel.CalculateOrdered(notes(0.75f), 1, 5);
            Assert.That(large.Reading, Is.LessThan(normal.Reading));
            Assert.That(small.Reading, Is.GreaterThan(normal.Reading));
            var repeatedNormal = notes(1);
            var repeatedLarge = notes(2);
            foreach (var note in repeatedNormal.Concat(repeatedLarge))
                note.Angle = 0;
            Assert.That(SticksDifficultyModel.CalculateOrdered(repeatedLarge, 1, 5).Mechanical,
                Is.EqualTo(SticksDifficultyModel.CalculateOrdered(repeatedNormal, 1, 5).Mechanical));
            Assert.That(large.StarRating, Is.LessThan(normal.StarRating));
            Assert.That(small.StarRating, Is.GreaterThan(normal.StarRating));
        }

        [Test]
        public void FastSliderWideningOffsetsMostOfTheAdditionalControlDemand()
        {
            double control(float speed, float size)
            {
                var notes = Enumerable.Range(0, 24).Select(i =>
                {
                    var slider = new SticksSlider
                    {
                        StartTime = 1000 + i * 1000,
                        Duration = 750,
                        Side = i % 2 == 0 ? StickSide.Left : StickSide.Right,
                        ArcAngle = speed * 0.75f,
                        SizeMultiplier = size,
                    };
                    slider.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty { CircleSize = 4 });
                    return slider;
                });
                return SticksDifficultyCalculator.CalculateDifficulty(notes).Control;
            }

            double normal = control(360, 1);
            double intermediate = control(540, 1.5f);
            double fastWide = control(720, 2);
            double fastNarrow = control(720, 1);
            Assert.That(intermediate, Is.GreaterThan(normal));
            Assert.That(fastWide, Is.GreaterThan(intermediate));
            Assert.That(fastWide - normal, Is.LessThan((fastNarrow - normal) / 2),
                "The conversion's wider tracking window must offset most of the extra control demand from 360 to 720 degrees/s.");
        }

        [Test]
        public void SweepsCrossSmallTargetsWithoutRechargeButStationaryAndWrongDirectionDoNot()
        {
            var slice = new SticksSlice { Angle = 0 };
            Assert.That(SticksSliceInput.TryCross(slice, at(-10), at(10), out double t));
            Assert.That(t, Is.EqualTo(0.5).Within(0.0001));
            Assert.That(SticksSliceInput.TryCross(slice, at(0), at(0), out _), Is.False);
            Assert.That(SticksSliceInput.TryCross(slice, at(-10) * 0.7f, at(10) * 0.7f, out _), Is.False);
            slice.Direction = SticksSliceDirection.Clockwise;
            Assert.That(SticksSliceInput.TryCross(slice, at(-10), at(10), out _));
            Assert.That(SticksSliceInput.TryCross(slice, at(10), at(-10), out _), Is.False);
            Assert.That(SticksSliceInput.TryCross(slice, Vector2.Zero, at(0), out _), Is.False);
            slice.Direction = SticksSliceDirection.Counterclockwise;
            Assert.That(SticksSliceInput.TryCross(slice, at(10), at(-10), out _));
            Assert.That(SticksSliceInput.TryCross(slice, at(-10), at(10), out _), Is.False);
        }

        private static Vector2 at(float angle) => new Vector2(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180));
    }
}
