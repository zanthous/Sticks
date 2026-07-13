// Copyright (c) Zanthous. Licensed under the MIT Licence.

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

        public ChordLinkStyle Style { get; private set; }

        private StickSide? displayedFirstSide;
        private float displayedFirstAngle = float.NaN;
        private StickSide? displayedSecondSide;
        private float displayedSecondAngle = float.NaN;

        public SticksSyncedNoteLink(
            StickSide firstSide,
            float firstAngle,
            StickSide secondSide,
            float secondAngle,
            ChordLinkStyle style = ChordLinkStyle.ToCentre)
        {
            Size = new Vector2(SticksPlayfield.SIZE);
            Alpha = 0;
            Depth = 20;

            SetGeometry(firstSide, firstAngle, secondSide, secondAngle, style);
        }

        public void SetGeometry(
            StickSide firstSide,
            float firstAngle,
            StickSide secondSide,
            float secondAngle,
            ChordLinkStyle style)
        {
            if (displayedFirstSide == firstSide
                && Math.Abs(displayedFirstAngle - firstAngle) < 0.001f
                && displayedSecondSide == secondSide
                && Math.Abs(displayedSecondAngle - secondAngle) < 0.001f
                && Style == style)
                return;

            ClearInternal();
            Style = style;
            displayedFirstSide = firstSide;
            displayedFirstAngle = firstAngle;
            displayedSecondSide = secondSide;
            displayedSecondAngle = secondAngle;

            Vector2 first = SticksPlayfield.PointAt(firstAngle, SticksPlayfield.RadiusFor(firstSide));
            Vector2 second = SticksPlayfield.PointAt(secondAngle, SticksPlayfield.RadiusFor(secondSide));

            if (style == ChordLinkStyle.ToCentre)
            {
                Vector2 centre = new Vector2(SticksPlayfield.SIZE / 2);
                AddInternal(path(first, centre, ColourFor(firstSide)));
                AddInternal(path(second, centre, ColourFor(secondSide)));
                return;
            }

            Vector2 delta = second - first;
            float length = delta.Length;

            if (length < 100)
            {
                AddInternal(path(first, second));
                return;
            }

            // Long opposite-side links leave the centre quiet instead of bisecting it with a solid bar.
            Vector2 direction = delta / Math.Max(1, length);
            Vector2 midpoint = (first + second) / 2;
            const float centre_gap = 14;
            AddInternal(path(first, midpoint - direction * centre_gap));
            AddInternal(path(midpoint + direction * centre_gap, second));
        }

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
