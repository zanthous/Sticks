// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

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
    /// A fixed-memory, time-based particle simulation shared by center-out contact effects.
    /// Particles use real independent initial conditions and analytic drag rather than a repeating
    /// collection of scripted line segments, so their motion is independent of render framerate.
    /// </summary>
    public partial class SticksContactParticleEmitter : Drawable
    {
        private const int max_particles = 128;
        private const double mote_interval = 1000.0 / 42;
        private const double shard_interval = 1000.0 / 15;
        private const double maximum_lifetime = 320;
        private const double seek_reset_threshold = 500;

        private readonly Particle[] particles = new Particle[max_particles];
        private readonly uint emitterSeed;
        private int nextParticle;
        private uint sequence;
        private double currentTime;
        private double previousTime = double.NaN;
        private double nextMoteTime = double.NaN;
        private double nextShardTime = double.NaN;
        private double latestParticleEndTime = double.NegativeInfinity;
        private bool drewParticlesLastFrame;
        private float contactAngle;
        private float contactSpan;
        private Color4 contactColour = Color4.White;
        private bool emitting;
        private IShader shader = null!;
        private Texture texture = null!;

        public SticksContactParticleEmitter(double seed)
        {
            emitterSeed = hash((uint)Math.Abs(BitConverter.DoubleToInt64Bits(seed)));
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

        public void SetContinuousState(bool active, double now, float angle, float span, Color4 colour)
        {
            bool wasEmitting = emitting;

            // Inactive duration objects report their state every gameplay frame. Keep their
            // timelines current without copying an empty 128-particle buffer to the draw thread.
            if (!active && !wasEmitting)
            {
                currentTime = previousTime = now;
                return;
            }

            if (active)
            {
                contactAngle = angle;
                contactSpan = span;
                contactColour = colour;
            }

            if (!double.IsFinite(previousTime) || now < previousTime || now - previousTime > seek_reset_threshold)
                resetTimeline(now, clearParticles: now < previousTime || now - previousTime > seek_reset_threshold);

            if (active && !emitting)
            {
                // A restrained irregular pickup prevents tracking from feeling delayed while the
                // normal timestamp-driven cadence gets underway.
                emitBurst(now, angle, span, colour, 7, 1.0f);
                nextMoteTime = now + jitteredInterval(mote_interval);
                nextShardTime = now + jitteredInterval(shard_interval);
            }

            emitting = active;
            currentTime = now;

            if (emitting)
            {
                emitDueParticles(ref nextMoteTime, mote_interval, ParticleKind.Mote);
                emitDueParticles(ref nextShardTime, shard_interval, ParticleKind.Shard);
            }

            previousTime = now;
            Invalidate(Invalidation.DrawNode);
        }

        public void TriggerBurst(double now, float angle, float span, Color4 colour, int count, float intensity)
        {
            currentTime = now;
            contactAngle = angle;
            contactSpan = span;
            contactColour = colour;
            emitBurst(now, angle, span, colour, count, intensity);
            previousTime = now;
            Invalidate(Invalidation.DrawNode);
        }

        public void Clear()
        {
            Array.Clear(particles);
            nextParticle = 0;
            emitting = false;
            previousTime = double.NaN;
            nextMoteTime = double.NaN;
            nextShardTime = double.NaN;
            latestParticleEndTime = double.NegativeInfinity;
            drewParticlesLastFrame = false;
            Invalidate(Invalidation.DrawNode);
        }

        protected override void Update()
        {
            base.Update();
            currentTime = Time.Current;
            bool hasVisibleParticles = emitting || currentTime <= latestParticleEndTime;

            // Invalidate once more when the last particle expires so the draw thread drops it,
            // then remain completely dormant until the next emission.
            if (hasVisibleParticles || drewParticlesLastFrame)
                Invalidate(Invalidation.DrawNode);

            drewParticlesLastFrame = hasVisibleParticles;
        }

        private void resetTimeline(double now, bool clearParticles)
        {
            if (clearParticles)
            {
                Array.Clear(particles);
                latestParticleEndTime = double.NegativeInfinity;
                drewParticlesLastFrame = false;
            }

            nextMoteTime = now;
            nextShardTime = now;
        }

        private void emitDueParticles(ref double nextTime, double interval, ParticleKind kind)
        {
            if (!double.IsFinite(nextTime))
                nextTime = currentTime;

            // Bound catch-up work after hitches. Old particles are analytically expired anyway.
            if (currentTime - nextTime > maximum_lifetime)
                nextTime = currentTime;

            int emitted = 0;

            while (nextTime <= currentTime && emitted++ < 8)
            {
                emit(nextTime, contactAngle, contactSpan, contactColour, kind, 1);
                nextTime += jitteredInterval(interval);
            }
        }

        private double jitteredInterval(double interval) => interval * (0.75 + random01() * 0.5);

        private void emitBurst(double now, float angle, float span, Color4 colour, int count, float intensity)
        {
            for (int i = 0; i < count; i++)
            {
                ParticleKind kind = i % 3 == 2 ? ParticleKind.Shard : ParticleKind.Mote;
                // A few milliseconds of deterministic staggering keeps a burst organic without
                // making response to the hit feel late.
                emit(now + i * 6, angle, span, colour, kind, intensity);
            }
        }

        private void emit(double birthTime, float angle, float span, Color4 colour, ParticleKind kind, float intensity)
        {
            // Cover the contacted arc rather than clustering in its centre. A very small inset
            // keeps the outermost particle bodies from visually hanging beyond the arc caps.
            float tangentOffset = (random01() - 0.5f) * span * 0.92f;
            float spawnAngle = angle + tangentOffset;
            float spawnRadians = spawnAngle * MathF.PI / 180;
            Vector2 outward = new Vector2(MathF.Cos(spawnRadians), MathF.Sin(spawnRadians));
            Vector2 tangent = new Vector2(-outward.Y, outward.X);
            float cone = kind == ParticleKind.Shard ? 12 : 24;
            float directionOffset = triangularRandom() * cone;
            float directionRadians = directionOffset * MathF.PI / 180;
            Vector2 direction = outward * MathF.Cos(directionRadians) + tangent * MathF.Sin(directionRadians);
            float speed = kind == ParticleKind.Shard
                ? lerp(325, 490, random01())
                : lerp(195, 325, random01());
            float radius = SticksPlayfield.GUIDE_RADIUS + lerp(-0.5f, 2, random01());

            ref Particle particle = ref particles[nextParticle];
            double lifetime = (kind == ParticleKind.Shard
                ? lerp(125, 195, random01())
                : lerp(170, 280, random01())) / Math.Max(0.8f, intensity);
            particle = new Particle
            {
                Active = true,
                Kind = kind,
                BirthTime = birthTime,
                Lifetime = lifetime,
                Origin = SticksPlayfield.PointAt(spawnAngle, radius),
                Outward = outward,
                Tangent = tangent,
                InitialOutwardVelocity = Math.Max(20, Vector2.Dot(direction * speed, outward)) * intensity,
                InitialTangentVelocity = Vector2.Dot(direction * speed, tangent) * intensity,
                Drag = kind == ParticleKind.Shard ? 3.2f : 2.35f,
                OutwardAcceleration = kind == ParticleKind.Shard ? 45 : 55,
                StartSize = kind == ParticleKind.Shard
                    ? new Vector2(lerp(3, 5, random01()), lerp(7, 12, random01()))
                    : new Vector2(lerp(3.5f, 6.5f, random01())),
                Rotation = MathF.Atan2(direction.Y, direction.X) + MathF.PI / 2,
                Spin = triangularRandom() * (kind == ParticleKind.Shard ? 1.8f : 4.5f),
                PeakAlpha = (kind == ParticleKind.Shard
                    ? lerp(0.65f, 0.9f, random01())
                    : lerp(0.5f, 0.75f, random01())) * Math.Min(1.15f, intensity),
                Shape = random01(),
                Colour = colour,
            };

            latestParticleEndTime = Math.Max(latestParticleEndTime, birthTime + lifetime);

            nextParticle = (nextParticle + 1) % particles.Length;
        }

        private float random01()
        {
            uint value = hash(emitterSeed + ++sequence * 0x9e3779b9u);
            return (value & 0x00ffffffu) / 16777216f;
        }

        private float triangularRandom() => random01() - random01();

        private static uint hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }

        private static float lerp(float from, float to, float amount) => from + (to - from) * amount;

        protected override DrawNode CreateDrawNode() => new ParticleDrawNode(this);

        private enum ParticleKind : byte
        {
            Mote,
            Shard,
        }

        private struct Particle
        {
            public bool Active;
            public ParticleKind Kind;
            public double BirthTime;
            public double Lifetime;
            public Vector2 Origin;
            public Vector2 Outward;
            public Vector2 Tangent;
            public float InitialOutwardVelocity;
            public float InitialTangentVelocity;
            public float Drag;
            public float OutwardAcceleration;
            public Vector2 StartSize;
            public float Rotation;
            public float Spin;
            public float PeakAlpha;
            public float Shape;
            public Color4 Colour;
        }

        private sealed class ParticleDrawNode : DrawNode
        {
            private readonly Particle[] particles = new Particle[max_particles];
            private double currentTime;
            private IShader shader = null!;
            private Texture texture = null!;

            private new SticksContactParticleEmitter Source => (SticksContactParticleEmitter)base.Source;

            public ParticleDrawNode(SticksContactParticleEmitter source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();
                Array.Copy(Source.particles, particles, particles.Length);
                currentTime = Source.currentTime;
                shader = Source.shader;
                texture = Source.texture;
            }

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                if (shader == null || texture == null)
                    return;

                shader.Bind();

                foreach (Particle particle in particles)
                {
                    if (!particle.Active)
                        continue;

                    float ageSeconds = (float)((currentTime - particle.BirthTime) / 1000);
                    float lifetimeSeconds = (float)(particle.Lifetime / 1000);

                    if (ageSeconds < 0 || ageSeconds >= lifetimeSeconds)
                        continue;

                    float progress = ageSeconds / lifetimeSeconds;
                    Vector2 position = particle.Origin
                                       + particle.Outward * displacementWithDrag(
                                           particle.InitialOutwardVelocity,
                                           particle.OutwardAcceleration,
                                           particle.Drag,
                                           ageSeconds)
                                       + particle.Tangent * displacementWithDrag(
                                           particle.InitialTangentVelocity,
                                           0,
                                           particle.Drag,
                                           ageSeconds);

                    float alpha = alphaAt(progress) * particle.PeakAlpha;
                    Vector2 size = sizeAt(particle, progress);
                    float rotation = particle.Rotation + particle.Spin * ageSeconds;
                    Color4 colour = colourAt(progress, particle.Shape, particle.Colour);

                    drawSoftParticle(renderer, texture, position, size, rotation, colour, alpha, particle.Kind);
                }

                shader.Unbind();
            }

            private void drawSoftParticle(
                IRenderer renderer,
                Texture texture,
                Vector2 centre,
                Vector2 size,
                float rotation,
                Color4 colour,
                float alpha,
                ParticleKind kind)
            {
                // Layered rotated diamonds approximate a tiny feathered mote/teardrop while keeping
                // this first pass self-contained. They are soft compact bodies, not trajectory lines.
                Vector2 glowSize = size * (kind == ParticleKind.Shard ? new Vector2(1.8f, 1.45f) : new Vector2(1.8f));
                drawDiamond(renderer, texture, centre, glowSize, rotation, colour, alpha * 0.1f);
                drawDiamond(renderer, texture, centre, size, rotation, colour, alpha * 0.5f);
                drawDiamond(renderer, texture, centre, size * 0.55f, rotation, new Color4(1f, 0.97f, 0.86f, 1), alpha * 0.8f);
            }

            private void drawDiamond(IRenderer renderer, Texture texture, Vector2 centre, Vector2 size, float rotation, Color4 colour, float alpha)
            {
                float sin = MathF.Sin(rotation);
                float cos = MathF.Cos(rotation);
                Vector2 x = new Vector2(cos, sin) * size.X / 2;
                Vector2 y = new Vector2(-sin, cos) * size.Y / 2;
                Quad quad = new Quad(
                    Vector2Extensions.Transform(centre - y, DrawInfo.Matrix),
                    Vector2Extensions.Transform(centre + x, DrawInfo.Matrix),
                    Vector2Extensions.Transform(centre - x, DrawInfo.Matrix),
                    Vector2Extensions.Transform(centre + y, DrawInfo.Matrix));
                renderer.DrawQuad(texture, quad, ColourInfo.SingleColour(colour).MultiplyAlpha(alpha));
            }

            private static float displacementWithDrag(float initialVelocity, float acceleration, float drag, float time)
            {
                float terminalVelocity = acceleration / drag;
                return terminalVelocity * time
                       + (initialVelocity - terminalVelocity) * (1 - MathF.Exp(-drag * time)) / drag;
            }

            private static float alphaAt(float progress)
            {
                if (progress < 0.08f)
                    return smoothStep(progress / 0.08f);

                float fade = 1 - smoothStep(Math.Clamp((progress - 0.28f) / 0.72f, 0, 1));
                return MathF.Pow(fade, 1.4f);
            }

            private static Vector2 sizeAt(Particle particle, float progress)
            {
                float scale;

                if (progress < 0.12f)
                    scale = lerp(0.65f, 1, smoothStep(progress / 0.12f));
                else if (progress < 0.4f)
                    scale = 1;
                else
                    scale = lerp(1, particle.Kind == ParticleKind.Shard ? 0.42f : 0.25f, smoothStep((progress - 0.4f) / 0.6f));

                if (particle.Kind == ParticleKind.Shard)
                    return new Vector2(particle.StartSize.X * scale, particle.StartSize.Y * lerp(1, 0.55f, progress));

                float irregularity = 0.9f + particle.Shape * 0.2f;
                return new Vector2(particle.StartSize.X * scale * irregularity, particle.StartSize.Y * scale / irregularity);
            }

            private static Color4 colourAt(float progress, float variation, Color4 laneColour)
            {
                Color4 start = blend(laneColour, Color4.White, 0.42f);
                Color4 middle = blend(laneColour, Color4.White, 0.12f + variation * 0.08f);
                Color4 end = new Color4(laneColour.R, laneColour.G, laneColour.B, 1);
                return progress < 0.55f
                    ? blend(start, middle, progress / 0.55f)
                    : blend(middle, end, (progress - 0.55f) / 0.45f);
            }

            private static Color4 blend(Color4 from, Color4 to, float amount) => new Color4(
                lerp(from.R, to.R, amount),
                lerp(from.G, to.G, amount),
                lerp(from.B, to.B, amount),
                lerp(from.A, to.A, amount));

            private static float smoothStep(float value) => value * value * (3 - 2 * value);
        }
    }
}
