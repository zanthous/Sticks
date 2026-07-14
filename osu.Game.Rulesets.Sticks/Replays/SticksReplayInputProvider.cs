// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osuTK;

namespace osu.Game.Rulesets.Sticks.Replays
{
    /// <summary>
    /// Transfers interpolated replay stick positions directly to the playfield without synthesising
    /// framework joystick events. This remains render-rate independent and allocation-free.
    /// </summary>
    public sealed class SticksReplayInputProvider
    {
        private readonly object sync = new object();
        private Vector2 leftStick;
        private Vector2 rightStick;
        private volatile bool active;

        public bool Active => active;

        public void Update(Vector2 left, Vector2 right)
        {
            lock (sync)
            {
                leftStick = left;
                rightStick = right;
                active = true;
            }
        }

        /// <summary>
        /// Stops replay input and discards its last position.
        /// </summary>
        /// <remarks>
        /// Editor test play can detach an autoplay replay without recreating the drawable
        /// ruleset. The final replay frame must not continue acting as live controller input.
        /// </remarks>
        public void Deactivate()
        {
            lock (sync)
            {
                leftStick = Vector2.Zero;
                rightStick = Vector2.Zero;
                active = false;
            }
        }

        public (Vector2 Left, Vector2 Right) Snapshot()
        {
            lock (sync)
                return (leftStick, rightStick);
        }
    }
}
