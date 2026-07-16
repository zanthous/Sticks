// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

#nullable enable

using System;
using osu.Game.Beatmaps;
using osu.Game.Replays.Legacy;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Replays.Types;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Replays
{
    public class SticksReplayFrame : ReplayFrame, IConvertibleReplayFrame
    {
        public Vector2 LeftStick;

        public Vector2 RightStick;

        public SticksReplayFrame()
        {
        }

        public SticksReplayFrame(double time, Vector2 leftStick, Vector2 rightStick)
            : base(time)
        {
            LeftStick = leftStick;
            RightStick = rightStick;
        }

        public void FromLegacy(LegacyReplayFrame currentFrame, IBeatmap beatmap, ReplayFrame? lastFrame = null)
        {
            LeftStick = currentFrame.Position;
            RightStick = unpackRightStick(currentFrame.ButtonState);
        }

        public LegacyReplayFrame ToLegacy(IBeatmap beatmap) => new LegacyReplayFrame(Time, LeftStick.X, LeftStick.Y, packRightStick(RightStick));

        public override bool IsEquivalentTo(ReplayFrame other) => other is SticksReplayFrame frame
                                                                 && Time == frame.Time
                                                                 && LeftStick == frame.LeftStick
                                                                 && RightStick == frame.RightStick;

        /// <summary>
        /// The stock replay bridge only offers two floats and a 32-bit button field. Sticks uses
        /// the floats for the physical left stick and stores the physical right stick as two signed
        /// Q15 axes in the otherwise-unused button field. A zero field remains compatible with
        /// earlier Sticks replays, where the right stick was always absent.
        /// </summary>
        private static ReplayButtonState packRightStick(Vector2 stick)
        {
            short x = encodeAxis(stick.X);
            short y = encodeAxis(stick.Y);
            uint packed = (ushort)x | ((uint)(ushort)y << 16);
            return (ReplayButtonState)unchecked((int)packed);
        }

        private static Vector2 unpackRightStick(ReplayButtonState state)
        {
            uint packed = unchecked((uint)(int)state);
            short x = unchecked((short)(packed & ushort.MaxValue));
            short y = unchecked((short)(packed >> 16));
            return new Vector2(decodeAxis(x), decodeAxis(y));
        }

        private static short encodeAxis(float value)
        {
            if (!float.IsFinite(value))
                value = 0;

            return (short)MathF.Round(Math.Clamp(value, -1, 1) * short.MaxValue);
        }

        private static float decodeAxis(short value) => Math.Clamp(value / (float)short.MaxValue, -1, 1);
    }
}
