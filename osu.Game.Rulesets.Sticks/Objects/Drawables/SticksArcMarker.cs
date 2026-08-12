// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Utils;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    public partial class SticksArcMarker : CompositeDrawable
    {
        private const float stroke_radius = 2.5f;
        private const float cap_half_length = 7;
        internal const float HIT_CIRCLE_DIAMETER = 22;
        internal const float APPROACH_CIRCLE_INITIAL_SCALE = 4;
        internal const float BOX_OUTLINE_HALF_THICKNESS = 7;
        internal const float BOX_FILL_HALF_THICKNESS = 4.5f;
        private static readonly Color4 box_empty_colour = new Color4(0.035f, 0.035f, 0.045f, 1);
        private StickSide side;
        private readonly SmoothPath arc;
        private readonly CircularProgress animatedArc;
        private readonly CircularProgress boxInteriorArc;
        private readonly CircularProgress boxFillArc;
        private readonly SmoothPath leadingCap;
        private readonly SmoothPath trailingCap;
        private readonly SmoothPath centerTick;
        private readonly Container hitCircle;
        private readonly Circle hitCircleBody;
        private readonly CircularContainer hitCircleRing;
        private readonly CircularContainer approachCircle;
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
                hitCircle.Size = new Vector2(HIT_CIRCLE_DIAMETER * targetCircleScale);
                updateApproachCircle();
            }
        }

        /// <summary>
        /// Controls the independent timing ring. The target presentation and approach cue are
        /// kept separate so active slider tracking markers can use a circular target without
        /// displaying another approach animation.
        /// </summary>
        public bool ApproachCircleEnabled
        {
            get => approachCircleEnabled;
            set
            {
                if (approachCircleEnabled == value)
                    return;

                approachCircleEnabled = value;
                updatePresentation();
            }
        }

        /// <summary>
        /// Linear progress through the approach window. This drives the selected presentation's timing cue:
        /// either the contracting approach circle or the center-out arc-container fill.
        /// </summary>
        public float ApproachProgress
        {
            get => approachProgress;
            set
            {
                value = Math.Clamp(value, 0, 1);
                if (Math.Abs(approachProgress - value) < 0.001f)
                    return;

                approachProgress = value;
                updateApproachCircle();
                updateBoxArcs();
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
                updatePresentation();
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
                updatePresentation();
                updateGeometry();
            }
        }

        public SticksArcMarker(StickSide side, Color4 colour, bool animatedSpan = false)
        {
            this.side = side;

            Anchor = Anchor.TopLeft;
            Origin = Anchor.Centre;
            Position = new Vector2(SticksPlayfield.SIZE / 2);
            Size = new Vector2(SticksPlayfield.SIZE);

            arc = animatedSpan ? null : createArc(colour, 0.72f);
            animatedArc = animatedSpan ? createAnimatedArc(side, colour, 0.72f) : null;
            boxInteriorArc = animatedSpan ? createAnimatedArc(side, box_empty_colour, 0, BOX_FILL_HALF_THICKNESS) : null;
            boxFillArc = animatedSpan ? createAnimatedArc(side, colour, 0, BOX_FILL_HALF_THICKNESS) : null;

            AddInternal(animatedSpan ? (Drawable)animatedArc : arc);
            if (boxInteriorArc != null)
            {
                AddInternal(boxInteriorArc);
                AddInternal(boxFillArc);
            }

            AddRangeInternal(new Drawable[]
            {
                leadingCap = createCap(colour),
                trailingCap = createCap(colour),
                centerTick = createCap(Color4.White, cap_half_length * 0.65f),
                approachCircle = createApproachCircle(colour),
                hitCircle = createHitCircle(colour, out hitCircleBody, out hitCircleRing),
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
            if (boxFillArc != null)
                boxFillArc.Colour = colour;

            leadingCap.Colour = colour;
            trailingCap.Colour = colour;
            approachCircle.BorderColour = colour;
            hitCircleBody.Colour = colour;
            updateGeometry();
        }

        private void updateGeometry()
        {
            float radius = SticksPlayfield.RadiusFor(side) + radialOffset;
            float outerHalfThickness = presentation == SticksNotePresentation.FillingArcs
                ? BOX_OUTLINE_HALF_THICKNESS
                : stroke_radius;

            if (animatedArc != null)
            {
                setCircularArcRange(animatedArc, radius, outerHalfThickness, -span / 2, span);
            }
            else
            {
                arc.PathRadius = outerHalfThickness;
                arc.Vertices = arcVertices(radius, span);
            }
            updateBoxArcs(radius);
            positionCap(leadingCap, radius, -span / 2);
            positionCap(trailingCap, radius, span / 2);
            positionCap(centerTick, radius, 0);
            hitCircle.Position = SticksPlayfield.PointAt(0, radius);
            approachCircle.Position = hitCircle.Position;
        }

        private void updatePresentation()
        {
            bool showApproachTarget = presentation == SticksNotePresentation.ApproachCircles;
            bool showBox = presentation == SticksNotePresentation.FillingArcs;
            bool showBrackets = !showApproachTarget && !showBox;
            leadingCap.Alpha = showBrackets ? 1 : 0;
            trailingCap.Alpha = showBrackets ? 1 : 0;
            centerTick.Alpha = showBrackets || showBox ? 1 : 0;
            hitCircle.Alpha = showApproachTarget ? 1 : 0;
            approachCircle.Alpha = showApproachTarget && approachCircleEnabled ? approachAlpha : 0;

            if (arc != null)
                arc.Alpha = showBox ? 1 : 0.72f;
            if (animatedArc != null)
                animatedArc.Alpha = showBox ? 1 : 0.72f;
            if (boxInteriorArc != null)
            {
                boxInteriorArc.Alpha = showBox ? 1 : 0;
                boxFillArc.Alpha = showBox ? 1 : 0;
            }
        }

        private void updateApproachCircle()
        {
            float scale = (float)Interpolation.Lerp(APPROACH_CIRCLE_INITIAL_SCALE, 1, approachProgress);
            approachCircle.Size = new Vector2(HIT_CIRCLE_DIAMETER * targetCircleScale * scale);
        }

        private void updateBoxArcs() => updateBoxArcs(SticksPlayfield.RadiusFor(side) + radialOffset);

        private void updateBoxArcs(float radius)
        {
            if (boxInteriorArc == null)
                return;

            setCircularArcRange(boxInteriorArc, radius, BOX_FILL_HALF_THICKNESS, -span / 2, span);
            float progress = FillProgressFor(approachProgress);
            float filledSpan = span * progress;
            setCircularArcRange(boxFillArc, radius, BOX_FILL_HALF_THICKNESS, -filledSpan / 2, filledSpan);
        }

        internal static float FillProgressFor(float linearProgress) =>
            (float)SticksHitObject.ApproachGrowthProgress(linearProgress);

        internal static float SpanForApproach(float finalSpan, SticksNotePresentation presentation, double growth) =>
            finalSpan * (presentation is SticksNotePresentation.ApproachCircles or SticksNotePresentation.FillingArcs
                ? 1
                : (float)(0.2 + growth * 0.8));

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

        private static void setCircularArcRange(CircularProgress drawable, float radius, float halfThickness, float startAngle, float length)
        {
            float outerRadius = radius + halfThickness;
            drawable.Size = new Vector2(outerRadius * 2);
            drawable.InnerRadius = 2 * halfThickness / outerRadius;
            drawable.Rotation = 90 + startAngle;
            drawable.Progress = length / 360;
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

        private static Container createHitCircle(Color4 colour, out Circle body, out CircularContainer ring)
        {
            body = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Colour = colour,
            };

            ring = new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                BorderThickness = 2.5f,
                BorderColour = Color4.White,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0,
                    AlwaysPresent = true,
                },
            };

            return new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Size = new Vector2(HIT_CIRCLE_DIAMETER),
                Alpha = 0,
                Depth = -2,
                Children = new Drawable[]
                {
                    body,
                    ring,
                },
            };
        }

        private static CircularContainer createApproachCircle(Color4 colour) => new CircularContainer
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Size = new Vector2(HIT_CIRCLE_DIAMETER * APPROACH_CIRCLE_INITIAL_SCALE),
            Masking = true,
            BorderThickness = 2.5f,
            BorderColour = colour,
            Alpha = 0,
            Depth = -1,
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
