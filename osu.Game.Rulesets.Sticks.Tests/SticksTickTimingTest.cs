using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksTickTimingTest
    {
        [Test]
        public void TestHoldTicksAdoptBpmChanges()
        {
            ControlPointInfo controlPoints = timingChanges();
            var hold = new SticksHold
            {
                StartTime = 1000,
                Duration = 2000,
                Side = StickSide.Left,
                Angle = 0,
            };

            hold.ApplyDefaults(controlPoints, difficulty());

            Assert.That(hold.NestedHitObjects.OfType<SticksHoldTick>().Select(tick => tick.StartTime),
                Is.EqualTo(new[] { 1500d, 2000d, 2250d, 2500d, 2750d }));
        }

        [Test]
        public void TestSliderTicksAdoptBpmChanges()
        {
            ControlPointInfo controlPoints = timingChanges();
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 2000,
                Side = StickSide.Left,
                Angle = 0,
                ArcAngle = 180,
            };

            slider.ApplyDefaults(controlPoints, difficulty());

            Assert.That(slider.NestedHitObjects.OfType<SticksSliderTick>().Select(tick => tick.StartTime),
                Is.EqualTo(new[] { 1500d, 2000d, 2250d, 2500d, 2750d }));
        }

        [Test]
        public void TestReversalStartsFreshTickPhase()
        {
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 500 });
            var slider = new SticksSlider
            {
                StartTime = 1000,
                Duration = 1800,
                Side = StickSide.Right,
                Angle = 45,
            };
            slider.SetCustomSegments(new[] { 90f, -90f });

            slider.ApplyDefaults(controlPoints, difficulty());

            Assert.Multiple(() =>
            {
                Assert.That(slider.NestedHitObjects.OfType<SticksSliderRepeat>().Single().StartTime, Is.EqualTo(1900));
                Assert.That(slider.NestedHitObjects.OfType<SticksSliderTick>().Select(tick => tick.StartTime),
                    Is.EqualTo(new[] { 1500d, 2400d }));
            });
        }

        private static ControlPointInfo timingChanges()
        {
            var controlPoints = new ControlPointInfo();
            controlPoints.Add(0, new TimingControlPoint { BeatLength = 500 });
            controlPoints.Add(2000, new TimingControlPoint { BeatLength = 250 });
            return controlPoints;
        }

        private static BeatmapDifficulty difficulty() => new BeatmapDifficulty
        {
            SliderTickRate = 1,
            OverallDifficulty = 5,
        };
    }
}
