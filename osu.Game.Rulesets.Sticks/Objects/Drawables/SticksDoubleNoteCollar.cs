using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    /// <summary>
    /// One textured quad per exact double. The overlap layer owns the shared texture;
    /// pooled collars only change their transform and visibility during gameplay.
    /// </summary>
    internal partial class SticksDoubleNoteCollar : Drawable
    {
        private Texture texture;
        private IShader shader;

        public SticksDoubleNoteCollar()
        {
            Origin = Anchor.Centre;
            Size = new Vector2(SticksDoubleNoteTexture.DrawWidth, SticksDoubleNoteTexture.DrawHeight);
            Alpha = 0;
        }

        public void SetTexture(Texture sharedTexture, IShader sharedShader)
        {
            texture = sharedTexture;
            shader = sharedShader;
            Invalidate(Invalidation.DrawNode);
        }

        protected override DrawNode CreateDrawNode() => new CollarDrawNode(this);

        private sealed class CollarDrawNode : DrawNode
        {
            private Texture texture;
            private IShader shader;
            private Quad quad;

            private new SticksDoubleNoteCollar Source => (SticksDoubleNoteCollar)base.Source;

            public CollarDrawNode(SticksDoubleNoteCollar source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();
                texture = Source.texture;
                shader = Source.shader;
                quad = Source.ScreenSpaceDrawQuad;
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);
                if (texture == null || shader == null)
                    return;

                shader.Bind();
                renderer.DrawQuad(texture, quad, DrawColourInfo.Colour);
                shader.Unbind();
            }
        }
    }
}
