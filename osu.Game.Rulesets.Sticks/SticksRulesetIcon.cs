// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osuTK;

namespace osu.Game.Rulesets.Sticks
{
    public partial class SticksRulesetIcon : SpriteIcon
    {
        public SticksRulesetIcon()
        {
            // Match lazer's built-in ruleset icons: a single intrinsically-sized drawable is safe in
            // both ConstrainedIconContainer and auto-sized horizontal flows used throughout song select.
            Size = new Vector2(32);
            Icon = FontAwesome.Solid.Gamepad;
        }
    }
}
