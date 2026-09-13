using System;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Edit
{
    /// <summary>
    /// Shared tracing behaviour for the first slider span and subsequent points.
    /// Pointer motion chooses the arc; the editor clock alone chooses its duration.
    /// </summary>
    internal sealed class SticksSliderPlacementGesture
    {
        private readonly double startTime;
        private readonly double earliestEndTime;
        private float lastPointerAngle;
        private float rawArc;
        private bool hasPointer = true;

        public float Arc { get; private set; }
        public double Duration { get; private set; }
        public bool CanCommit => Duration > 0;

        public SticksSliderPlacementGesture(float startAngle, double startTime, double initialClockTime)
        {
            lastPointerAngle = startAngle;
            this.startTime = startTime;
            earliestEndTime = Math.Max(startTime, initialClockTime) + 0.5;
        }

        public void UpdateTime(double time)
        {
            Duration = double.IsFinite(time) && time > earliestEndTime ? time - startTime : 0;
        }

        public void UpdatePointer(float angle, bool valid, bool snap)
        {
            if (!valid)
            {
                hasPointer = false;
                return;
            }

            if (hasPointer)
                rawArc += SticksHitObject.DeltaAngle(lastPointerAngle, angle);

            hasPointer = true;
            lastPointerAngle = angle;
            Arc = snap ? SticksEditorCoordinates.SnapAngleOffset(rawArc) : Math.Abs(rawArc) < 0.01f ? 0 : rawArc;
        }
    }
}
