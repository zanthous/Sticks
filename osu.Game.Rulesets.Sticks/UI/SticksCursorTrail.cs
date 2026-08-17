#nullable disable

using System;
using System.Runtime.InteropServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shaders.Types;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.Visualisation;
using osu.Framework.Timing;
using osuTK;
using osuTK.Graphics;
using osuTK.Graphics.ES30;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// A fixed-memory, spatially interpolated cursor trail driven by a stick cursor.
    /// Its rendering follows osu!'s cursortrail model: overlapping texture stamps fade
    /// according to their age in the CursorTrail shader rather than appearing as sampled dots.
    /// </summary>
    [DrawVisualiserHidden]
    public partial class SticksCursorTrail : Drawable
    {
        private const int max_parts = 1024;
        private const double fade_duration = 180;

        private readonly TrailPart[] parts = new TrailPart[max_parts];
        private readonly string textureName;
        private int currentIndex;
        private IShader shader;
        private Texture texture;
        private Vector2 partScale = Vector2.One;
        private Vector2? lastPosition;
        private double timeOffset;
        private float time;

        public SticksCursorTrail(string textureName)
        {
            this.textureName = textureName;
            Clock = new FramedClock();
            Size = new Vector2(SticksPlayfield.SIZE);
            Colour = Color4.White;
            Blending = BlendingParameters.Additive;
            Depth = -15;
            Alpha = 0;

            for (int i = 0; i < parts.Length; i++)
                parts[i].InvalidationID = -1;
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer, ShaderManager shaders, TextureStore textures)
        {
            texture = textures.Get(textureName) ?? renderer.WhitePixel;
            shader = shaders.Load(@"CursorTrail", FragmentShaderDescriptor.TEXTURE);
            partScale = Vector2.One;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            resetTime();
        }

        public override bool IsPresent => true;

        /// <summary>
        /// Adds the current cursor position, filling the distance from the previous position
        /// with overlapping trail stamps. The supplied position is in screen space.
        /// </summary>
        public void AddPosition(Vector2 screenSpacePosition)
        {
            if (texture == null)
                return;

            Vector2 position = ToLocalSpace(screenSpacePosition);

            if (!lastPosition.HasValue)
            {
                lastPosition = position;
                addPart(position);
                return;
            }

            Vector2 difference = position - lastPosition.Value;
            float distance = difference.Length;
            float interval = Math.Max(1, texture.DisplayWidth / 6.25f);

            if (distance < interval)
                return;

            Vector2 direction = difference / distance;
            int partCount = (int)(distance / interval);

            for (int i = 0; i < partCount; i++)
            {
                lastPosition += direction * interval;
                addPart(lastPosition.Value);
            }
        }

        /// <summary>
        /// Clears all visible history and prevents a line from being drawn from a stale position
        /// when trails are enabled again.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < parts.Length; i++)
                parts[i].InvalidationID = -1;

            currentIndex = 0;
            lastPosition = null;
            Invalidate(Invalidation.DrawNode);
        }

        protected override void Update()
        {
            base.Update();
            Invalidate(Invalidation.DrawNode);

            const int fade_clock_reset_threshold = 1000000;

            time = (float)((Time.Current - timeOffset) / fade_duration);
            if (time > fade_clock_reset_threshold)
                resetTime();
        }

        private void resetTime()
        {
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].Time -= time;

                if (parts[i].InvalidationID != -1)
                    parts[i].InvalidationID++;
            }

            time = 0;
            timeOffset = Time.Current;
        }

        private void addPart(Vector2 localSpacePosition)
        {
            parts[currentIndex].Position = localSpacePosition;
            parts[currentIndex].Time = time + 1;
            parts[currentIndex].InvalidationID++;
            currentIndex = (currentIndex + 1) % max_parts;
        }

        protected override DrawNode CreateDrawNode() => new TrailDrawNode(this);

        private struct TrailPart
        {
            public Vector2 Position;
            public float Time;
            public long InvalidationID;
        }

        private class TrailDrawNode : DrawNode
        {
            protected new SticksCursorTrail Source => (SticksCursorTrail)base.Source;

            private readonly TrailPart[] parts = new TrailPart[max_parts];
            private IShader shader;
            private Texture texture;
            private float time;
            private Vector2 partScale;
            private IVertexBatch<TexturedTrailVertex> vertexBatch;
            private IUniformBuffer<CursorTrailParameters> cursorTrailParameters;

            public TrailDrawNode(SticksCursorTrail source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                shader = Source.shader;
                texture = Source.texture;
                time = Source.time;
                partScale = Source.partScale;
                Source.parts.CopyTo(parts, 0);
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (shader == null || texture == null)
                    return;

                vertexBatch ??= renderer.CreateQuadBatch<TexturedTrailVertex>(max_parts, 1);
                cursorTrailParameters ??= renderer.CreateUniformBuffer<CursorTrailParameters>();
                cursorTrailParameters.Data = cursorTrailParameters.Data with
                {
                    FadeClock = time,
                    FadeExponent = 2.2f,
                };

                shader.Bind();
                shader.BindUniformBlock("m_CursorTrailParameters", cursorTrailParameters);
                texture.Bind();

                RectangleF textureRect = texture.GetTextureRect();
                float halfWidth = texture.DisplayWidth * partScale.X / 2;
                float halfHeight = texture.DisplayHeight * partScale.Y / 2;

                renderer.PushLocalMatrix(DrawInfo.Matrix);

                foreach (TrailPart part in parts)
                {
                    if (part.InvalidationID == -1 || time - part.Time >= 1)
                        continue;

                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = part.Position + new Vector2(-halfWidth, halfHeight),
                        TexturePosition = textureRect.BottomLeft,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = DrawColourInfo.Colour.BottomLeft.Linear,
                        Time = part.Time,
                    });
                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = part.Position + new Vector2(halfWidth, halfHeight),
                        TexturePosition = textureRect.BottomRight,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = DrawColourInfo.Colour.BottomRight.Linear,
                        Time = part.Time,
                    });
                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = part.Position + new Vector2(halfWidth, -halfHeight),
                        TexturePosition = textureRect.TopRight,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = DrawColourInfo.Colour.TopRight.Linear,
                        Time = part.Time,
                    });
                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = part.Position + new Vector2(-halfWidth, -halfHeight),
                        TexturePosition = textureRect.TopLeft,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = DrawColourInfo.Colour.TopLeft.Linear,
                        Time = part.Time,
                    });
                }

                renderer.PopLocalMatrix();
                vertexBatch.Draw();
                shader.Unbind();
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);
                vertexBatch?.Dispose();
                cursorTrailParameters?.Dispose();
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private record struct CursorTrailParameters
            {
                public UniformFloat FadeClock;
                public UniformFloat FadeExponent;
                private readonly UniformPadding8 pad1;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TexturedTrailVertex : IEquatable<TexturedTrailVertex>, IVertex
        {
            [VertexMember(2, VertexAttribPointerType.Float)]
            public Vector2 Position;

            [VertexMember(4, VertexAttribPointerType.Float)]
            public Color4 Colour;

            [VertexMember(2, VertexAttribPointerType.Float)]
            public Vector2 TexturePosition;

            [VertexMember(4, VertexAttribPointerType.Float)]
            public Vector4 TextureRect;

            [VertexMember(1, VertexAttribPointerType.Float)]
            public float Time;

            public bool Equals(TexturedTrailVertex other) =>
                Position.Equals(other.Position)
                && TexturePosition.Equals(other.TexturePosition)
                && Colour.Equals(other.Colour)
                && Time.Equals(other.Time);
        }
    }
}
