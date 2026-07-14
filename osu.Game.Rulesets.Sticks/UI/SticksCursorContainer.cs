// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osu.Framework.Graphics;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks.UI
{
    public partial class SticksCursorContainer : GameplayCursorContainer
    {
        public SticksCursorContainer()
        {
            Alpha = 0;
            AlwaysPresent = true;
        }

        protected override Drawable CreateCursor() => Empty();
    }
}
