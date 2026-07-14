// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    /// <summary>
    /// A single quiet visual grouping cue for a simultaneous two-stick chord.
    /// Geometry is rebuilt only when editor mutations change one of its endpoints.
    /// </summary>
    public partial class SticksSyncedNoteLink : CompositeDrawable
    {
        public const float INITIAL_ALPHA = 0.45f;
        public const float FINAL_ALPHA = 0.8f;
        public const float SHARED_ANGLE_TOLERANCE = 0.5f;

        private const float dash_length = 12;
        private const float dash_gap = 4;

        public bool UsesAlternatingDashes { get; private set; }

        private StickSide? displayedFirstSide;
        private float displayedFirstAngle = float.NaN;
        private StickSide? displayedSecondSide;
        private float displayedSecondAngle = float.NaN;

        public SticksSyncedNoteLink(
            StickSide firstSide,
            float firstAngle,
            StickSide secondSide,
            float secondAngle)
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            Alpha = 0;
            Depth = 20;

            SetGeometry(firstSide, firstAngle, secondSide, secondAngle);
        }

        public void SetGeometry(
            StickSide firstSide,
            float firstAngle,
            StickSide secondSide,
            float secondAngle)
        {
            if (displayedFirstSide == firstSide
                && Math.Abs(displayedFirstAngle - firstAngle) < 0.001f
                && displayedSecondSide == secondSide
                && Math.Abs(displayedSecondAngle - secondAngle) < 0.001f)
                return;

            ClearInternal();
            displayedFirstSide = firstSide;
            displayedFirstAngle = firstAngle;
            displayedSecondSide = secondSide;
            displayedSecondAngle = secondAngle;
            UsesAlternatingDashes = IsSharedAngle(firstAngle, secondAngle);

            Vector2 first = SticksPlayfield.PointAt(firstAngle, SticksPlayfield.RadiusFor(firstSide));
            Vector2 second = SticksPlayfield.PointAt(secondAngle, SticksPlayfield.RadiusFor(secondSide));
            Vector2 centre = new Vector2(SticksPlayfield.SIZE / 2);

            if (UsesAlternatingDashes)
            {
                Vector2 outerEndpoint = SticksPlayfield.PointAt(
                    firstAngle,
                    Math.Max(SticksPlayfield.RadiusFor(firstSide), SticksPlayfield.RadiusFor(secondSide)));
                addAlternatingDashes(centre, outerEndpoint);
                return;
            }

            AddInternal(path(first, centre, ColourFor(firstSide)));
            AddInternal(path(second, centre, ColourFor(secondSide)));
        }

        private void addAlternatingDashes(Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            float length = delta.Length;
            Vector2 direction = delta / Math.Max(1, length);
            int dashIndex = 0;

            for (float offset = 0; offset < length; offset += dash_length + dash_gap)
            {
                float dashEnd = Math.Min(length, offset + dash_length);
                Color4 colour = ColourFor(dashIndex++ % 2 == 0 ? StickSide.Left : StickSide.Right);
                AddInternal(path(start + direction * offset, start + direction * dashEnd, colour));
            }
        }

        public static bool IsSharedAngle(float firstAngle, float secondAngle) =>
            Math.Abs(SticksHitObject.DeltaAngle(firstAngle, secondAngle)) <= SHARED_ANGLE_TOLERANCE;

        private static SmoothPath path(Vector2 start, Vector2 end, Color4? colour = null) => new SmoothPath
        {
            AutoSizeAxes = Axes.None,
            Size = new Vector2(SticksPlayfield.SIZE),
            PathRadius = 1.6f,
            Colour = colour ?? Color4.White,
            Vertices = new[] { start, end },
        };

        public static float AlphaAtGrowth(double growth) =>
            (float)(INITIAL_ALPHA + Math.Clamp(growth, 0, 1) * (FINAL_ALPHA - INITIAL_ALPHA));

        public static Color4 ColourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;
    }
}
