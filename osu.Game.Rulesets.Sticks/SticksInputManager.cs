using System.ComponentModel;
using osu.Framework.Allocation;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Handlers.Joystick;
using osu.Framework.Platform;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks
{
    public partial class SticksInputManager : RulesetInputManager<SticksAction>
    {
        [Resolved]
        private GameHost host { get; set; }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            EnableJoystickInput(host);
        }

        internal static void EnableJoystickInput(GameHost host)
        {
            // This is the same persisted handler setting exposed by Device: Joystick/Gamepad.
            foreach (var handler in host.AvailableInputHandlers)
            {
                if (handler is JoystickHandler)
                    handler.Enabled.Value = true;
            }
        }

        public SticksInputManager(RulesetInfo ruleset)
            : base(ruleset, 0, SimultaneousBindingMode.Unique)
        {
        }
    }

    public enum SticksAction
    {
        [Description("Focus playfield")]
        Focus,

        [Description("Flick tool")]
        EditorFlickTool = 10000,

        [Description("Slider tool")]
        EditorSliderTool,

        [Description("Click tool")]
        EditorClickTool,

        [Description("Slice tool")]
        EditorSliceTool,
    }
}
