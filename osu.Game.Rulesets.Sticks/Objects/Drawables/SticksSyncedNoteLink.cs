// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Game.Rulesets.Sticks.Configuration;
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
        public const float SHORT_LENGTH_FRACTION = 0.25f;

        private const float dash_length = 12;
        private const float dash_gap = 4;

        public bool UsesAlternatingDashes { get; private set; }

        internal int DrawableSegmentCount => InternalChildren.Count;

        private SticksChordLinkPresentation presentation = SticksChordLinkPresentation.FullToCentre;

        public SticksChordLinkPresentation Presentation
        {
            get => presentation;
            set
            {
                if (presentation == value)
                    return;

                presentation = value;
                rebuildGeometry();
            }
        }

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

            displayedFirstSide = firstSide;
            displayedFirstAngle = firstAngle;
            displayedSecondSide = secondSide;
            displayedSecondAngle = secondAngle;
            rebuildGeometry();
        }

        private void rebuildGeometry()
        {
            ClearInternal();

            if (!displayedFirstSide.HasValue || !displayedSecondSide.HasValue)
                return;

            StickSide firstSide = displayedFirstSide.Value;
            StickSide secondSide = displayedSecondSide.Value;
            float firstAngle = displayedFirstAngle;
            float secondAngle = displayedSecondAngle;

            UsesAlternatingDashes = IsSharedAngle(firstAngle, secondAngle);

            if (Presentation == SticksChordLinkPresentation.Hidden)
                return;

            Vector2 first = SticksPlayfield.PointAt(firstAngle, SticksPlayfield.RadiusFor(firstSide));
            Vector2 second = SticksPlayfield.PointAt(secondAngle, SticksPlayfield.RadiusFor(secondSide));
            Vector2 centre = new Vector2(SticksPlayfield.SIZE / 2);

            if (UsesAlternatingDashes)
            {
                Vector2 outerEndpoint = SticksPlayfield.PointAt(
                    firstAngle,
                    Math.Max(SticksPlayfield.RadiusFor(firstSide), SticksPlayfield.RadiusFor(secondSide)));
                Vector2 innerEndpoint = EndpointTowardCentre(outerEndpoint, centre, Presentation);
                addAlternatingDashes(outerEndpoint, innerEndpoint);
                return;
            }

            AddInternal(path(first, EndpointTowardCentre(first, centre, Presentation), ColourFor(firstSide)));
            AddInternal(path(second, EndpointTowardCentre(second, centre, Presentation), ColourFor(secondSide)));
        }

        internal static Vector2 EndpointTowardCentre(Vector2 start, Vector2 centre, SticksChordLinkPresentation presentation) =>
            presentation == SticksChordLinkPresentation.Short
                ? Vector2.Lerp(start, centre, SHORT_LENGTH_FRACTION)
                : centre;

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

        /// <summary>
        /// Duration objects remain alive after their head, so their chord cue needs its own short
        /// departure instead of lingering across the full slider or hold.
        /// </summary>
        public static float AlphaAtHeadCue(double time, double headTime, double growth)
        {
            if (time < headTime)
                return AlphaAtGrowth(growth);

            return FINAL_ALPHA * (float)Math.Clamp(1 - (time - headTime) / 120, 0, 1);
        }

        public static Color4 ColourFor(StickSide side) => side == StickSide.Left
            ? SticksPlayfield.LEFT_COLOUR
            : SticksPlayfield.RIGHT_COLOUR;
    }
}
