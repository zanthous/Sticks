using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksStationarySliderTest
    {
        [TestCase("single")]
        [TestCase("legacy spans")]
        [TestCase("custom")]
        [TestCase("timed")]
        public void TestStationaryPathsHaveConstantAnglesAndNoReversalCues(string path)
        {
            SticksSlider slider = create(path);
            var samples = new float[17];
            slider.FillAngleSamples(500, 3500, samples);

            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(slider.IsStationary, Is.True);
                Assert.That(slider.InitialDirection, Is.Zero);
                Assert.That(samples, Is.All.EqualTo(37));
                Assert.That(Enumerable.Range(0, 13).Select(i => slider.AngleAt(500 + i * 250)), Is.All.EqualTo(37));
                Assert.That(Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentEndsWithReversal), Is.All.EqualTo(false));
            });

            string json = JsonConvert.SerializeObject(slider);
            SticksSlider restored = JsonConvert.DeserializeObject<SticksSlider>(json)!;
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(restored.IsStationary, Is.True);
                Assert.That(restored.SegmentArcAngles, Is.EqualTo(slider.SegmentArcAngles));
                Assert.That(restored.SegmentDurationWeights, Is.EqualTo(slider.SegmentDurationWeights));
                Assert.That(restored.AngleAt(2600), Is.EqualTo(37));
                Assert.That(restored.Duration, Is.EqualTo(2000));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestTailCanMoveToZeroAndBackWithoutChangingDuration(bool timed)
        {
            SticksSlider slider = create("single");
            if (timed)
                slider.SetTimedSegments(new[] { 0f, 90f }, new[] { 400d, 1600d });
            else
                slider.SetCustomSegments(new[] { 90f });

            slider.ReplaceFinalSegment(0);
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(slider.IsStationary, Is.True);
                Assert.That(slider.AngleAt(slider.EndTime), Is.EqualTo(37));
                Assert.That(slider.Duration, Is.EqualTo(2000));
                Assert.That(slider.InitialDirection, Is.Zero);
            });

            slider.ReplaceFinalSegment(-0.25f);
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(slider.IsStationary, Is.False);
                Assert.That(slider.AngleAt(slider.EndTime), Is.EqualTo(36.75));
                Assert.That(slider.Duration, Is.EqualTo(2000));
                Assert.That(slider.InitialDirection, Is.EqualTo(-1));
                Assert.That(slider.HasTimedSegments, Is.EqualTo(timed));
            });
        }

        [TestCase("single")]
        [TestCase("custom")]
        [TestCase("timed")]
        public void TestStationaryContinuationExtendsDurationWithoutInventingMovement(string path)
        {
            SticksSlider slider = create(path);
            int count = slider.SegmentCount;

            Assert.That(slider.ContinuationArcAt(3750), Is.Zero);
            Assert.That(slider.AppendTimedSegmentAtConstantSpeed(3750), Is.True);
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(slider.EndTime, Is.EqualTo(3750));
                Assert.That(slider.AngleAt(3700), Is.EqualTo(37));
                Assert.That(slider.SegmentCount, Is.EqualTo(count));
                Assert.That(slider.IsStationary, Is.True);
                NUnitCompatibility.That(() => slider.AppendSegmentAtConstantSpeed(45), Throws.InvalidOperationException,
                    "An angular continuation cannot infer a speed from stationary movement.");
            });

            foreach (double invalidEnd in new[] { 1000d, 3750d, double.NaN, double.PositiveInfinity })
                Assert.That(slider.AppendTimedSegmentAtConstantSpeed(invalidEnd), Is.False);

            Assert.That(slider.EndTime, Is.EqualTo(3750));
        }

        [TestCase("custom", 2000d / 3)]
        [TestCase("timed", 800d)]
        public void TestRemovingStationaryPieceRemovesItsActualDuration(string path, double removedDuration)
        {
            SticksSlider slider = create(path);
            Assert.That(slider.RemoveFinalSegmentAtConstantSpeed(), Is.True);
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(slider.Duration, Is.EqualTo(2000 - removedDuration).Within(0.00001));
                Assert.That(slider.IsStationary, Is.True);
                Assert.That(slider.SegmentCount, Is.EqualTo(2));
            });
        }

        [Test]
        public void TestSubDegreePathKeepsSpeedWhenExtendingAndRemoving()
        {
            SticksSlider slider = create("single");
            slider.SetCustomSegments(new[] { 0.25f });
            Assert.That(slider.AppendTimedSegmentAtConstantSpeed(3500), Is.True);
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(slider.SegmentArcAngles, Is.EqualTo(new[] { 0.25f, -0.0625f }));
                Assert.That(slider.SegmentEndTimeAt(0), Is.EqualTo(3000));
            });

            Assert.That(slider.RemoveFinalSegmentAtConstantSpeed(), Is.True);
            Assert.That(slider.EndTime, Is.EqualTo(3000));
            slider.AppendSegmentAtConstantSpeed(-0.25f);
            Assert.That(slider.EndTime, Is.EqualTo(5000));
            Assert.That(slider.SegmentEndTimeAt(0), Is.EqualTo(3000));
        }

        [Test]
        public void TestStationaryHeadUsesDirectionlessSkinCentre()
        {
            using var marker = new SticksSliderHeadMarker(StickSide.Left, 0, Color4.White);
            var centre = (osu.Game.Rulesets.Sticks.Skinning.SticksSkinnedSprite)typeof(SticksSliderHeadMarker)
                .GetField("skinCentre", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(marker)!;
            Assert.That(centre.TextureName, Is.EqualTo("sticks-note-centre"));

            marker.SetLaneAndDirection(StickSide.Left, -1, Color4.White);
            Assert.That(centre.TextureName, Is.EqualTo("sticks-slider-head"));

            marker.SetLaneAndDirection(StickSide.Left, 0, Color4.White);
            Assert.That(centre.TextureName, Is.EqualTo("sticks-note-centre"));
        }

        [Test]
        public void TestStationaryHeadStaysVisibleUntilRelease()
        {
            SticksSlider slider = create("single");
            using var drawable = new DrawableSticksSlider(slider);
            MethodInfo updateCue = typeof(DrawableSticksSlider).GetMethod("updateHeadCue", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var marker = (SticksSliderHeadMarker)typeof(DrawableSticksSlider)
                .GetField("headMarker", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(drawable)!;

            updateCue.Invoke(drawable, new object[] { 2500d, false });
            Assert.That(marker.Alpha, Is.EqualTo(1));
            updateCue.Invoke(drawable, new object[] { 3001d, false });
            Assert.That(marker.Alpha, Is.Zero);
        }

        [Test]
        public void TestStationaryHeadAndTailKeepAuthoredSamples()
        {
            SticksSlider slider = create("single");
            slider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_CLAP, volume: 60) });
            slider.NodeSamples.Add(new[] { new HitSampleInfo(HitSampleInfo.HIT_FINISH, volume: 80) });
            slider.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());

            using var drawable = new DrawableSticksSlider(slider);
            NUnitCompatibility.Multiple(() =>
            {
                Assert.That(drawable.GetSamples().Single().Name, Is.EqualTo(HitSampleInfo.HIT_CLAP));
                Assert.That(slider.NestedHitObjects.OfType<SticksSliderTail>().Single().Samples.Single().Name, Is.EqualTo(HitSampleInfo.HIT_FINISH));
            });
        }

        private static SticksSlider create(string path)
        {
            var slider = new SticksSlider { StartTime = 1000, Duration = 2000, Angle = 37, Side = StickSide.Left };
            switch (path)
            {
                case "legacy spans":
                    slider.RepeatCount = 2;
                    break;

                case "custom":
                    slider.SetCustomSegments(new[] { 0f, 0f, 0f });
                    break;

                case "timed":
                    slider.SetTimedSegments(new[] { 0f, 0f, 0f }, new[] { 500d, 700d, 800d });
                    break;
            }

            return slider;
        }
    }
}
