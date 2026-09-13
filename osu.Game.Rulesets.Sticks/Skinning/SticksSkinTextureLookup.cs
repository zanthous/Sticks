#nullable enable

using osu.Framework.Graphics.Textures;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Sticks.Skinning
{
    /// <summary>
    /// Looks up only explicitly named Sticks assets. The skin provider owns returned
    /// textures and handles source precedence and high-resolution variants.
    /// </summary>
    public static class SticksSkinTextureLookup
    {
        public static Texture? Get(ISkin? skin, string? textureName) =>
            skin == null || textureName == null ? null : skin.GetTexture(textureName);
    }
}
