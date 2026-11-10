using System;
using NUnit.Framework;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [TestFixture]
    public class SticksStickResponseTest
    {
        private static readonly float[] activation_settings = { 0.85f, 0.9f, 0.95f, 1f };

        private static readonly float[] directions = { 0, 10, 30, 45, 80, 135, 180, 225, 270, 315, 350 };

        // A stick held against its gate reports slightly past its nominal range.
        private const float full_push = 1.01f;

        [Test]
        public void TestRestingStickIsCentred()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SticksStickResponse.FromRaw(Vector2.Zero), Is.EqualTo(Vector2.Zero));
                Assert.That(SticksStickResponse.FromRaw(new Vector2(0.06f, -0.07f)), Is.EqualTo(Vector2.Zero));
                Assert.That(SticksStickResponse.FromRaw(at(45, SticksStickResponse.DEAD_CENTRE)), Is.EqualTo(Vector2.Zero));
                Assert.That(SticksStickResponse.FromRaw(at(45, SticksStickResponse.DEAD_CENTRE + 0.01f)).Length, Is.GreaterThan(0));
            });
        }

        [Test]
        public void TestEdgeIsTheRealPosition()
        {
            Assert.Multiple(() =>
            {
                foreach (float distance in new[] { 0.85f, 0.9f, 0.95f, 1f })
                {
                    foreach (float direction in directions)
                    {
                        Vector2 real = at(direction, distance);
                        Assert.That((SticksStickResponse.FromRaw(real) - real).Length, Is.LessThan(0.00001f), $"{distance} at {direction}");
                    }
                }

                // Sticks which report past their nominal range were never altered either.
                Assert.That(SticksStickResponse.FromRaw(new Vector2(1, 1)), Is.EqualTo(new Vector2(1, 1)));
            });
        }

        [Test]
        public void TestEveryDirectionBehavesTheSame()
        {
            Assert.Multiple(() =>
            {
                foreach (float distance in new[] { 0.2f, 0.5f, 0.7f, 0.8f, 0.9f })
                {
                    float expected = SticksStickResponse.FromRaw(distance);

                    foreach (float direction in directions)
                    {
                        Vector2 units = SticksStickResponse.FromRaw(at(direction, distance));
                        float angle = SticksHitObject.NormaliseAngle(MathF.Atan2(units.Y, units.X) * 180 / MathF.PI);
                        Assert.That(units.Length, Is.EqualTo(expected).Within(0.0001), $"{distance} at {direction}");
                        Assert.That(SticksHitObject.DeltaAngle(angle, direction), Is.Zero.Within(0.001), $"{distance} at {direction}");
                    }
                }
            });
        }

        [Test]
        public void TestFurtherOutNeverReadsCloserIn()
        {
            float previous = 0;

            for (int step = 0; step <= 1000; step++)
            {
                float units = SticksStickResponse.FromRaw(step / 1000f);
                Assert.That(units, Is.GreaterThanOrEqualTo(previous), $"at {step / 1000f}");
                Assert.That(units - previous, Is.LessThan(0.002f), $"jump at {step / 1000f}");
                previous = units;
            }

            Assert.That(previous, Is.EqualTo(1));
        }

        [Test]
        public void TestRealDistanceRoundTrips()
        {
            Assert.Multiple(() =>
            {
                for (int step = 11; step <= 100; step++)
                {
                    float distance = step / 100f;
                    Assert.That(SticksStickResponse.ToRaw(SticksStickResponse.FromRaw(distance)), Is.EqualTo(distance).Within(0.0001), $"at {distance}");
                }
            });
        }

        [Test]
        public void TestRechargePointIsAFixedRealDistanceBelowTheFlickPoint()
        {
            Assert.Multiple(() =>
            {
                foreach (float activation in activation_settings)
                {
                    float recharge = activation - SticksStickResponse.PHYSICAL_RECHARGE_OFFSET;
                    Assert.That(SticksStickResponse.FromRaw(recharge), Is.EqualTo(SticksInputTracker.RechargeThresholdFor(activation)).Within(0.0001), $"setting {activation}");
                    Assert.That(SticksStickResponse.ToRaw(SticksInputTracker.RechargeThresholdFor(activation)), Is.EqualTo(recharge).Within(0.0001), $"setting {activation}");
                }

                // The default: flick at a real 95%, recharge at a real 71%.
                Assert.That(SticksStickResponse.ToRaw(SticksInputTracker.RechargeThresholdFor(SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD)), Is.EqualTo(0.71f).Within(0.0001));
            });
        }

        [Test]
        public void TestControllerFlicksAndRechargesAtTheSameRealDistanceInEveryDirection()
        {
            Assert.Multiple(() =>
            {
                // 100% itself is left out. Whether a full push reads as exactly 100% there depends
                // on rounding for the direction, which is how that setting has always behaved.
                foreach (float activation in new[] { 0.85f, 0.9f, 0.95f, 0.99f })
                {
                    float recharge = activation - SticksStickResponse.PHYSICAL_RECHARGE_OFFSET;

                    foreach (float direction in directions)
                    {
                        string situation = $"setting {activation} at {direction}";

                        Assert.That(flicksFor(activation, direction, 0, activation - 0.004f), Is.Zero, $"short of the flick point, {situation}");
                        Assert.That(flicksFor(activation, direction, 0, activation + 0.0005f), Is.EqualTo(1), $"first flick, {situation}");
                        Assert.That(flicksFor(activation, direction, 0, full_push, recharge + 0.004f, full_push), Is.EqualTo(1), $"return short of the recharge point, {situation}");
                        Assert.That(flicksFor(activation, direction, 0, full_push, recharge - 0.004f, full_push), Is.EqualTo(2), $"return past the recharge point, {situation}");
                    }
                }
            });
        }

        /// <summary>
        /// Moves a controller stick through real distances from the centre and counts the flicks.
        /// </summary>
        private static long flicksFor(float activation, float direction, params float[] distances)
        {
            var tracker = new SticksInputTracker { ActivationThreshold = activation };
            double time = 0;

            foreach (float distance in distances)
            {
                Vector2 units = SticksStickResponse.FromRaw(at(direction, distance));
                tracker.Update(StickSide.Left, units, SticksPlayfield.MapStickDistance(units, 1), time += 10);
            }

            return tracker.SequenceFor(StickSide.Left);
        }

        private static Vector2 at(float degrees, float distance)
        {
            float radians = degrees * MathF.PI / 180;
            return new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * distance;
        }
    }
}
