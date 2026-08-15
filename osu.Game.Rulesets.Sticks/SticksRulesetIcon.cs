// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osuTK;

namespace osu.Game.Rulesets.Sticks
{
    public partial class SticksRulesetIcon : CompositeDrawable
    {
        private readonly Sprite icon;
        private TextureStore textureStore;

        public SticksRulesetIcon()
        {
            // Song select places ruleset icons directly inside an auto-sized horizontal flow.
            // The root must therefore have an intrinsic size rather than relative sizing.
            Size = new Vector2(32);

            InternalChild = icon = new Sprite
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                FillMode = FillMode.Fit,
            };
        }

        [BackgroundDependencyLoader]
        private void load(GameHost host)
        {
            var resources = new NamespacedResourceStore<byte[]>(
                new DllResourceStore(typeof(SticksRulesetIcon).Assembly),
                @"Resources/Textures");

            textureStore = new TextureStore(host.Renderer, host.CreateTextureLoaderStore(resources));
            icon.Texture = textureStore.Get("Icon/sticks_icon");
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (isDisposing)
                textureStore?.Dispose();
        }
    }
}
