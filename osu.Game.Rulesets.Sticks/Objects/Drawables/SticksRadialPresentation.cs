using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
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
        private const float edge_glow_width = 24;

        private readonly RibbonPoint[] points = new RibbonPoint[max_points];
        private readonly float[] sampledAngles = new float[max_points];
        private readonly RibbonShape shape;
        private int pointCount;
        private StickSide displayedSide;
        private float displayedFillAlpha = 0.82f;

        public SticksRadialTimelinePath(Color4 colour)
        {
            displayedSide = colour.B > colour.R ? StickSide.Left : StickSide.Right;
            Size = new Vector2(SticksPlayfield.SIZE);
            AddInternal(shape = new RibbonShape
            {
                Size = new Vector2(SticksPlayfield.SIZE),
                Blending = new BlendingParameters
                {
                    Source = BlendingType.SrcAlpha,
                    Destination = BlendingType.One,
                    SourceAlpha = BlendingType.One,
                    DestinationAlpha = BlendingType.One,
                    RGBEquation = BlendingEquation.Max,
                    AlphaEquation = BlendingEquation.Max,
                },
            });

            applyColour(colour);
        }

        public void SetSliderGeometry(SticksSlider slider, double now)
        {
            setGeometry(slider.StartTime, slider.EndTime, slider.ApproachDuration, slider.PrimaryHitAngle, now, slider, slider.Angle);

            if (displayedSide != slider.Side)
                applyColour(colourFor(slider.Side));
        }

        /// <summary>
        /// Gives an actively tracked center-out slider a restrained beat-synchronised fill highlight without
        /// adding another path or scheduling transforms every frame.
        /// </summary>
        public void SetTrackingState(bool tracking, float beatPulse)
        {
            // Supplied from the active beatmap timing section, keeping BPM changes and seeks aligned.
            float desiredAlpha = tracking
                ? 0.82f + 0.08f * Math.Clamp(beatPulse, 0, 1)
                : 0.82f;

            if (Math.Abs(displayedFillAlpha - desiredAlpha) < 0.0001f)
                return;

            displayedFillAlpha = desiredAlpha;
            shape.SetStyle(displayedSide, displayedFillAlpha);
        }

        public void SetHoldGeometry(SticksHold hold, double now)
        {
            setGeometry(hold.StartTime, hold.EndTime, hold.ApproachDuration, hold.PrimaryHitAngle, now, null, hold.Angle);

            if (displayedSide != hold.Side)
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
                if (pointCount == 0)
                    return;

                pointCount = 0;
                updateGeometry();
                return;
            }

            pointCount = Math.Clamp((int)Math.Ceiling((visibleEnd - visibleStart) / target_sample_interval) + 1, 2, max_points);
            float halfAngularSpan = MathF.Max(minimum_half_span, angularSpan / 2);

            if (slider != null)
                slider.FillAngleSamples(visibleStart, visibleEnd, sampledAngles.AsSpan(0, pointCount));
            else
                sampledAngles.AsSpan(0, pointCount).Fill(constantAngle);

            for (int i = 0; i < pointCount; i++)
            {
                double time = Interpolation.Lerp(visibleStart, visibleEnd, i / (double)(pointCount - 1));
                float radialProgress = SticksPlayfield.CenterOutProgressAt(now, time, approachDuration);
                float radius = SticksPlayfield.GUIDE_RADIUS * radialProgress;
                float angle = sampledAngles[i];

                points[i] = new RibbonPoint(
                    radius,
                    angle,
                    halfAngularSpan,
                    SticksPlayfield.PointAt(angle - halfAngularSpan, radius),
                    SticksPlayfield.PointAt(angle + halfAngularSpan, radius));
            }

            updateGeometry();
        }

        private void updateGeometry()
        {
            shape.SetGeometry(points, pointCount);
        }

        private void applyColour(Color4 colour)
        {
            displayedSide = colour.B > colour.R ? StickSide.Left : StickSide.Right;
            displayedFillAlpha = 0.82f;
            shape.SetStyle(displayedSide, displayedFillAlpha);
        }

        private readonly record struct RibbonPoint(
            float Radius,
            float Angle,
            float HalfSpan,
            Vector2 LeftEdge,
            Vector2 RightEdge);

        private partial class RibbonShape : Drawable
        {
            private readonly RibbonPoint[] points = new RibbonPoint[max_points];
            private int pointCount;
            private StickSide side;
            private float fillAlpha;
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

            public void SetStyle(StickSide newSide, float newFillAlpha)
            {
                if (side == newSide && Math.Abs(fillAlpha - newFillAlpha) < 0.0001f)
                    return;

                side = newSide;
                fillAlpha = newFillAlpha;
                Invalidate(Invalidation.DrawNode);
            }

            protected override DrawNode CreateDrawNode() => new RibbonShapeDrawNode(this);

            private sealed class RibbonShapeDrawNode : DrawNode
            {
                private readonly RibbonPoint[] points = new RibbonPoint[max_points];
                private int pointCount;
                private StickSide side;
                private float fillAlpha;
                private IShader shader = null!;
                private Texture texture = null!;

                private new RibbonShape Source => (RibbonShape)base.Source;

                public RibbonShapeDrawNode(RibbonShape source)
                    : base(source)
                {
                }

                public override void ApplyState()
                {
                    base.ApplyState();

                    shader = Source.shader;
                    texture = Source.texture;
                    pointCount = Source.pointCount;
                    side = Source.side;
                    fillAlpha = Source.fillAlpha;
                    Array.Copy(Source.points, points, pointCount);
                }

                protected override void Draw(IRenderer renderer)
                {
                    base.Draw(renderer);

                    if (pointCount < 2 || shader == null || texture == null)
                        return;

                    ColourInfo fillColour = ColourInfo.SingleColour(side == StickSide.Left
                        ? new Color4(0, 1, 0, fillAlpha)
                        : new Color4(1, 0, 0, fillAlpha));
                    ColourInfo railColour = ColourInfo.SingleColour(side == StickSide.Left
                        ? new Color4(0, 1, 1, 1)
                        : new Color4(1, 0, 1, 1));
                    ColourInfo edgeToInterior = ColourInfo.GradientVertical(
                        new Color4(0, 0, 1, 0.82f),
                        new Color4(0, 0, 0, 0.82f));
                    ColourInfo interiorToEdge = ColourInfo.GradientVertical(
                        new Color4(0, 0, 0, 0.82f),
                        new Color4(0, 0, 1, 0.82f));

                    shader.Bind();

                    for (int i = 0; i < pointCount - 1; i++)
                    {
                        renderer.DrawQuad(
                            texture,
                            new Quad(
                                Vector2Extensions.Transform(points[i].LeftEdge, DrawInfo.Matrix),
                                Vector2Extensions.Transform(points[i + 1].LeftEdge, DrawInfo.Matrix),
                                Vector2Extensions.Transform(points[i].RightEdge, DrawInfo.Matrix),
                                Vector2Extensions.Transform(points[i + 1].RightEdge, DrawInfo.Matrix)),
                            fillColour);

                        drawEdgeGlowSegment(renderer, points[i], points[i + 1], true, edgeToInterior);
                        drawEdgeGlowSegment(renderer, points[i], points[i + 1], false, interiorToEdge);
                        drawRailSegment(renderer, points[i].LeftEdge, points[i + 1].LeftEdge, railColour);
                        drawRailSegment(renderer, points[i].RightEdge, points[i + 1].RightEdge, railColour);
                    }

                    drawCurvedLeadingCap(renderer, fillColour, railColour, edgeToInterior);
                    shader.Unbind();
                }

                private void drawCurvedLeadingCap(IRenderer renderer, ColourInfo fillColour, ColourInfo railColour, ColourInfo edgeToInterior)
                {
                    const int cap_segments = 12;
                    RibbonPoint leading = points[0];
                    float halfSpanRadians = leading.HalfSpan * MathF.PI / 180;
                    float capDepth = Math.Max(2, leading.Radius * (1 - MathF.Cos(halfSpanRadians)) + 1);
                    float innerRadius = Math.Max(0, leading.Radius - capDepth);
                    float glowInnerRadius = Math.Max(innerRadius, leading.Radius - edge_glow_width);
                    Vector2 previousOuter = SticksPlayfield.PointAt(leading.Angle - leading.HalfSpan, leading.Radius);

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
                            fillColour);

                        renderer.DrawQuad(
                            texture,
                            new Quad(
                                Vector2Extensions.Transform(SticksPlayfield.PointAt(firstAngle, leading.Radius), DrawInfo.Matrix),
                                Vector2Extensions.Transform(SticksPlayfield.PointAt(secondAngle, leading.Radius), DrawInfo.Matrix),
                                Vector2Extensions.Transform(SticksPlayfield.PointAt(firstAngle, glowInnerRadius), DrawInfo.Matrix),
                                Vector2Extensions.Transform(SticksPlayfield.PointAt(secondAngle, glowInnerRadius), DrawInfo.Matrix)),
                            edgeToInterior);

                        Vector2 nextOuter = SticksPlayfield.PointAt(secondAngle, leading.Radius);
                        drawRailSegment(renderer, previousOuter, nextOuter, railColour);
                        previousOuter = nextOuter;
                    }
                }

                private void drawEdgeGlowSegment(
                    IRenderer renderer,
                    RibbonPoint from,
                    RibbonPoint to,
                    bool leftEdge,
                    ColourInfo colour)
                {
                    Vector2 fromAcross = from.RightEdge - from.LeftEdge;
                    Vector2 toAcross = to.RightEdge - to.LeftEdge;
                    float fromWidth = fromAcross.Length;
                    float toWidth = toAcross.Length;

                    if (fromWidth <= 0.001f || toWidth <= 0.001f)
                        return;

                    Vector2 fromDirection = fromAcross / fromWidth;
                    Vector2 toDirection = toAcross / toWidth;
                    float fromDepth = Math.Min(edge_glow_width, fromWidth / 2);
                    float toDepth = Math.Min(edge_glow_width, toWidth / 2);

                    Vector2 fromEdge = leftEdge ? from.LeftEdge : from.RightEdge;
                    Vector2 toEdge = leftEdge ? to.LeftEdge : to.RightEdge;
                    Vector2 fromInner = leftEdge
                        ? fromEdge + fromDirection * fromDepth
                        : fromEdge - fromDirection * fromDepth;
                    Vector2 toInner = leftEdge
                        ? toEdge + toDirection * toDepth
                        : toEdge - toDirection * toDepth;

                    renderer.DrawQuad(
                        texture,
                        leftEdge
                            ? new Quad(
                                Vector2Extensions.Transform(fromEdge, DrawInfo.Matrix),
                                Vector2Extensions.Transform(toEdge, DrawInfo.Matrix),
                                Vector2Extensions.Transform(fromInner, DrawInfo.Matrix),
                                Vector2Extensions.Transform(toInner, DrawInfo.Matrix))
                            : new Quad(
                                Vector2Extensions.Transform(fromInner, DrawInfo.Matrix),
                                Vector2Extensions.Transform(toInner, DrawInfo.Matrix),
                                Vector2Extensions.Transform(fromEdge, DrawInfo.Matrix),
                                Vector2Extensions.Transform(toEdge, DrawInfo.Matrix)),
                        colour);
                }

                private void drawRailSegment(IRenderer renderer, Vector2 from, Vector2 to, ColourInfo colour)
                {
                    Vector2 difference = to - from;
                    float length = difference.Length;

                    if (length <= 0.001f)
                        return;

                    Vector2 direction = difference / length;
                    Vector2 normal = new Vector2(-direction.Y, direction.X) * rail_radius;
                    Vector2 extension = direction * rail_radius;
                    Vector2 start = from - extension;
                    Vector2 end = to + extension;

                    renderer.DrawQuad(
                        texture,
                        new Quad(
                            Vector2Extensions.Transform(start - normal, DrawInfo.Matrix),
                            Vector2Extensions.Transform(end - normal, DrawInfo.Matrix),
                            Vector2Extensions.Transform(start + normal, DrawInfo.Matrix),
                            Vector2Extensions.Transform(end + normal, DrawInfo.Matrix)),
                        colour);
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
        private float displayedAngle = float.NaN;
        private float displayedSpan = float.NaN;
        private Color4 displayedColour;

        public SticksSliderContactEffect(Color4 colour, double sparkPhaseOffset)
        {
            Size = new Vector2(SticksPlayfield.SIZE);
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
            bool wasActive = targetActive;
            targetActive = isActive;

            // An invisible contact effect should not keep its arcs and particle emitter in the
            // update traversal for every approaching duration object. Give a newly-active effect
            // a sub-pixel wake-up alpha so its normal damped fade-in starts on this frame.
            if (isActive && !wasActive && visualStrength == 0)
            {
                visualStrength = 0.001f;
                Alpha = visualStrength;
            }

            if (!isActive)
            {
                if (wasActive)
                    particles.SetContinuousState(false, now, angle, sliderSpan, colour);

                return;
            }

            float span = ContactSpanFor(sliderSpan);

            if (!float.IsFinite(displayedAngle)
                || !float.IsFinite(displayedSpan)
                || Math.Abs(displayedAngle - angle) >= 0.001f
                || Math.Abs(displayedSpan - span) >= 0.001f)
            {
                displayedAngle = angle;
                displayedSpan = span;
                setArcRange(halo, angle, span, 5.5f, 1);
                setArcRange(glow, angle, span * 0.72f, 2.75f, 0.5f);
                setArcRange(core, angle, span * 0.52f, 1, 0);
            }

            if (displayedColour != colour)
            {
                displayedColour = colour;
                applyColour(colour);
            }

            particles.SetContinuousState(isActive, now, angle, span, colour);
        }

        internal static float ContactSpanFor(float hitSpan) => Math.Clamp(hitSpan, 1, 360);

        protected override void Update()
        {
            base.Update();

            if (!targetActive && visualStrength == 0)
                return;

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
