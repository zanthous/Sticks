// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using osu.Framework.Input;
using osu.Framework.Input.StateChanges;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Replays
{
    public class SticksFramedReplayInputHandler : FramedReplayInputHandler<SticksReplayFrame>
    {
        private readonly SticksReplayInputProvider inputProvider;
        private readonly Func<float> physicalStickDistanceAtGameEdge;
        private readonly Func<float> flickActivationThreshold;

        public SticksFramedReplayInputHandler(Replay replay, SticksReplayInputProvider inputProvider,
                                              Func<float> physicalStickDistanceAtGameEdge = null,
                                              Func<float> flickActivationThreshold = null)
            : base(replay)
        {
            this.inputProvider = inputProvider;
            this.physicalStickDistanceAtGameEdge = physicalStickDistanceAtGameEdge ?? (() => 1);
            this.flickActivationThreshold = flickActivationThreshold ?? (() => SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD);
        }

        protected override bool IsImportant(SticksReplayFrame frame) => true;

        protected override void CollectReplayInputs(List<IInput> inputs)
        {
            if (CurrentFrame == null)
            {
                // Match the original tracker's initial observation. Injecting neutral before a
                // held-out first sample would arm the replay and manufacture a first flick that
                // never occurred in the recorded play.
                inputProvider.Update(
                    HasFrames ? StartFrame.LeftStick : Vector2.Zero,
                    HasFrames ? StartFrame.RightStick : Vector2.Zero);
                return;
            }

            float edgeDistance = physicalStickDistanceAtGameEdge();
            float activationThreshold = flickActivationThreshold();
            inputProvider.Update(
                InterpolateStick(StartFrame.LeftStick, EndFrame.LeftStick, CurrentTime, StartFrame.Time, EndFrame.Time, edgeDistance, activationThreshold),
                InterpolateStick(StartFrame.RightStick, EndFrame.RightStick, CurrentTime, StartFrame.Time, EndFrame.Time, edgeDistance, activationThreshold));
        }

        /// <summary>
        /// Interpolates analogue path motion without inventing gameplay-state transitions.
        /// Neutral and activation crossings occur only when their recorded endpoint is reached.
        /// </summary>
        public static Vector2 InterpolateStick(Vector2 start, Vector2 end, double time, double startTime, double endTime,
                                               float physicalDistanceAtGameEdge = 1,
                                               float flickActivationThreshold = SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD)
        {
            if (time <= startTime || endTime <= startTime)
                return start;

            if (time >= endTime)
                return end;

            float progress = (float)((time - startTime) / (endTime - startTime));
            float startMagnitude = start.Length;
            float endMagnitude = end.Length;
            float rechargeThreshold = SticksInputTracker.RechargeThresholdFor(flickActivationThreshold);

            Vector2 value;

            if (startMagnitude <= rechargeThreshold && endMagnitude <= rechargeThreshold)
            {
                // The neutral disc is convex, so Cartesian interpolation cannot accidentally
                // leave it and reads naturally when the stick passes through its centre.
                value = start + (end - start) * progress;
            }
            else
            {
                // Interpolating Cartesian coordinates across a wide angular change can cut
                // through neutral and falsely re-arm the stick. Follow the physical polar path.
                float magnitude = startMagnitude + (endMagnitude - startMagnitude) * progress;
                float startAngle = angleOf(startMagnitude > 0 ? start : end);
                float endAngle = angleOf(endMagnitude > 0 ? end : start);
                float angle = startAngle + shortestAngleDelta(startAngle, endAngle) * progress;

                float radians = angle * MathF.PI / 180;
                value = new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * magnitude;
            }

            float valueMagnitude = value.Length;
            float activationMagnitude = Math.Clamp(flickActivationThreshold,
                SticksInputTracker.MIN_ACTIVATION_THRESHOLD,
                SticksInputTracker.MAX_ACTIVATION_THRESHOLD) * Math.Clamp(physicalDistanceAtGameEdge, 0.01f, 1);

            if (startMagnitude < activationMagnitude && endMagnitude >= activationMagnitude && valueMagnitude >= activationMagnitude)
                value = withMagnitude(value, Math.Max(0, activationMagnitude - 0.0001f));

            if (startMagnitude > rechargeThreshold
                && endMagnitude <= rechargeThreshold
                && valueMagnitude <= rechargeThreshold)
                value = withMagnitude(value, rechargeThreshold + 0.0001f);

            return value;
        }

        private static float angleOf(Vector2 value) => MathF.Atan2(value.Y, value.X) * 180 / MathF.PI;

        private static float shortestAngleDelta(float start, float end) => (end - start + 540) % 360 - 180;

        private static Vector2 withMagnitude(Vector2 value, float magnitude)
        {
            if (value.LengthSquared == 0)
                return new Vector2(magnitude, 0);

            return value.Normalized() * magnitude;
        }
    }
}
