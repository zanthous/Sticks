using osu.Game.Rulesets.Sticks.UI;
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
        private bool leftTrigger;
        private bool rightTrigger;
        private bool leftShoulder;
        private bool rightShoulder;
        private bool leftStickButton;
        private bool rightStickButton;
        private float flickActivationThreshold = SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD;
        private volatile bool active;

        public bool Active => active;

        public float FlickActivationThreshold
        {
            get
            {
                lock (sync)
                    return flickActivationThreshold;
            }
        }

        public void Update(Vector2 left, Vector2 right,
                           bool leftTrigger = false, bool rightTrigger = false,
                           bool leftShoulder = false, bool rightShoulder = false,
                           bool leftStickButton = false, bool rightStickButton = false,
                           float flickActivationThreshold = SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD)
        {
            lock (sync)
            {
                leftStick = left;
                rightStick = right;
                this.leftTrigger = leftTrigger;
                this.rightTrigger = rightTrigger;
                this.leftShoulder = leftShoulder;
                this.rightShoulder = rightShoulder;
                this.leftStickButton = leftStickButton;
                this.rightStickButton = rightStickButton;
                this.flickActivationThreshold = flickActivationThreshold;
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
                leftTrigger = false;
                rightTrigger = false;
                leftShoulder = false;
                rightShoulder = false;
                leftStickButton = false;
                rightStickButton = false;
                flickActivationThreshold = SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD;
                active = false;
            }
        }

        public (Vector2 Left, Vector2 Right, bool LeftTrigger, bool RightTrigger, bool LeftShoulder, bool RightShoulder, bool LeftStickButton, bool RightStickButton) SnapshotWithAllButtons()
        {
            lock (sync)
                return (leftStick, rightStick, leftTrigger, rightTrigger, leftShoulder, rightShoulder, leftStickButton, rightStickButton);
        }

        public (Vector2 Left, Vector2 Right) Snapshot()
        {
            lock (sync)
                return (leftStick, rightStick);
        }

        public (Vector2 Left, Vector2 Right, bool LeftTrigger, bool RightTrigger, bool LeftShoulder, bool RightShoulder) SnapshotWithButtons()
        {
            lock (sync)
                return (leftStick, rightStick, leftTrigger, rightTrigger, leftShoulder, rightShoulder);
        }
    }
}
