// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    public sealed class SticksInputTracker
    {
        public const float NEUTRAL_THRESHOLD = 0.5f;
        public const float FLICK_THRESHOLD = 0.82f;

        private readonly TrackedStick left = new TrackedStick();
        private readonly TrackedStick right = new TrackedStick();

        public Vector2 VectorFor(StickSide side) => tracked(side).Vector;

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

        public void Update(StickSide side, Vector2 value, double time)
        {
            TrackedStick stick = tracked(side);
            float magnitude = Math.Clamp(value.Length, 0, 1);
            stick.Vector = value.LengthSquared > 1 ? value.Normalized() : value;

            if (magnitude <= NEUTRAL_THRESHOLD)
                stick.NeutralReady = true;

            if (stick.NeutralReady && stick.PreviousMagnitude < FLICK_THRESHOLD && magnitude >= FLICK_THRESHOLD)
            {
                float angle = SticksHitObject.NormaliseAngle(MathF.Atan2(stick.Vector.Y, stick.Vector.X) * 180 / MathF.PI);
                stick.Sequence++;
                stick.LastFlick = new FlickEvent(stick.Sequence, time, angle);
                stick.NeutralReady = false;
            }

            stick.PreviousMagnitude = magnitude;
        }

        private TrackedStick tracked(StickSide side) => side == StickSide.Left ? left : right;

        private sealed class TrackedStick
        {
            public Vector2 Vector;
            public float PreviousMagnitude;
            public bool NeutralReady = true;
            public long Sequence;
            public long ConsumedSequence;
            public FlickEvent LastFlick;
        }

        public readonly record struct FlickEvent(long Sequence, double Time, float Angle);
    }
}
