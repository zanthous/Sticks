using System.ComponentModel;
using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks
{
    public partial class SticksInputManager : RulesetInputManager<SticksAction>
    {
        public SticksInputManager(RulesetInfo ruleset)
            : base(ruleset, 0, SimultaneousBindingMode.Unique)
        {
        }
    }

    public enum SticksAction
    {
        [Description("Focus playfield")]
        Focus,

#if STICKS_RULESET_API_2026_818
        [Description("Flick tool")]
        EditorFlickTool = 10000,

        [Description("Slider tool")]
        EditorSliderTool,

        [Description("Click tool")]
        EditorClickTool,
#endif
    }
}
