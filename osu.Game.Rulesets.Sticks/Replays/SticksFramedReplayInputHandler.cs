// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System.Collections.Generic;
using osu.Framework.Input;
using osu.Framework.Input.StateChanges;
using osu.Framework.Utils;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.Sticks.Replays
{
    public class SticksFramedReplayInputHandler : FramedReplayInputHandler<SticksReplayFrame>
    {
        private readonly SticksReplayInputProvider inputProvider;

        public SticksFramedReplayInputHandler(Replay replay, SticksReplayInputProvider inputProvider)
            : base(replay)
        {
            this.inputProvider = inputProvider;
        }

        protected override bool IsImportant(SticksReplayFrame frame) => true;

        protected override void CollectReplayInputs(List<IInput> inputs)
        {
            var leftStick = Interpolation.ValueAt(CurrentTime, StartFrame.LeftStick, EndFrame.LeftStick, StartFrame.Time, EndFrame.Time);
            var rightStick = Interpolation.ValueAt(CurrentTime, StartFrame.RightStick, EndFrame.RightStick, StartFrame.Time, EndFrame.Time);

            inputProvider.Update(leftStick, rightStick);
        }
    }
}
