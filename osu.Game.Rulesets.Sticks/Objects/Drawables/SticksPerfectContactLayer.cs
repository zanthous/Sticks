using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    /// <summary>
    /// A full-width white arc impact and outward coloured release, reserved for perfect heads.
    /// All geometry stays outside the timing ring. A single pooled drawable uses the
    /// ordinary white-pixel shader, without per-hit textures, drawables, or transforms.
    /// </summary>
    public partial class SticksPerfectContactLayer : Drawable
    {
        internal const int CAPACITY = 32;
        internal const double DURATION = 220;
        private readonly Contact[] contacts = new Contact[CAPACITY];
        private int next;
        private double now;
        private double previousTime = double.NaN;
        private IShader shader;
        private Texture texture;

        internal int ActiveCount { get; private set; }

        public SticksPerfectContactLayer()
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            AlwaysPresent = true;
            Blending = BlendingParameters.Additive;
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer, ShaderManager shaders)
        {
            texture = renderer.WhitePixel;
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        public void Trigger(float angle, float hitSpan, Color4 colour) => TriggerAt(Time.Current, angle, hitSpan, colour);

        internal void TriggerAt(double time, float angle, float hitSpan, Color4 colour)
        {
            UpdateAt(time);
            if (!contacts[next].Active)
                ActiveCount++;
            contacts[next] = new Contact(time, angle, Math.Clamp(hitSpan, 1, 360), colour, true);
            next = (next + 1) % contacts.Length;
            Invalidate(Invalidation.DrawNode);
        }

        public void Clear()
        {
            Array.Clear(contacts);
            next = ActiveCount = 0;
            previousTime = double.NaN;
            Invalidate(Invalidation.DrawNode);
        }

        protected override void Update()
        {
            base.Update();
            UpdateAt(Time.Current);
        }

        internal void UpdateAt(double time)
        {
            if (time < previousTime)
                Clear();
            now = previousTime = time;
            if (ActiveCount == 0)
                return;

            for (int i = 0; i < contacts.Length; i++)
            {
                if (contacts[i].Active && time - contacts[i].Time >= DURATION)
                {
                    contacts[i] = default;
                    ActiveCount--;
                }
            }
            Invalidate(Invalidation.DrawNode);
        }

        protected override DrawNode CreateDrawNode() => new ContactDrawNode(this);

        private readonly record struct Contact(double Time, float Angle, float Span, Color4 Colour, bool Active);

        private sealed class ContactDrawNode : DrawNode
        {
            private readonly Contact[] contacts = new Contact[CAPACITY];
            private double now;
            private int activeCount;
            private IShader shader;
            private Texture texture;
            private new SticksPerfectContactLayer Source => (SticksPerfectContactLayer)base.Source;

            public ContactDrawNode(SticksPerfectContactLayer source) : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();
                Array.Copy(Source.contacts, contacts, contacts.Length);
                now = Source.now;
                activeCount = Source.ActiveCount;
                shader = Source.shader;
                texture = Source.texture;
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);
                if (activeCount == 0 || shader == null || texture == null)
                    return;

                shader.Bind();
                foreach (Contact contact in contacts)
                {
                    double age = now - contact.Time;
                    if (!contact.Active || age < 0 || age >= DURATION)
                        continue;

                    float progress = (float)(age / DURATION);
                    float release = 1 - (1 - progress) * (1 - progress);
                    float fade = (1 - progress) * (1 - progress);
                    float span = contact.Span * (1 + 0.12f * release);
                    float radius = SticksPlayfield.GUIDE_RADIUS + 26 * release;

                    // A broad, solid echo of the entire hit arc, not a particle effect.
                    // The leading edge releases outward while the next notes remain inside.
                    arc(renderer, contact.Angle, span, radius, 12 - 5 * release, contact.Colour, fade * 0.8f);
                    float white = Math.Clamp(1 - (float)age / 110, 0, 1);
                    if (white > 0)
                        arc(renderer, contact.Angle, span, radius + 2, 6, Color4.White, white);

                    // The impact is anchored to the timing circle for the first 70 ms.
                    float impact = Math.Clamp(1 - (float)age / 70, 0, 1);
                    if (impact > 0)
                        arc(renderer, contact.Angle, contact.Span, SticksPlayfield.GUIDE_RADIUS, 8, Color4.White, impact);

                }
                shader.Unbind();
            }

            private void arc(IRenderer renderer, float angle, float span, float innerRadius, float thickness, Color4 colour, float alpha)
            {
                int segments = Math.Max(2, (int)Math.Ceiling(span / 2));
                float start = (angle - span / 2) * MathF.PI / 180;
                float step = span * MathF.PI / 180 / segments;
                Vector2 centre = new Vector2(SticksPlayfield.SIZE / 2);
                Vector2 previous = new Vector2(MathF.Cos(start), MathF.Sin(start));
                ColourInfo tint = ColourInfo.SingleColour(colour).MultiplyAlpha(alpha * DrawColourInfo.Colour.MaxAlpha);

                for (int i = 1; i <= segments; i++)
                {
                    float radians = start + step * i;
                    Vector2 next = new Vector2(MathF.Cos(radians), MathF.Sin(radians));
                    Quad quad = new Quad(
                        Vector2Extensions.Transform(centre + previous * innerRadius, DrawInfo.Matrix),
                        Vector2Extensions.Transform(centre + previous * (innerRadius + thickness), DrawInfo.Matrix),
                        Vector2Extensions.Transform(centre + next * innerRadius, DrawInfo.Matrix),
                        Vector2Extensions.Transform(centre + next * (innerRadius + thickness), DrawInfo.Matrix));
                    renderer.DrawQuad(texture, quad, tint);
                    previous = next;
                }
            }
        }
    }
}
