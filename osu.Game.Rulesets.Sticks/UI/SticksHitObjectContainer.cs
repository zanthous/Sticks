using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks.UI
{
    public partial class SticksHitObjectContainer : HitObjectContainer
    {
        internal IEnumerable<DrawableHitObject> VisibleObjects => AliveEntries.Values;

        protected override int Compare(Drawable x, Drawable y)
        {
            if (x is DrawableHitObject xObject && y is DrawableHitObject yObject)
            {
                bool xIsClick = xObject.HitObject is SticksClick;
                bool yIsClick = yObject.HitObject is SticksClick;

                // A full halo must remain behind every directional head, regardless of
                // timestamps or insertion order. Keep ordinary ordering between halos.
                if (xIsClick != yIsClick)
                    return xIsClick ? -1 : 1;

                bool xIsDurationBody = xObject.HitObject is SticksSlider or SticksHold;
                bool yIsDurationBody = yObject.HitObject is SticksSlider or SticksHold;

                if (xIsDurationBody != yIsDurationBody)
                    return xIsDurationBody ? -1 : 1;
            }

            return base.Compare(x, y);
        }
    }
}
