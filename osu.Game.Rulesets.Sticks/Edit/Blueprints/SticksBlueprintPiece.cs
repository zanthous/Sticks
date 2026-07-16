// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    public partial class SticksBlueprintPiece : CompositeDrawable
    {
        private readonly SmoothPath headArc;
        private readonly SmoothPath centreTick;
        private readonly SmoothPath body;
        private readonly CircularContainer tail;
        private readonly Box tailFill;
        private readonly Circle selectionMarker;
        private readonly OsuSpriteText detailText;
        private bool hasDisplayedGeometry;
        private StickSide displayedSide;
        private float displayedAngle;
        private int displayedKind;
        private int displayedSegmentSignature;
        private double displayedDuration;

        public Drawable Marker => selectionMarker;

        public SticksBlueprintPiece()
        {
            Size = new Vector2(SticksPlayfield.SIZE);

            InternalChildren = new Drawable[]
            {
                body = path(5),
                headArc = path(4),
                centreTick = path(2, Color4.White),
                tail = new CircularContainer
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Size = new Vector2(12),
                    Masking = true,
                    BorderThickness = 2,
                    BorderColour = Color4.White,
                    Child = tailFill = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                },
                detailText = new OsuSpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Font = OsuFont.Default.With(size: 13, weight: FontWeight.Bold),
                },
                selectionMarker = new Circle
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Size = new Vector2(34),
                    Alpha = 0,
                    AlwaysPresent = true,
                },
            };
        }

        public void UpdateFrom(SticksHitObject hitObject)
        {
            int kind = hitObject is SticksSlider ? 2 : hitObject is SticksHold ? 1 : 0;
            int segmentSignature = hitObject is SticksSlider sliderValue ? segmentHash(sliderValue) : 0;
            bool durationAffectsGeometry = hitObject is SticksHold;
            double duration = hitObject switch
            {
                SticksSlider sliderDuration => sliderDuration.Duration,
                SticksHold holdValue => holdValue.Duration,
                _ => 0,
            };

            if (hasDisplayedGeometry
                && displayedSide == hitObject.Side
                && Math.Abs(displayedAngle - hitObject.Angle) < 0.001f
                && displayedKind == kind
                && displayedSegmentSignature == segmentSignature
                && (!durationAffectsGeometry || Math.Abs(displayedDuration - duration) < 0.001))
                return;

            hasDisplayedGeometry = true;
            displayedSide = hitObject.Side;
            displayedAngle = hitObject.Angle;
            displayedKind = kind;
            displayedSegmentSignature = segmentSignature;
            displayedDuration = duration;

            float radius = SticksPlayfield.RadiusFor(hitObject.Side);
            Color4 colour = colourFor(hitObject.Side);
            Vector2 head = SticksPlayfield.PointAt(hitObject.Angle, radius);

            headArc.Colour = colour;
            headArc.Vertices = arcVertices(radius, hitObject.Angle - SticksHitObject.VISIBLE_ARC_SPAN / 2, SticksHitObject.VISIBLE_ARC_SPAN);
            centreTick.Vertices = radialTick(hitObject.Angle, radius, 8);
            selectionMarker.Position = head;
            tailFill.Colour = colour;
            detailText.Colour = colour;

            switch (hitObject)
            {
                case SticksSlider slider:
                    body.Show();
                    tail.Show();
                    detailText.Show();
                    body.Colour = colour;
                    body.Alpha = 0.5f;
                    body.Vertices = sliderVertices(radius, slider);
                    tail.Position = SticksPlayfield.PointAt(slider.SegmentStartAngleAt(slider.SegmentCount), radius);
                    detailText.Position = labelPosition(slider.Side, slider.Angle);
                    detailText.Text = slider.SegmentCount > 1
                        ? $"{slider.SegmentCount} segments  {slider.TotalAngularDistance:0.#}°"
                        : $"{slider.ArcAngle:+0.#;-0.#;0}°";
                    break;

                case SticksHold hold:
                    body.Show();
                    tail.Show();
                    detailText.Show();
                    body.Colour = colour;
                    body.Alpha = 0.5f;
                    float railLength = (float)Math.Clamp(hold.Duration * 0.06, 40, 130);
                    float farRadius = radius + (hold.Side == StickSide.Left ? railLength : -railLength);
                    Vector2 railEnd = SticksPlayfield.PointAt(hold.Angle, farRadius);
                    body.Vertices = new[] { head, railEnd };
                    tail.Position = railEnd;
                    detailText.Position = labelPosition(hold.Side, hold.Angle);
                    detailText.Text = $"{hold.Duration:0} ms";
                    break;

                default:
                    body.Hide();
                    tail.Hide();
                    detailText.Hide();
                    break;
            }
        }

        public bool ReceiveAt(Vector2 screenSpacePosition)
        {
            Vector2 local = ToLocalSpace(screenSpacePosition);
            return (local - selectionMarker.Position).Length <= 24;
        }

        private static SmoothPath path(float radius, Color4? colour = null) => new SmoothPath
        {
            AutoSizeAxes = Axes.None,
            Size = new Vector2(SticksPlayfield.SIZE),
            PathRadius = radius,
            Colour = colour ?? Color4.White,
        };

        private static IReadOnlyList<Vector2> arcVertices(float radius, float startAngle, float arcAngle)
        {
            int segments = Math.Clamp((int)Math.Ceiling(Math.Abs(arcAngle) / 8), 2, 512);
            var vertices = new List<Vector2>(segments + 1);

            for (int i = 0; i <= segments; i++)
                vertices.Add(SticksPlayfield.PointAt(startAngle + arcAngle * i / segments, radius));

            return vertices;
        }

        private static IReadOnlyList<Vector2> sliderVertices(float radius, SticksSlider slider)
        {
            var result = new List<Vector2>();
            for (int segment = 0; segment < slider.SegmentCount; segment++)
            {
                IReadOnlyList<Vector2> vertices = arcVertices(radius, slider.SegmentStartAngleAt(segment), slider.SegmentArcAngleAt(segment));
                for (int i = segment == 0 ? 0 : 1; i < vertices.Count; i++)
                    result.Add(vertices[i]);
            }

            return result;
        }

        private static int segmentHash(SticksSlider slider)
        {
            var hash = new HashCode();
            hash.Add(slider.SegmentCount);
            for (int i = 0; i < slider.SegmentCount; i++)
                hash.Add(slider.SegmentArcAngleAt(i));
            return hash.ToHashCode();
        }

        private static IReadOnlyList<Vector2> radialTick(float angle, float radius, float halfLength) => new[]
        {
            SticksPlayfield.PointAt(angle, radius - halfLength),
            SticksPlayfield.PointAt(angle, radius + halfLength),
        };

        private static Vector2 labelPosition(StickSide side, float angle) =>
            SticksPlayfield.PointAt(angle, SticksPlayfield.RadiusFor(side) + (side == StickSide.Left ? 32 : -32));

        private static Color4 colourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;
    }
}
