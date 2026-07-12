// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    public partial class SticksPlayfieldAdjustmentContainer : PlayfieldAdjustmentContainer
    {
        protected override Container<Drawable> Content => content;
        private readonly ScalingContainer content;

        public SticksPlayfieldAdjustmentContainer()
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;
            Size = new Vector2(0.92f);

            InternalChild = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Fit,
                FillAspectRatio = 1,
                Child = content = new ScalingContainer { RelativeSizeAxes = Axes.Both },
            };
        }

        private partial class ScalingContainer : Container
        {
            protected override void Update()
            {
                base.Update();
                Scale = new Vector2(Parent!.ChildSize.X / SticksPlayfield.SIZE);
                Size = Vector2.Divide(Vector2.One, Scale);
            }
        }
    }
}
