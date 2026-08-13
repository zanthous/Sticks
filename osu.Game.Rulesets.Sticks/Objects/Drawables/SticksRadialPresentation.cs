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

        public SticksRadialTimelinePath(Color4 colour)
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            AddRangeInternal(new Drawable[]
            {
                fill = new RibbonFill
                {
                    Size = new Vector2(SticksPlayfield.SIZE),
                    Depth = 2,
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
            fill.Colour = colour.Opacity(0.82f);
            Color4 railColour = colour.Lighten(0.35f);
            leftRail.Colour = railColour;
            rightRail.Colour = railColour;
            leadingRail.Colour = railColour;
        }

        private static SmoothPath createRail() => new SmoothPath
        {
            AutoSizeAxes = Axes.None,
            Size = new Vector2(SticksPlayfield.SIZE),
            PathRadius = rail_radius,
            Depth = 0,
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
}
