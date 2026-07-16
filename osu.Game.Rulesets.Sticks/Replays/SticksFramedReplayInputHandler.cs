// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Collections.Generic;
using osu.Framework.Input;
using osu.Framework.Input.StateChanges;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;
using osuTK;

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
            // Controller samples are gameplay state, not merely cursor positions. Interpolating
            // between them can cross the flick threshold before the physical input did and change
            // both timing and score. Hold each complete sample until its recorded successor.
            SticksReplayFrame frame = CurrentFrame;
            inputProvider.Update(frame?.LeftStick ?? Vector2.Zero, frame?.RightStick ?? Vector2.Zero);
        }
    }
}
