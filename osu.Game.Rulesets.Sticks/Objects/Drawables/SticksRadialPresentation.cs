// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Utils;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    /// <summary>
    /// A smooth, time-sampled ribbon used by center-out sliders and holds. Sampling by timestamp
    /// keeps this renderer compatible with future freeform timed slider paths.
    /// </summary>
    public partial class SticksRadialTimelinePath : CompositeDrawable
    {
        private const int max_points = 96;
        private const double target_sample_interval = 18;
        private const float minimum_half_span = 0.35f;
        private const float rail_radius = 1.35f;

        private readonly RibbonPoint[] points = new RibbonPoint[max_points];
        private readonly RibbonFill fill;
        private readonly SmoothPath leftRail;
        private readonly SmoothPath rightRail;
        private readonly SmoothPath leadingRail;
        private int pointCount;
        private StickSide displayedSide;

        public SticksRadialTimelinePath(Color4 colour)
        {
            displayedSide = colour.B > colour.R ? StickSide.Left : StickSide.Right;
            Size = new Vector2(SticksPlayfield.SIZE);
            AddRangeInternal(new Drawable[]
            {
                fill = new RibbonFill
                {
                    Size = new Vector2(SticksPlayfield.SIZE),
                    Depth = 2,
                    // The containing hit-object buffer isolates this from the beatmap background.
                    // Component-wise max keeps either lane unchanged alone, while red/blue
                    // intersections become purple regardless of draw order.
                    Blending = new BlendingParameters
                    {
                        Source = BlendingType.SrcAlpha,
                        Destination = BlendingType.One,
                        SourceAlpha = BlendingType.One,
                        DestinationAlpha = BlendingType.One,
                        RGBEquation = BlendingEquation.Max,
                        AlphaEquation = BlendingEquation.Max,
                    },
                },
                leftRail = createRail(),
                rightRail = createRail(),
                leadingRail = createRail(),
            });

            applyColour(colour);
        }

        public void SetSliderGeometry(SticksSlider slider, double now)
        {
            setGeometry(slider.StartTime, slider.EndTime, slider.ApproachDuration, slider.PrimaryHitAngle, now, slider, slider.Angle);
            applyColour(colourFor(slider.Side));
        }

        /// <summary>
        /// Gives an actively tracked center-out slider a restrained beat-synchronised fill highlight without
        /// adding another path or scheduling transforms every frame.
        /// </summary>
        public void SetTrackingState(bool tracking, float beatPulse)
        {
            if (!tracking)
            {
                fill.Colour = fillMask(0.82f);
                return;
            }

            // Supplied from the active beatmap timing section, keeping BPM changes and seeks aligned.
            float pulse = Math.Clamp(beatPulse, 0, 1);
            fill.Colour = fillMask(0.82f + 0.08f * pulse);
        }

        public void SetHoldGeometry(SticksHold hold, double now)
        {
            setGeometry(hold.StartTime, hold.EndTime, hold.ApproachDuration, hold.PrimaryHitAngle, now, null, hold.Angle);
            applyColour(colourFor(hold.Side));
        }

        private void setGeometry(
            double objectStartTime,
            double objectEndTime,
            double approachDuration,
            float angularSpan,
            double now,
            SticksSlider slider,
            float constantAngle)
        {
            double visibleStart = Math.Max(now, objectStartTime);
            double visibleEnd = Math.Min(objectEndTime, now + Math.Max(1, approachDuration));

            if (visibleEnd - visibleStart <= 0.001)
            {
                pointCount = 0;
                updateGeometry();
                return;
            }

            pointCount = Math.Clamp((int)Math.Ceiling((visibleEnd - visibleStart) / target_sample_interval) + 1, 2, max_points);
            float halfAngularSpan = MathF.Max(minimum_half_span, angularSpan / 2);

            for (int i = 0; i < pointCount; i++)
            {
                double time = Interpolation.Lerp(visibleStart, visibleEnd, i / (double)(pointCount - 1));
                float radialProgress = SticksPlayfield.CenterOutProgressAt(now, time, approachDuration);
                float radius = SticksPlayfield.GUIDE_RADIUS * radialProgress;
                float angle = slider?.AngleAt(time) ?? constantAngle;

                points[i] = new RibbonPoint(radius, angle, halfAngularSpan);
            }

            updateGeometry();
        }

        private void updateGeometry()
        {
            fill.SetGeometry(points, pointCount);
            leftRail.ClearVertices();
            rightRail.ClearVertices();
            leadingRail.ClearVertices();

            for (int i = 0; i < pointCount; i++)
            {
                RibbonPoint point = points[i];
                leftRail.AddVertex(SticksPlayfield.PointAt(point.Angle - point.HalfSpan, point.Radius));
                rightRail.AddVertex(SticksPlayfield.PointAt(point.Angle + point.HalfSpan, point.Radius));
            }

            if (pointCount == 0)
                return;

            const int cap_segments = 12;
            RibbonPoint leading = points[0];

            for (int i = 0; i <= cap_segments; i++)
            {
                float angle = leading.Angle - leading.HalfSpan + 2 * leading.HalfSpan * i / cap_segments;
                leadingRail.AddVertex(SticksPlayfield.PointAt(angle, leading.Radius));
            }
        }

        private void applyColour(Color4 colour)
        {
            displayedSide = colour.B > colour.R ? StickSide.Left : StickSide.Right;
            fill.Colour = fillMask(0.82f);
            leftRail.Colour = railMask;
            rightRail.Colour = railMask;
            leadingRail.Colour = railMask;
            leftRail.PathRadius = rightRail.PathRadius = leadingRail.PathRadius = rail_radius;
        }

        // The isolated framebuffer stores geometry masks, not display colours. This gives the
        // compositor an exact, order-independent distinction between either lane and its rails.
        private Color4 fillMask(float alpha) => displayedSide == StickSide.Left
            ? new Color4(0, 1, 0, alpha)
            : new Color4(1, 0, 0, alpha);

        private Color4 railMask => displayedSide == StickSide.Left
            ? new Color4(0, 1, 1, 1)
            : new Color4(1, 0, 1, 1);

        private static SmoothPath createRail() => new SmoothPath
        {
            AutoSizeAxes = Axes.None,
            Size = new Vector2(SticksPlayfield.SIZE),
            PathRadius = rail_radius,
            Depth = 0,
            Blending = overlapBlending,
        };

        private static BlendingParameters overlapBlending => new BlendingParameters
        {
            Source = BlendingType.SrcAlpha,
            Destination = BlendingType.One,
            SourceAlpha = BlendingType.One,
            DestinationAlpha = BlendingType.One,
            RGBEquation = BlendingEquation.Max,
            AlphaEquation = BlendingEquation.Max,
        };

        private readonly record struct RibbonPoint(float Radius, float Angle, float HalfSpan);

        private partial class RibbonFill : Drawable
        {
            private readonly RibbonPoint[] points = new RibbonPoint[max_points];
            private int pointCount;
            private IShader shader = null!;
            private Texture texture = null!;

            [BackgroundDependencyLoader]
            private void load(IRenderer renderer, ShaderManager shaders)
            {
                texture = renderer.WhitePixel;
                shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
            }

            public void SetGeometry(RibbonPoint[] source, int count)
            {
                pointCount = count;
                Array.Copy(source, points, count);
                Invalidate(Invalidation.DrawNode);
            }

            protected override DrawNode CreateDrawNode() => new RibbonFillDrawNode(this);

            private sealed class RibbonFillDrawNode : DrawNode
            {
                private readonly RibbonPoint[] points = new RibbonPoint[max_points];
                private readonly Vector2[] leftEdges = new Vector2[max_points];
                private readonly Vector2[] rightEdges = new Vector2[max_points];
                private int pointCount;
                private IShader shader = null!;
                private Texture texture = null!;

                private new RibbonFill Source => (RibbonFill)base.Source;

                public RibbonFillDrawNode(RibbonFill source)
                    : base(source)
                {
                }

                public override void ApplyState()
                {
                    base.ApplyState();

                    shader = Source.shader;
                    texture = Source.texture;
                    pointCount = Source.pointCount;
                    Array.Copy(Source.points, points, pointCount);

                    for (int i = 0; i < pointCount; i++)
                    {
                        leftEdges[i] = SticksPlayfield.PointAt(points[i].Angle - points[i].HalfSpan, points[i].Radius);
                        rightEdges[i] = SticksPlayfield.PointAt(points[i].Angle + points[i].HalfSpan, points[i].Radius);
                    }
                }

                protected override void Draw(IRenderer renderer)
                {
                    base.Draw(renderer);

                    if (pointCount < 2 || shader == null || texture == null)
                        return;

                    shader.Bind();

                    for (int i = 0; i < pointCount - 1; i++)
                    {
                        renderer.DrawQuad(
                            texture,
                            new Quad(
                                Vector2Extensions.Transform(leftEdges[i], DrawInfo.Matrix),
                                Vector2Extensions.Transform(leftEdges[i + 1], DrawInfo.Matrix),
                                Vector2Extensions.Transform(rightEdges[i], DrawInfo.Matrix),
                                Vector2Extensions.Transform(rightEdges[i + 1], DrawInfo.Matrix)),
                            DrawColourInfo.Colour);
                    }

                    drawCurvedLeadingCap(renderer);
                    shader.Unbind();
                }

                private void drawCurvedLeadingCap(IRenderer renderer)
                {
                    const int cap_segments = 12;
                    RibbonPoint leading = points[0];
                    float halfSpanRadians = leading.HalfSpan * MathF.PI / 180;
                    float capDepth = Math.Max(2, leading.Radius * (1 - MathF.Cos(halfSpanRadians)) + 1);
                    float innerRadius = Math.Max(0, leading.Radius - capDepth);

                    for (int i = 0; i < cap_segments; i++)
                    {
                        float firstAngle = leading.Angle - leading.HalfSpan + 2 * leading.HalfSpan * i / cap_segments;
                        float secondAngle = leading.Angle - leading.HalfSpan + 2 * leading.HalfSpan * (i + 1) / cap_segments;

                        renderer.DrawQuad(
                            texture,
                            new Quad(
                                Vector2Extensions.Transform(SticksPlayfield.PointAt(firstAngle, leading.Radius), DrawInfo.Matrix),
                                Vector2Extensions.Transform(SticksPlayfield.PointAt(secondAngle, leading.Radius), DrawInfo.Matrix),
                                Vector2Extensions.Transform(SticksPlayfield.PointAt(firstAngle, innerRadius), DrawInfo.Matrix),
                                Vector2Extensions.Transform(SticksPlayfield.PointAt(secondAngle, innerRadius), DrawInfo.Matrix)),
                            DrawColourInfo.Colour);
                    }
                }

            }
        }

        private static Color4 colourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;
    }

    /// <summary>
    /// A compact contact flare which follows a tracked center-out slider along the judgment line.
    /// It uses layered arcs rather than a blurred framebuffer, keeping the effect inexpensive and
    /// shaped to the circular playfield rather than looking like a rectangular particle emitter.
    /// </summary>
    public partial class SticksSliderContactEffect : CompositeDrawable
    {
        private readonly CircularProgress halo;
        private readonly CircularProgress glow;
        private readonly CircularProgress core;
        private readonly SticksContactParticleEmitter particles;
        private bool targetActive;
        private float visualStrength;

        public SticksSliderContactEffect(Color4 colour, double sparkPhaseOffset)
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            AlwaysPresent = true;
            Blending = BlendingParameters.Additive;

            AddRangeInternal(new Drawable[]
            {
                halo = createArc(6, 3),
                glow = createArc(3, 2),
                core = createArc(0.85f, 1),
                particles = new SticksContactParticleEmitter(sparkPhaseOffset)
                {
                    Depth = 0,
                },
            });

            applyColour(colour);
        }

        public void SetState(bool isActive, double now, float angle, float sliderSpan, Color4 colour)
        {
            targetActive = isActive;

            float span = ContactSpanFor(sliderSpan);
            setArcRange(halo, angle, span, 5.5f, 1);
            setArcRange(glow, angle, span * 0.72f, 2.75f, 0.5f);
            setArcRange(core, angle, span * 0.52f, 1, 0);
            applyColour(colour);
            particles.SetContinuousState(isActive, now, angle, span, colour);
        }

        internal static float ContactSpanFor(float hitSpan) => Math.Clamp(hitSpan, 1, 360);

        protected override void Update()
        {
            base.Update();

            visualStrength = (float)Interpolation.DampContinuously(
                visualStrength,
                targetActive ? 1 : 0,
                targetActive ? 42 : 28,
                Math.Abs(Time.Elapsed));

            if (visualStrength < 0.001f)
                visualStrength = 0;
            else if (visualStrength > 0.999f)
                visualStrength = 1;

            Alpha = visualStrength;
        }

        private void applyColour(Color4 colour)
        {
            halo.Colour = colour.Opacity(0.1f);
            glow.Colour = blend(colour, Color4.White, 0.28f).Opacity(0.24f);
            core.Colour = blend(colour, Color4.White, 0.65f).Opacity(0.82f);
        }

        private static CircularProgress createArc(float halfThickness, float depth)
        {
            float outerRadius = SticksPlayfield.GUIDE_RADIUS + halfThickness;
            return new CircularProgress
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = new Vector2(SticksPlayfield.SIZE / 2),
                Size = new Vector2(outerRadius * 2),
                InnerRadius = 2 * halfThickness / outerRadius,
                RoundedCaps = true,
                Depth = depth,
            };
        }

        private static void setArcRange(CircularProgress arc, float angle, float span, float halfThickness, float radiusOffset)
        {
            float radius = SticksPlayfield.GUIDE_RADIUS + radiusOffset;
            float outerRadius = radius + halfThickness;
            arc.Size = new Vector2(outerRadius * 2);
            arc.InnerRadius = 2 * halfThickness / outerRadius;
            arc.Rotation = 90 + angle - span / 2;
            arc.Progress = span / 360;
        }

        private static Color4 blend(Color4 from, Color4 to, float amount) => new Color4(
            from.R + (to.R - from.R) * amount,
            from.G + (to.G - from.G) * amount,
            from.B + (to.B - from.B) * amount,
            from.A + (to.A - from.A) * amount);
    }

    /// <summary>
    /// A bounded pool of short center-out contact bursts. The pool permits simultaneous chords
    /// without creating drawables during gameplay or retaining pooled hit object drawables.
    /// </summary>
    public partial class SticksContactBurstLayer : CompositeDrawable
    {
        private const int pool_size = 16;

        private readonly SticksContactBurstEffect[] effects = new SticksContactBurstEffect[pool_size];
        private int nextEffect;

        public SticksContactBurstLayer()
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            AlwaysPresent = true;

            for (int i = 0; i < effects.Length; i++)
                AddInternal(effects[i] = new SticksContactBurstEffect(i));
        }

        public void Trigger(float angle, float hitSpan, Color4 colour, bool completion)
        {
            effects[nextEffect].Trigger(Time.Current, angle, hitSpan, colour, completion);
            nextEffect = (nextEffect + 1) % effects.Length;
        }

        public void ClearBursts()
        {
            foreach (SticksContactBurstEffect effect in effects)
                effect.Clear();
        }
    }

    /// <summary>
    /// A compact one-shot flare at the judgement ring for successful notes and slider ends.
    /// </summary>
    public partial class SticksContactBurstEffect : CompositeDrawable
    {
        private const double hit_duration = 170;
        private const double completion_duration = 220;

        private readonly CircularProgress halo;
        private readonly CircularProgress glow;
        private readonly CircularProgress core;
        private readonly SticksContactParticleEmitter particles;
        private double startTime;
        private double duration;
        private bool active;

        public SticksContactBurstEffect(int seed)
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            AlwaysPresent = true;
            Alpha = 0;
            Blending = BlendingParameters.Additive;

            AddRangeInternal(new Drawable[]
            {
                halo = createArc(4.5f, 3),
                glow = createArc(2.2f, 2),
                core = createArc(0.75f, 1),
                particles = new SticksContactParticleEmitter(seed),
            });
        }

        public void Trigger(double now, float angle, float hitSpan, Color4 colour, bool completion)
        {
            active = true;
            startTime = now;
            duration = completion ? completion_duration : hit_duration;

            float span = SticksSliderContactEffect.ContactSpanFor(hitSpan);
            setArcRange(halo, angle, span, 4.5f, 2);
            setArcRange(glow, angle, span * 0.7f, 2.2f, 1);
            setArcRange(core, angle, span * 0.48f, 0.75f, 0);

            Color4 warmGold = new Color4(1f, 0.84f, 0.48f, 1f);
            Color4 warmWhite = new Color4(1f, 0.97f, 0.82f, 1f);
            halo.Colour = blend(warmGold, colour, 0.15f).Opacity(completion ? 0.11f : 0.1f);
            glow.Colour = warmGold.Opacity(completion ? 0.3f : 0.27f);
            core.Colour = warmWhite.Opacity(completion ? 0.9f : 0.85f);
            particles.TriggerBurst(now, angle, span, colour, completion ? 18 : 12, completion ? 1.12f : 1);
            Alpha = 1;
        }

        public void Clear()
        {
            active = false;
            Alpha = 0;
            particles.Clear();
        }

        protected override void Update()
        {
            base.Update();

            if (!active)
                return;

            float progress = (float)Math.Clamp((Time.Current - startTime) / duration, 0, 1);

            if (progress >= 1)
            {
                Clear();
                return;
            }

            // Start crisp, then disappear quickly without scheduling transforms per hit.
            float remaining = 1 - progress;
            Alpha = remaining * remaining;
        }

        private static CircularProgress createArc(float halfThickness, float depth)
        {
            float outerRadius = SticksPlayfield.GUIDE_RADIUS + halfThickness;
            return new CircularProgress
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = new Vector2(SticksPlayfield.SIZE / 2),
                Size = new Vector2(outerRadius * 2),
                InnerRadius = 2 * halfThickness / outerRadius,
                RoundedCaps = true,
                Depth = depth,
            };
        }

        private static void setArcRange(CircularProgress arc, float angle, float span, float halfThickness, float radiusOffset)
        {
            float radius = SticksPlayfield.GUIDE_RADIUS + radiusOffset;
            float outerRadius = radius + halfThickness;
            arc.Size = new Vector2(outerRadius * 2);
            arc.InnerRadius = 2 * halfThickness / outerRadius;
            arc.Rotation = 90 + angle - span / 2;
            arc.Progress = span / 360;
        }

        private static Color4 blend(Color4 from, Color4 to, float amount) => new Color4(
            from.R + (to.R - from.R) * amount,
            from.G + (to.G - from.G) * amount,
            from.B + (to.B - from.B) * amount,
            from.A + (to.A - from.A) * amount);
    }

}
