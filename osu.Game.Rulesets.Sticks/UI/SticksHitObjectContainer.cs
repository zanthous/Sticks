// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks.UI
{
    public partial class SticksHitObjectContainer : HitObjectContainer
    {
        protected override int Compare(Drawable x, Drawable y)
        {
            if (x is DrawableHitObject xObject && y is DrawableHitObject yObject)
            {
                bool xIsSlider = xObject.HitObject is SticksSlider;
                bool yIsSlider = yObject.HitObject is SticksSlider;

                if (xIsSlider != yIsSlider)
                    return xIsSlider ? -1 : 1;
            }

            return base.Compare(x, y);
        }
    }
}
