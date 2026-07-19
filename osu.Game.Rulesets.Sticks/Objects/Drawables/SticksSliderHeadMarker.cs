// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Utils;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    /// <summary>
    /// A compact slider head with a centered direction glyph framed by its angular hit width.
    /// It intentionally omits the center tick used by flick markers so the double arrow is
    /// the sole focal point.
    /// </summary>
    public partial class SticksSliderHeadMarker : CompositeDrawable
    {
        private const float stroke_radius = 2.5f;
        private const float cap_half_length = 7;
        internal const float APPROACH_TARGET_DIAMETER = 22;
        internal const float DIRECTION_SYMBOL_DIAMETER = 18;
        internal const float APPROACH_CIRCLE_INITIAL_SCALE = 4;
        private StickSide side;
        private int direction;
        private readonly SmoothPath widthArc;
        private readonly CircularProgress animatedWidthArc;
        private readonly SmoothPath leadingCap;
        private readonly SmoothPath trailingCap;
        private readonly CircularContainer approachCircle;
        private readonly Circle colourPlate;
        private readonly SpriteIcon directionArrow;
        private readonly bool reversalStyle;
        private SticksNotePresentation presentation;
        private bool approachCircleEnabled;
        private float approachProgress;
        private float approachAlpha = 0.9f;
        private float targetCircleScale = SticksPlayfield.DEFAULT_NOTE_CIRCLE_SCALE;
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

        public int Direction => direction;

        public StickSide Side => side;

        public float TargetCircleScale
        {
            get => targetCircleScale;
            set
            {
                value = Math.Clamp(value, SticksPlayfield.MIN_NOTE_CIRCLE_SCALE, SticksPlayfield.MAX_NOTE_CIRCLE_SCALE);
                if (Math.Abs(targetCircleScale - value) < 0.001f)
                    return;

                targetCircleScale = value;
                updateTargetSizes();
            }
        }

        public SticksNotePresentation Presentation
        {
            get => presentation;
            set
            {
                if (presentation == value)
                    return;

                presentation = value;
                updateCapVisibility();
            }
        }

        public bool ApproachCircleEnabled
        {
            get => approachCircleEnabled;
            set
            {
                if (approachCircleEnabled == value)
                    return;

                approachCircleEnabled = value;
                updateApproachCircleVisibility();
            }
        }

        public float ApproachProgress
        {
            get => approachProgress;
            set
            {
                value = Math.Clamp(value, 0, 1);
                if (Math.Abs(approachProgress - value) < 0.001f)
                    return;

                approachProgress = value;
                updateApproachCircleSize();
            }
        }

        public float ApproachAlpha
        {
            get => approachAlpha;
            set
            {
                value = Math.Clamp(value, 0, 0.9f);
                if (Math.Abs(approachAlpha - value) < 0.001f)
                    return;

                approachAlpha = value;
                updateApproachCircleVisibility();
            }
        }

        public SticksSliderHeadMarker(StickSide side, int direction, Color4 colour, bool animatedSpan = false, bool reversalStyle = false)
        {
            this.side = side;
            this.direction = Math.Sign(direction) == 0 ? 1 : Math.Sign(direction);
            this.reversalStyle = reversalStyle;

            Anchor = Anchor.TopLeft;
            Origin = Anchor.Centre;
            Position = new Vector2(SticksPlayfield.SIZE / 2);
            Size = new Vector2(SticksPlayfield.SIZE);

            Vector2 centrePosition = SticksPlayfield.PointAt(0, SticksPlayfield.RadiusFor(side));

            widthArc = animatedSpan ? null : createArc(colour, 0.72f);
            animatedWidthArc = animatedSpan ? createAnimatedArc(side, colour, 0.72f) : null;

            AddRangeInternal(new Drawable[]
            {
                animatedSpan ? animatedWidthArc : widthArc,
                leadingCap = createCap(colour),
                trailingCap = createCap(colour),
                approachCircle = createApproachCircle(colour),
                colourPlate = new Circle
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = centrePosition,
                    Size = new Vector2(APPROACH_TARGET_DIAMETER),
                    Colour = colour,
                },
                directionArrow = new SpriteIcon
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = centrePosition,
                    Size = new Vector2(DIRECTION_SYMBOL_DIAMETER),
                    Icon = FontAwesome.Solid.AngleDoubleRight,
                    Colour = Color4.White,
                    Rotation = this.direction * 90,
                    Shadow = true,
                },
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

        public void SetLaneAndDirection(StickSide newSide, int newDirection, Color4 colour)
        {
            side = newSide;
            direction = Math.Sign(newDirection) == 0 ? 1 : Math.Sign(newDirection);

            if (widthArc != null)
                widthArc.Colour = colour;
            if (animatedWidthArc != null)
                animatedWidthArc.Colour = colour;

            leadingCap.Colour = colour;
            trailingCap.Colour = colour;
            approachCircle.BorderColour = colour;
            colourPlate.Colour = colour;
            directionArrow.Rotation = direction * 90;
            updateGeometry();
        }

        private void updateGeometry()
        {
            float radius = SticksPlayfield.RadiusFor(side) + radialOffset;
            float outsideAngle = -direction * span / 2;
            float arcStart = reversalStyle ? Math.Min(0, outsideAngle) : -span / 2;
            float arcEnd = reversalStyle ? Math.Max(0, outsideAngle) : span / 2;

            if (animatedWidthArc != null)
            {
                float outerRadius = radius + stroke_radius;
                animatedWidthArc.Size = new Vector2(outerRadius * 2);
                animatedWidthArc.InnerRadius = 2 * stroke_radius / outerRadius;
                animatedWidthArc.Progress = (arcEnd - arcStart) / 360;
                animatedWidthArc.Rotation = 90 + arcStart;
            }
            else
            {
                widthArc.Vertices = arcVertices(radius, arcStart, arcEnd);
            }

            if (reversalStyle)
            {
                positionCap(leadingCap, radius, outsideAngle);
            }
            else
            {
                positionCap(leadingCap, radius, -span / 2);
                positionCap(trailingCap, radius, span / 2);
            }

            updateCapVisibility();

            Vector2 centrePosition = SticksPlayfield.PointAt(0, radius);
            approachCircle.Position = centrePosition;
            colourPlate.Position = centrePosition;
            directionArrow.Position = centrePosition;
        }

        private void updateApproachCircleVisibility() =>
            approachCircle.Alpha = approachCircleEnabled ? approachAlpha : 0;

        private void updateTargetSizes()
        {
            colourPlate.Size = new Vector2(APPROACH_TARGET_DIAMETER * targetCircleScale);
            directionArrow.Size = new Vector2(DIRECTION_SYMBOL_DIAMETER * targetCircleScale);
            updateApproachCircleSize();
        }

        private void updateApproachCircleSize()
        {
            float scale = (float)Interpolation.Lerp(APPROACH_CIRCLE_INITIAL_SCALE, 1, approachProgress);
            approachCircle.Size = new Vector2(APPROACH_TARGET_DIAMETER * targetCircleScale * scale);
        }

        private void updateCapVisibility()
        {
            bool showCaps = presentation != SticksNotePresentation.ApproachCircles;
            leadingCap.Alpha = showCaps ? 1 : 0;
            trailingCap.Alpha = showCaps && !reversalStyle ? 1 : 0;
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

        private static IReadOnlyList<Vector2> arcVertices(float radius, float startAngle, float endAngle)
        {
            const int segments = 24;
            var vertices = new List<Vector2>(segments + 1);

            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + (endAngle - startAngle) * i / segments;
                vertices.Add(SticksPlayfield.PointAt(angle, radius));
            }

            return vertices;
        }

        private static SmoothPath createCap(Color4 colour) => new SmoothPath
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.None,
            Size = new Vector2(cap_half_length * 2 + stroke_radius * 2, stroke_radius * 2),
            PathRadius = stroke_radius,
            Colour = colour,
            Vertices = new[]
            {
                new Vector2(stroke_radius, stroke_radius),
                new Vector2(stroke_radius + cap_half_length * 2, stroke_radius),
            },
        };

        private static CircularContainer createApproachCircle(Color4 colour) => new CircularContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Size = new Vector2(APPROACH_TARGET_DIAMETER * APPROACH_CIRCLE_INITIAL_SCALE),
            Masking = true,
            BorderThickness = 2.5f,
            BorderColour = colour,
            Alpha = 0,
            Depth = 1,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
                AlwaysPresent = true,
            },
        };

        private static void positionCap(SmoothPath cap, float radius, float angle)
        {
            cap.Position = SticksPlayfield.PointAt(angle, radius);
            cap.Rotation = angle;
        }
    }
}
