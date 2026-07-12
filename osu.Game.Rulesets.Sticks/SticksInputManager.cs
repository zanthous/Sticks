// Copyright (c) Zanthous. Licensed under the MIT Licence.

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
    }
}
