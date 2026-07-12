// Copyright (c) Zanthous. Licensed under the MIT Licence.

#nullable enable

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
            // Legacy frames only provide two analogue values. Retain left-stick compatibility;
            // native lazer replay/autoplay frames carry both sticks directly.
            LeftStick = currentFrame.Position;
            RightStick = Vector2.Zero;
        }

        public LegacyReplayFrame ToLegacy(IBeatmap beatmap) => new LegacyReplayFrame(Time, LeftStick.X, LeftStick.Y, ReplayButtonState.None);

        public override bool IsEquivalentTo(ReplayFrame other) => other is SticksReplayFrame frame
                                                                 && Time == frame.Time
                                                                 && LeftStick == frame.LeftStick
                                                                 && RightStick == frame.RightStick;
    }
}
