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

        private void onPhysicalStickInputChanged()
        {
            // Match mouse replay capture: axis changes can trigger a sample immediately, while the
            // base recorder's 60 Hz limit still prevents controller polling rate from bloating files.
            RecordFrame(false);
        }

        protected override ReplayFrame HandleFrame(Vector2 mousePosition, List<SticksAction> actions, ReplayFrame previousFrame) =>
            captureFrame(Time.Current);

        private SticksReplayFrame captureFrame(double time) => new SticksReplayFrame(
            time,
            playfield.PhysicalStickVector(StickSide.Left),
            playfield.PhysicalStickVector(StickSide.Right));
    }
}
