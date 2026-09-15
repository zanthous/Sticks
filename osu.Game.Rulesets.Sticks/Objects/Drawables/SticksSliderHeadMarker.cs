using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Utils;
using osu.Game.Rulesets.Sticks.Skinning;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    /// <summary>
    /// The angular hit window and optional skinned centre of a centre-out slider head.
    /// </summary>
    public partial class SticksSliderHeadMarker : CompositeDrawable
    {
        private const float stroke_radius = 2.5f;
        private const float cap_half_length = 7;
        private StickSide side;
        private int direction;
        private readonly SmoothPath widthArc;
        private readonly CircularProgress animatedWidthArc;
        private readonly SmoothPath leadingCap;
        private readonly SmoothPath trailingCap;
        private readonly SmoothPath centerTick;
        private readonly SticksSkinnedSprite skinCentre;
        private readonly bool reversalStyle;
        private float span;
        private float radialOffset;
        private float targetRadialOffset;
        private bool radialOffsetInitialised;
        private bool skinCentreOnly;

        internal bool HasSkinCentre => skinCentre.UsesSkinTexture;

        internal bool SkinCentreOnly
        {
            get => skinCentreOnly;
            set
            {
                if (skinCentreOnly == value)
                    return;

                skinCentreOnly = value;
                updateCapVisibility();
            }
        }

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

        public SticksSliderHeadMarker(StickSide side, int direction, Color4 colour, bool animatedSpan = false, bool reversalStyle = false)
        {
            this.side = side;
            this.direction = Math.Sign(direction);
            this.reversalStyle = reversalStyle;

            Anchor = Anchor.TopLeft;
            Origin = Anchor.Centre;
            Position = new Vector2(SticksPlayfield.SIZE / 2);
            Size = new Vector2(SticksPlayfield.SIZE);

            widthArc = animatedSpan ? null : createArc(colour, 1);
            animatedWidthArc = animatedSpan ? createAnimatedArc(side, colour, 1) : null;

            AddInternal(animatedSpan ? (Drawable)animatedWidthArc : widthArc);
            AddRangeInternal(new Drawable[]
            {
                leadingCap = createCap(colour),
                trailingCap = createCap(colour),
                centerTick = createCap(Color4.White, cap_half_length * 0.65f),
                skinCentre = new SticksSkinnedSprite(centreTextureName)
                {
                    Origin = Anchor.Centre,
                    TextureColour = colour,
                    Rotation = this.direction * 90,
                    Alpha = 0,
                },
            });

            skinCentre.OnSkinChanged += updateCapVisibility;
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
            direction = Math.Sign(newDirection);

            if (widthArc != null)
                widthArc.Colour = colour;
            if (animatedWidthArc != null)
                animatedWidthArc.Colour = colour;

            leadingCap.Colour = colour;
            trailingCap.Colour = colour;
            skinCentre.TextureName = centreTextureName;
            skinCentre.TextureColour = colour;
            skinCentre.Rotation = direction * 90;
            updateGeometry();
        }

        private string centreTextureName => direction == 0 ? "sticks-note-centre"
            : reversalStyle ? "sticks-slider-reversal" : "sticks-slider-head";

        private void updateGeometry()
        {
            float radius = SticksPlayfield.RadiusFor(side) + radialOffset;
            float outsideAngle = -direction * span / 2;
            float arcStart = reversalStyle ? Math.Min(0, outsideAngle) : -span / 2;
            float arcEnd = reversalStyle ? Math.Max(0, outsideAngle) : span / 2;

            if (animatedWidthArc != null)
            {
                setArcRange(animatedWidthArc, radius, stroke_radius, arcStart, arcEnd - arcStart);
            }
            else
            {
                widthArc.PathRadius = stroke_radius;
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

            positionCap(centerTick, radius, 0);
            skinCentre.Position = SticksPlayfield.PointAt(0, radius);
            skinCentre.Size = SticksArcMarker.SkinCentreSizeAt(radius, span);

            updateCapVisibility();
        }

        private void updateCapVisibility()
        {
            leadingCap.Alpha = skinCentreOnly ? 0 : 1;
            trailingCap.Alpha = !skinCentreOnly && !reversalStyle ? 1 : 0;
            bool showSkinCentre = skinCentre.UsesSkinTexture;
            centerTick.Alpha = !showSkinCentre && !skinCentreOnly ? 1 : 0;
            skinCentre.Alpha = showSkinCentre ? 1 : 0;
            if (widthArc != null)
                widthArc.Alpha = skinCentreOnly ? 0 : 1;
            if (animatedWidthArc != null)
                animatedWidthArc.Alpha = skinCentreOnly ? 0 : 1;
        }

        private static void setArcRange(CircularProgress drawable, float radius, float halfThickness, float startAngle, float length)
        {
            float outerRadius = radius + halfThickness;
            drawable.Size = new Vector2(outerRadius * 2);
            drawable.InnerRadius = Math.Min(1, 2 * halfThickness / Math.Max(0.001f, outerRadius));
            drawable.Rotation = 90 + startAngle;
            drawable.Progress = length / 360;
        }

        private static SmoothPath createArc(Color4 colour, float alpha) => new SmoothPath
        {
            AutoSizeAxes = Axes.None,
            Size = new Vector2(SticksPlayfield.SIZE),
            PathRadius = stroke_radius,
            Colour = colour,
            Alpha = alpha,
        };

        private static CircularProgress createAnimatedArc(StickSide side, Color4 colour, float alpha, float halfThickness = stroke_radius)
        {
            float radius = SticksPlayfield.RadiusFor(side);
            float outerRadius = radius + halfThickness;
            return new CircularProgress
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = new Vector2(SticksPlayfield.SIZE / 2),
                Size = new Vector2(outerRadius * 2),
                InnerRadius = 2 * halfThickness / outerRadius,
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

        private static SmoothPath createCap(Color4 colour, float halfLength = cap_half_length) => new SmoothPath
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.None,
            Size = new Vector2(halfLength * 2 + stroke_radius * 2, stroke_radius * 2),
            PathRadius = stroke_radius,
            Colour = colour,
            Vertices = new[]
            {
                new Vector2(stroke_radius, stroke_radius),
                new Vector2(stroke_radius + halfLength * 2, stroke_radius),
            },
        };

        private static void positionCap(SmoothPath cap, float radius, float angle)
        {
            cap.Position = SticksPlayfield.PointAt(angle, radius);
            cap.Rotation = angle;
        }
    }
}
