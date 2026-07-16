// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

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

        public void Update(StickSide side, Vector2 value, double time) => Update(side, value, value, time);

        /// <summary>
        /// Updates a stick using its physical controller position for neutral charging and its
        /// mapped gameplay position for edge crossing. This keeps the neutral requirement at
        /// 50% physical travel even when a difficulty setting maps 80% travel to the playfield edge.
        /// </summary>
        public void Update(StickSide side, Vector2 physicalValue, Vector2 gameplayValue, double time)
        {
            TrackedStick stick = tracked(side);
            float physicalMagnitude = Math.Clamp(physicalValue.Length, 0, 1);
            float gameplayMagnitude = Math.Clamp(gameplayValue.Length, 0, 1);
            stick.Vector = gameplayValue.LengthSquared > 1 ? gameplayValue.Normalized() : gameplayValue;

            if (physicalMagnitude <= NEUTRAL_THRESHOLD)
                stick.NeutralReady = true;

            if (stick.NeutralReady && stick.PreviousGameplayMagnitude < FLICK_THRESHOLD && gameplayMagnitude >= FLICK_THRESHOLD)
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
