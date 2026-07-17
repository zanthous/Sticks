// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Collections.Generic;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    public partial class SticksReplayRecorder : ReplayRecorder<SticksAction>
    {
        private readonly SticksPlayfield playfield;

        public SticksReplayRecorder(Score score, SticksPlayfield playfield)
            : base(score)
        {
            this.playfield = playfield;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            playfield.PhysicalStickInputChanged += onPhysicalStickInputChanged;
        }

        protected override void Dispose(bool isDisposing)
        {
            playfield.PhysicalStickInputChanged -= onPhysicalStickInputChanged;
            base.Dispose(isDisposing);
        }

        private void onPhysicalStickInputChanged(bool important)
        {
            // Neutral and activation crossings are equivalent to button edges and must bypass the
            // ordinary 60 Hz movement limit. Other analogue changes remain rate-limited.
            RecordFrame(important);
        }

        protected override ReplayFrame HandleFrame(Vector2 mousePosition, List<SticksAction> actions, ReplayFrame previousFrame) =>
            captureFrame(Time.Current);

        private SticksReplayFrame captureFrame(double time) => new SticksReplayFrame(
            time,
            playfield.PhysicalStickVector(StickSide.Left),
            playfield.PhysicalStickVector(StickSide.Right));
    }
}
