using System;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// Tracks whether a duration object has been entered using a fresh flick. Starting this
    /// tracker while a stick is already held out intentionally does not authorise tracking.
    /// </summary>
    public sealed class SticksTrackingEligibility
    {
        private long observedSequence;

        public bool IsAuthorised { get; private set; }

        public void Reset(long currentSequence)
        {
            observedSequence = currentSequence;
            IsAuthorised = false;
        }

        /// <summary>
        /// Observes the input track and returns whether a new physical flick was seen. The
        /// caller must still claim the gesture before calling <see cref="Authorise"/>; this
        /// prevents a flick already consumed by another note from arming this duration object.
        /// </summary>
        public bool Observe(long currentSequence, SticksInputTracker.FlickEvent flick, double earliestTime, double latestTime,
                            float targetAngle, float lenientHalfAngle, out bool canAuthorise)
        {
            canAuthorise = false;

            if (currentSequence == observedSequence)
                return false;

            observedSequence = currentSequence;
            canAuthorise = flick.Sequence == currentSequence
                           && flick.Time >= earliestTime
                           && flick.Time <= latestTime
                           && Math.Abs(SticksHitObject.DeltaAngle(flick.Angle, targetAngle)) <= lenientHalfAngle;
            return true;
        }

        public void Authorise() => IsAuthorised = true;
    }
}
