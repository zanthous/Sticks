using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Handlers.Joystick;
using osu.Framework.Platform;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// Makes osu!'s "Joystick / Gamepad" input handler enabled whenever Sticks is active.
    /// This prevents a user from thinking the ruleset is not functiornal if they had the setting disabled.
    /// Sticks is not playable without a gamepad, so this should be fine.
    /// </summary>
    public static class SticksControllerInput
    {
        public const string ENABLED_MESSAGE = "Controller input was turned on automatically because Sticks requires it.";

        /// <summary>
        /// Turns the joystick handler on if it's off, and posts a notification saying so.
        /// </summary>
        public static void EnsureEnabled(GameHost host, INotificationOverlay notifications)
        {
            var handler = host?.AvailableInputHandlers.OfType<JoystickHandler>().FirstOrDefault();

            // Headless tests have no joystick handler.
            if (handler == null)
                return;

            host.UpdateThread.Scheduler.Add(() =>
            {
                if (handler.Enabled.Value)
                    return;

                handler.Enabled.Value = true;

                notifications?.Post(new SimpleNotification
                {
                    Text = ENABLED_MESSAGE,
                    Icon = FontAwesome.Solid.Gamepad,
                    IconColour = Colour4.FromHex("EEAA00"),
                });
            });
        }
    }
}
