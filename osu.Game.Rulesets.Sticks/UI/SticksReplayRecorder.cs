// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System.Collections.Generic;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    public partial class SticksReplayRecorder : ReplayRecorder<SticksAction>
    {
        public SticksReplayRecorder(Score score)
            : base(score)
        {
        }

        protected override ReplayFrame HandleFrame(Vector2 mousePosition, List<SticksAction> actions, ReplayFrame previousFrame) =>
            new SticksReplayFrame(Time.Current, Vector2.Zero, Vector2.Zero);
    }
}
