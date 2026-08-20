using System;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    internal sealed class SticksStrumButtonState
    {
        private bool initialised;

        public bool Pressed { get; private set; }

        /// <summary>
        /// Returns one edge for each physical press while ignoring a button which was already
        /// held when gameplay began.
        /// </summary>
        public bool Update(bool pressed)
        {
            bool risingEdge = initialised && pressed && !Pressed;
            Pressed = pressed;
            initialised = true;
            return risingEdge;
        }
    }

    public sealed class SticksInputTracker
    {
        public const float MIN_ACTIVATION_THRESHOLD = 0.85f;
        public const float MAX_ACTIVATION_THRESHOLD = 1f;
        public const float DEFAULT_ACTIVATION_THRESHOLD = 0.95f;
        public const float RECHARGE_OFFSET = 0.30f;

        private readonly TrackedStick left = new TrackedStick();
        private readonly TrackedStick right = new TrackedStick();
        private float activationThreshold = DEFAULT_ACTIVATION_THRESHOLD;

        /// <summary>
        /// Whether moving outwards creates gestures. Strum mode disables this and creates the
        /// same gesture records from trigger or shoulder-button presses instead.
        /// </summary>
        public bool FlickGesturesEnabled { get; set; } = true;

        /// <summary>
        /// Gameplay-radius boundary which an armed stick must cross outwards to create a flick.
        /// </summary>
        public float ActivationThreshold
        {
            get => activationThreshold;
            set => activationThreshold = Math.Clamp(value, MIN_ACTIVATION_THRESHOLD, MAX_ACTIVATION_THRESHOLD);
        }

        /// <summary>
        /// Physical-radius boundary which rearms a stick. Duration tracking uses the strict
        /// opposite side of this same boundary.
        /// </summary>
        public float RechargeThreshold => RechargeThresholdFor(ActivationThreshold);

        public static float RechargeThresholdFor(float activationThreshold) =>
            Math.Clamp(activationThreshold, MIN_ACTIVATION_THRESHOLD, MAX_ACTIVATION_THRESHOLD) - RECHARGE_OFFSET;

        public Vector2 VectorFor(StickSide side) => tracked(side).Vector;

        public Vector2 PhysicalVectorFor(StickSide side) => tracked(side).PhysicalVector;

        public bool IsBeyondRechargeBoundary(StickSide side) => PhysicalVectorFor(side).Length > RechargeThreshold;

        public long SequenceFor(StickSide side) => tracked(side).Sequence;

        public FlickEvent LastFlickFor(StickSide side) => tracked(side).LastFlick;

        /// <summary>
        /// Claims a detected gesture for one hit object. Multiple drawable flicks can have
        /// overlapping hit windows, but one physical gesture must never judge all of them.
        /// </summary>
        public bool TryConsumeFlick(StickSide side, long sequence)
        {
            TrackedStick stick = tracked(side);
            if (sequence != stick.Sequence || sequence <= stick.ConsumedSequence)
                return false;

            stick.ConsumedSequence = sequence;
            return true;
        }

        public void Update(StickSide side, Vector2 value, double time) => Update(side, value, value, time);

        /// <summary>
        /// Updates the direction used by Relax while deliberately ignoring physical magnitude.
        /// A remembered direction is represented at full gameplay radius so duration objects keep
        /// using their normal angular tracking rules.
        /// </summary>
        internal void UpdateRelaxDirection(StickSide side, Vector2 direction)
        {
            TrackedStick stick = tracked(side);
            Vector2 value = direction.LengthSquared > 0 ? direction.Normalized() : Vector2.Zero;
            stick.PhysicalVector = value;
            stick.Vector = value;
            stick.PreviousGameplayMagnitude = value.Length;
            stick.NeutralReady = false;
        }

        /// <summary>
        /// Creates a Relax gesture at the currently remembered direction without requiring a
        /// neutral crossing. The normal per-gesture consumption path still decides which object
        /// receives it.
        /// </summary>
        internal bool TriggerRelaxFlick(StickSide side, double time)
        {
            TrackedStick stick = tracked(side);
            if (stick.Vector.LengthSquared == 0)
                return false;

            float angle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Vector.Y, stick.Vector.X) * 180 / MathF.PI);
            stick.Sequence++;
            stick.LastFlick = new FlickEvent(stick.Sequence, time, angle);
            return true;
        }

        /// <summary>
        /// Creates a gesture from a strum-button press while the aimed stick is already beyond the
        /// configured gameplay activation boundary.
        /// </summary>
        internal bool TriggerStrum(StickSide side, double time)
        {
            TrackedStick stick = tracked(side);
            if (stick.Vector.Length < ActivationThreshold)
                return false;

            float angle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Vector.Y, stick.Vector.X) * 180 / MathF.PI);
            stick.Sequence++;
            stick.LastFlick = new FlickEvent(stick.Sequence, time, angle);
            return true;
        }

        /// <summary>
        /// Updates a stick using its physical controller position for neutral charging and its
        /// mapped gameplay position for edge crossing. The recharge boundary remains a physical
        /// stick distance even when a difficulty setting remaps the playfield edge.
        /// </summary>
        public void Update(StickSide side, Vector2 physicalValue, Vector2 gameplayValue, double time)
        {
            TrackedStick stick = tracked(side);
            float physicalMagnitude = Math.Clamp(physicalValue.Length, 0, 1);
            float gameplayMagnitude = Math.Clamp(gameplayValue.Length, 0, 1);
            stick.PhysicalVector = physicalValue.LengthSquared > 1 ? physicalValue.Normalized() : physicalValue;
            stick.Vector = gameplayValue.LengthSquared > 1 ? gameplayValue.Normalized() : gameplayValue;

            if (physicalMagnitude <= RechargeThreshold)
                stick.NeutralReady = true;

            if (FlickGesturesEnabled
                && stick.NeutralReady
                && stick.PreviousGameplayMagnitude < ActivationThreshold
                && gameplayMagnitude >= ActivationThreshold)
            {
                float angle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Vector.Y, stick.Vector.X) * 180 / MathF.PI);
                stick.Sequence++;
                stick.LastFlick = new FlickEvent(stick.Sequence, time, angle);
                stick.NeutralReady = false;
            }

            stick.PreviousGameplayMagnitude = gameplayMagnitude;
        }

        private TrackedStick tracked(StickSide side) => side == StickSide.Left ? left : right;

        private sealed class TrackedStick
        {
            public Vector2 PhysicalVector;
            public Vector2 Vector;
            public float PreviousGameplayMagnitude;
            // A stick must be observed inside the neutral zone after the tracker starts. Starting
            // gameplay while already held out must not manufacture a gesture on the first frame.
            public bool NeutralReady;
            public long Sequence;
            public long ConsumedSequence;
            public FlickEvent LastFlick;
        }

        public readonly record struct FlickEvent(long Sequence, double Time, float Angle);
    }
}
