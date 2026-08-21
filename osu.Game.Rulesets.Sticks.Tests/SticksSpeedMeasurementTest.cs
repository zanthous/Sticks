using NUnit.Framework;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksSpeedMeasurementTest
    {
        [Test]
        public void TestMeasuresPressAndReturnWithoutEdgeHoldTime()
        {
            var measurement = new SticksSpeedMeasurement(0.95f);

            measurement.Update(0, 0);
            measurement.Update(0.10f, 10);
            measurement.Update(1, 100);
            measurement.Update(1, 300);
            measurement.Update(0.90f, 310);
            measurement.Update(0, 400);

            Assert.Multiple(() =>
            {
                Assert.That(measurement.LatestPressTime, Is.EqualTo(95).Within(0.001));
                Assert.That(measurement.AveragePressTime, Is.EqualTo(95).Within(0.001));
                Assert.That(measurement.LatestReturnTime, Is.EqualTo(90).Within(0.001));
                Assert.That(measurement.AverageReturnTime, Is.EqualTo(90).Within(0.001));
                Assert.That(measurement.PressCount, Is.EqualTo(1));
                Assert.That(measurement.ReturnCount, Is.EqualTo(1));
                Assert.That(measurement.TryGetPressPercentiles(out double press5, out double pressMedian, out double press95), Is.True);
                Assert.That(press5, Is.EqualTo(95).Within(0.001));
                Assert.That(pressMedian, Is.EqualTo(95).Within(0.001));
                Assert.That(press95, Is.EqualTo(95).Within(0.001));
                Assert.That(measurement.TryGetReturnPercentiles(out double return5, out double returnMedian, out double return95), Is.True);
                Assert.That(return5, Is.EqualTo(90).Within(0.001));
                Assert.That(returnMedian, Is.EqualTo(90).Within(0.001));
                Assert.That(return95, Is.EqualTo(90).Within(0.001));
            });
        }

        [Test]
        public void TestIncompleteMotionIsNotRecorded()
        {
            var measurement = new SticksSpeedMeasurement(0.95f);

            measurement.Update(0, 0);
            measurement.Update(0.5f, 50);
            measurement.Update(0, 100);

            Assert.Multiple(() =>
            {
                Assert.That(measurement.PressCount, Is.Zero);
                Assert.That(measurement.ReturnCount, Is.Zero);
            });
        }

        [Test]
        public void TestAveragesRepeatedTrials()
        {
            var measurement = new SticksSpeedMeasurement(0.95f);

            performTrial(measurement, 0, 100, 200);
            performTrial(measurement, 300, 500, 700);

            Assert.Multiple(() =>
            {
                Assert.That(measurement.AveragePressTime, Is.EqualTo(142.5).Within(0.001));
                Assert.That(measurement.AverageReturnTime, Is.EqualTo(135).Within(0.001));
                Assert.That(measurement.PressCount, Is.EqualTo(2));
                Assert.That(measurement.ReturnCount, Is.EqualTo(2));
                Assert.That(measurement.TryGetPressPercentiles(out double press5, out double pressMedian, out double press95), Is.True);
                Assert.That(press5, Is.EqualTo(99.75).Within(0.001));
                Assert.That(pressMedian, Is.EqualTo(142.5).Within(0.001));
                Assert.That(press95, Is.EqualTo(185.25).Within(0.001));
                Assert.That(measurement.TryGetReturnPercentiles(out double return5, out double returnMedian, out double return95), Is.True);
                Assert.That(return5, Is.EqualTo(94.5).Within(0.001));
                Assert.That(returnMedian, Is.EqualTo(135).Within(0.001));
                Assert.That(return95, Is.EqualTo(175.5).Within(0.001));
            });
        }

        [Test]
        public void TestSnapbackSegmentCrossingThroughZeroCompletesReturn()
        {
            var measurement = new SticksSpeedMeasurement(0.95f);

            measurement.Update(Vector2.Zero, 0);
            measurement.Update(new Vector2(1, 0), 100);

            // Neither endpoint is within the 5% neutral circle, but the physical movement from
            // positive to negative X passes directly through it.
            measurement.Update(new Vector2(-0.2f, 0), 200);

            Assert.Multiple(() =>
            {
                Assert.That(measurement.ReturnCount, Is.EqualTo(1));
                Assert.That(measurement.LatestReturnTime, Is.EqualTo(75).Within(0.001));
                Assert.That(measurement.PressCount, Is.EqualTo(1), "Snapback must not become a second completed press.");
            });
        }

        private static void performTrial(SticksSpeedMeasurement measurement, double neutralTime, double edgeTime, double returnedTime)
        {
            measurement.Update(0, neutralTime);
            measurement.Update(1, edgeTime);
            measurement.Update(0, returnedTime);
        }
    }
}
