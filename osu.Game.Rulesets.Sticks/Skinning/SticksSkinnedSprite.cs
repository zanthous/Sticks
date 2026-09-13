#nullable enable

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Skinning
{
    /// <summary>
    /// An optional skin image with a retained procedural fallback. Images fill the
    /// caller's bounds unless native skin dimensions are explicitly requested.
    /// </summary>
    public partial class SticksSkinnedSprite : SticksSkinReloadableDrawable
    {
        private readonly Sprite sprite;
        private readonly Container fallbackContainer;
        private string textureName;
        private bool nativeSkinSize;

        public bool UsesSkinTexture => Texture != null;
        public Texture? Texture => sprite.Texture;

        public string TextureName
        {
            get => textureName;
            set
            {
                ArgumentException.ThrowIfNullOrEmpty(value);
                if (textureName == value)
                    return;

                if (Name == textureName)
                    Name = value;
                textureName = value;
                InvalidateSkin();
            }
        }

        /// <summary>
        /// Tints only the custom image, leaving the fallback's colours unchanged.
        /// </summary>
        public Color4 TextureColour
        {
            get => sprite.Colour;
            set => sprite.Colour = value;
        }

        public bool NativeSkinSize
        {
            get => nativeSkinSize;
            set
            {
                if (nativeSkinSize == value)
                    return;

                nativeSkinSize = value;
                updateSpriteSize();
            }
        }

        public SticksSkinnedSprite(string textureName, Drawable? fallback = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(textureName);
            this.textureName = textureName;
            Name = textureName;
            // Optional markers hide themselves until an image is available. Keep their
            // scheduler alive so a subsequent skin change can make them visible again.
            AlwaysPresent = true;

            InternalChildren = new Drawable[]
            {
                fallbackContainer = new Container { RelativeSizeAxes = Axes.Both },
                sprite = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Alpha = 0,
                },
            };

            if (fallback != null)
                fallbackContainer.Add(fallback);
        }

        protected override void SkinChanged(ISkinSource? skin)
        {
            base.SkinChanged(skin);
            sprite.Texture = SticksSkinTextureLookup.Get(skin, textureName);
            sprite.Alpha = UsesSkinTexture ? 1 : 0;
            // Hiding the holder preserves the fallback's own alpha and any animations.
            fallbackContainer.Alpha = UsesSkinTexture ? 0 : 1;
            updateSpriteSize();
        }

        private void updateSpriteSize()
        {
            sprite.RelativeSizeAxes = nativeSkinSize ? Axes.None : Axes.Both;
            sprite.Size = nativeSkinSize ? Texture?.DisplaySize ?? Vector2.Zero : Vector2.One;
        }
    }
}
