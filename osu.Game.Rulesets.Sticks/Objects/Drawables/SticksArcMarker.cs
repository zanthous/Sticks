// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Utils;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class SticksArcMarker : CompositeDrawable
    {
        private const float stroke_radius = 2.5f;
        private const float cap_half_length = 7;
        private StickSide side;
        private readonly SmoothPath arc;
        private readonly CircularProgress animatedArc;
        private readonly SmoothPath leadingCap;
        private readonly SmoothPath trailingCap;
        private readonly SmoothPath centreTick;
        private float span;
        private float radialOffset;
        private float targetRadialOffset;
        private bool radialOffsetInitialised;

        public float RadialOffset
        {
            get => targetRadialOffset;
            set => SetRadialOffset(value);
        }

        internal float DisplayedRadialOffset => radialOffset;

        internal void SetRadialOffset(float value, bool immediate = false)
        {
            targetRadialOffset = value;

            if (!radialOffsetInitialised || immediate)
            {
                radialOffsetInitialised = true;

                if (Math.Abs(radialOffset - value) < 0.001f)
                    return;

                radialOffset = value;
                updateGeometry();
            }
        }

        public float Angle
        {
            get => Rotation;
            set => Rotation = value;
        }

        public float Span
        {
            get => span;
            set
            {
                value = Math.Max(1, value);
                if (Math.Abs(span - value) < 0.001f)
                    return;

                span = value;
                updateGeometry();
            }
        }

        public StickSide Side => side;

        public SticksArcMarker(StickSide side, Color4 colour, bool animatedSpan = false)
        {
            this.side = side;

            Anchor = Anchor.TopLeft;
            Origin = Anchor.Centre;
            Position = new Vector2(SticksPlayfield.SIZE / 2);
            Size = new Vector2(SticksPlayfield.SIZE);

            arc = animatedSpan ? null : createArc(colour, 0.72f);
            animatedArc = animatedSpan ? createAnimatedArc(side, colour, 0.72f) : null;

            AddRangeInternal(new[]
            {
                animatedSpan ? (Drawable)animatedArc : arc,
                leadingCap = createCap(colour),
                trailingCap = createCap(colour),
                centreTick = createCap(Color4.White, cap_half_length * 0.65f),
            });

            Span = SticksHitObject.VISIBLE_ARC_SPAN;
        }

        protected override void Update()
        {
            base.Update();

            float nextOffset = (float)Interpolation.DampContinuously(radialOffset, targetRadialOffset, 45, Math.Abs(Time.Elapsed));
            if (Math.Abs(nextOffset - radialOffset) < 0.001f)
                return;

            radialOffset = nextOffset;
            updateGeometry();
        }

        public void SetLane(StickSide newSide, Color4 colour)
        {
            side = newSide;

            if (arc != null)
                arc.Colour = colour;
            if (animatedArc != null)
                animatedArc.Colour = colour;

            leadingCap.Colour = colour;
            trailingCap.Colour = colour;
            updateGeometry();
        }

        private void updateGeometry()
        {
            float radius = SticksPlayfield.RadiusFor(side) + radialOffset;
            if (animatedArc != null)
            {
                float outerRadius = radius + stroke_radius;
                animatedArc.Size = new Vector2(outerRadius * 2);
                animatedArc.InnerRadius = 2 * stroke_radius / outerRadius;
                animatedArc.Progress = span / 360;
                animatedArc.Rotation = 90 - span / 2;
            }
            else
            {
                arc.Vertices = arcVertices(radius, span);
            }
            positionCap(leadingCap, radius, -span / 2);
            positionCap(trailingCap, radius, span / 2);
            positionCap(centreTick, radius, 0);
        }

        private static SmoothPath createArc(Color4 colour, float alpha) => new SmoothPath
        {
            AutoSizeAxes = Axes.None,
            Size = new Vector2(SticksPlayfield.SIZE),
            PathRadius = stroke_radius,
            Colour = colour,
            Alpha = alpha,
        };

        private static CircularProgress createAnimatedArc(StickSide side, Color4 colour, float alpha)
        {
            float radius = SticksPlayfield.RadiusFor(side);
            float outerRadius = radius + stroke_radius;
            return new CircularProgress
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = new Vector2(SticksPlayfield.SIZE / 2),
                Size = new Vector2(outerRadius * 2),
                InnerRadius = 2 * stroke_radius / outerRadius,
                RoundedCaps = true,
                Colour = colour,
                Alpha = alpha,
            };
        }

        private static IReadOnlyList<Vector2> arcVertices(float radius, float span)
        {
            const int segments = 24;
            var vertices = new List<Vector2>(segments + 1);

            for (int i = 0; i <= segments; i++)
            {
                float angle = -span / 2 + span * i / segments;
                vertices.Add(SticksPlayfield.PointAt(angle, radius));
            }

            return vertices;
        }

        private static SmoothPath createCap(Color4 colour, float halfLength = cap_half_length, float pathRadius = stroke_radius) => new SmoothPath
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.None,
            Size = new Vector2(halfLength * 2 + pathRadius * 2, pathRadius * 2),
            PathRadius = pathRadius,
            Colour = colour,
            Vertices = new[]
            {
                new Vector2(pathRadius, pathRadius),
                new Vector2(pathRadius + halfLength * 2, pathRadius),
            },
        };

        private static void positionCap(SmoothPath cap, float radius, float angle)
        {
            cap.Position = SticksPlayfield.PointAt(angle, radius);
            cap.Rotation = angle;
        }
    }
}
