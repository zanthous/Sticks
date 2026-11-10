using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Tests
{
    /// <summary>
    /// A replay stores stick positions, and watching it judges those positions again. The same
    /// recorded positions must therefore give the same flicks in every version; otherwise older
    /// replays, and replays shared between players on different versions, stop matching their scores.
    /// </summary>
    [TestFixture]
    public class SticksReplayCompatibilityTest
    {
        // Measured on v1.0.18a, before Sticks stopped using osu!'s joystick deadzone. A change to
        // any of these numbers means existing replays would play back differently.
        [TestCase(0.85f, 1f, 303, 7553731.737d)]
        [TestCase(0.95f, 1f, 197, 4908663.164d)]
        [TestCase(1f, 1f, 94, 2471176.240d)]
        [TestCase(0.95f, 0.8f, 475, 11812205.223d)]
        public void TestRecordedPositionsAlwaysGiveTheSameFlicks(float activation, float stickTravel, int expectedFlicks, double expectedChecksum)
        {
            List<SticksReplayFrame> frames = recordedPlay();
            var tracker = new SticksInputTracker { ActivationThreshold = activation };
            int flicks = 0;
            double checksum = 0;
            int index = 0;

            for (double time = frames[0].Time; time <= frames[^1].Time; time += 1)
            {
                while (index < frames.Count - 2 && frames[index + 1].Time <= time)
                    index++;

                SticksReplayFrame start = frames[index];
                SticksReplayFrame end = frames[index + 1];

                foreach (StickSide side in new[] { StickSide.Left, StickSide.Right })
                {
                    Vector2 position = SticksFramedReplayInputHandler.InterpolateStick(
                        side == StickSide.Left ? start.LeftStick : start.RightStick,
                        side == StickSide.Left ? end.LeftStick : end.RightStick,
                        time, start.Time, end.Time, stickTravel, activation);
                    long before = tracker.SequenceFor(side);
                    tracker.Update(side, position, SticksPlayfield.MapStickDistance(position, stickTravel), time);

                    if (tracker.SequenceFor(side) == before)
                        continue;

                    flicks++;
                    checksum += tracker.LastFlickFor(side).Time + tracker.LastFlickFor(side).Angle;
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(flicks, Is.EqualTo(expectedFlicks));
                // One flick more, fewer or at another moment moves this by far more than the
                // tolerance, which only absorbs rounding differences between platforms.
                Assert.That(checksum, Is.EqualTo(expectedChecksum).Within(0.5));
            });
        }

        /// <summary>
        /// A long, fixed sequence of pushes and partial returns whose peaks and resting points fall
        /// on both sides of every flick and recharge point.
        /// </summary>
        private static List<SticksReplayFrame> recordedPlay()
        {
            uint state = 0x5717C5;

            float next()
            {
                state = state * 1664525u + 1013904223u;
                return (state >> 8) / (float)(1 << 24);
            }

            var frames = new List<SticksReplayFrame>();
            double time = 0;
            Vector2 left = Vector2.Zero;
            Vector2 right = Vector2.Zero;
            frames.Add(new SticksReplayFrame(time, left, right));

            void moveTo(bool isLeft, Vector2 target, int steps)
            {
                Vector2 origin = isLeft ? left : right;

                for (int step = 1; step <= steps; step++)
                {
                    Vector2 position = origin + (target - origin) * (step / (float)steps);
                    if (isLeft) left = position;
                    else right = position;
                    time += 12 + next() * 9;
                    frames.Add(new SticksReplayFrame(time, left, right));
                }
            }

            for (int gesture = 0; gesture < 600; gesture++)
            {
                bool isLeft = next() < 0.5f;
                float angle = next() * MathF.Tau;
                float peak = 0.8f + next() * 0.25f;
                float settleAngle = angle + (next() - 0.5f) * 0.6f;
                float settle = 0.4f + next() * 0.4f;

                moveTo(isLeft, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * peak, 1 + (int)(next() * 4));
                moveTo(isLeft, new Vector2(MathF.Cos(settleAngle), MathF.Sin(settleAngle)) * settle, 1 + (int)(next() * 4));
            }

            return frames;
        }
    }
}
