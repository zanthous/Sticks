using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Objects.Drawables
{
    /// <summary>
    /// Draws the geometric intersection of simultaneous opposite-stick Center Out note heads.
    /// Flicks, slider heads, and hold heads all begin with the same gesture and therefore share
    /// the same overlap treatment.
    /// This stays separate from the ribbon compositor so duration paths can never alter a note's
    /// base colour, and uses a bounded set of reusable drawables for dense converted maps.
    /// </summary>
    public partial class SticksCenterOutNoteOverlapLayer : CompositeDrawable
    {
        private const int max_visible_heads = 512;
        private const int max_overlaps = 64;
        private const float marker_half_thickness = 2.5f;
        private const float tick_half_length = 7 * 0.65f;

        private readonly SticksPlayfield playfield;
        private Color4 overlapColour = SticksPlayfield.OVERLAP_COLOUR;
        private Texture collarTexture;
        private readonly SticksHitObject[] visibleHeads = new SticksHitObject[max_visible_heads];
        private readonly OverlapVisual[] overlaps = new OverlapVisual[max_overlaps];

        public SticksCenterOutNoteOverlapLayer(SticksPlayfield playfield)
        {
            this.playfield = playfield;
            RelativeSizeAxes = Axes.Both;
            AlwaysPresent = true;

            for (int i = 0; i < overlaps.Length; i++)
                AddInternal(overlaps[i] = new OverlapVisual());
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer, ShaderManager shaders)
        {
            // A single 144x160 RGBA texture is shared by all pooled collars. It is
            // rebuilt only for a palette change, never for note movement or hits.
            collarTexture = renderer.CreateTexture(SticksDoubleNoteTexture.Width, SticksDoubleNoteTexture.Height);
            uploadCollar();
            IShader shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
            foreach (OverlapVisual overlap in overlaps)
                overlap.SetCollarTexture(collarTexture, shader);
        }

        public void SetColour(Color4 colour)
        {
            if (overlapColour == colour)
                return;

            overlapColour = colour;
            foreach (OverlapVisual overlap in overlaps)
                overlap.SetColour(colour);

            if (collarTexture != null)
                uploadCollar();
        }

        private void uploadCollar() =>
            collarTexture.SetData(new TextureUpload(SticksDoubleNoteTexture.CreateImage(overlapColour)));

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            if (isDisposing)
                collarTexture?.Dispose();
        }

        internal static SticksHitObject UnjudgedHeadOf(DrawableHitObject drawable) => drawable switch
        {
            DrawableSticksFlick flick when !flick.Judged => flick.HitObject,
            DrawableSticksSlider slider when !slider.HeadJudged => slider.HitObject,
            DrawableSticksHold hold when !hold.HeadJudged => hold.HitObject,
            _ => null,
        };

        internal static SticksHitObject VisibleHeadOf(DrawableHitObject drawable, double time, bool pausedEditorPreview)
        {
            if (pausedEditorPreview)
            {
                // A preview result exactly on this timestamp need not be reverted by lazer's
                // rewind logic. The paused editor still needs the complete authored head here.
                SticksHitObject head = drawable switch
                {
                    DrawableSticksFlick flick => flick.HitObject,
                    DrawableSticksSlider slider => slider.HitObject,
                    DrawableSticksHold hold => hold.HitObject,
                    _ => null,
                };

                if (head != null && time <= head.StartTime)
                    return head;
            }

            return UnjudgedHeadOf(drawable);
        }

        protected override void Update()
        {
            base.Update();
            int headCount = 0;
            if (playfield.CenterOutPresentation)
            {
                foreach (DrawableHitObject drawable in playfield.HitObjectContainer.AliveEntries.Values)
                {
                    if (headCount >= visibleHeads.Length)
                        break;

                    SticksHitObject head = VisibleHeadOf(drawable, Time.Current, playfield.IsPausedEditorPreview);
                    if (head != null)
                        visibleHeads[headCount++] = head;
                }
            }

            UpdateOverlaps(visibleHeads.AsSpan(0, headCount), Time.Current, playfield.CenterOutPresentation);
            Array.Clear(visibleHeads, 0, headCount);
        }

        internal void UpdateOverlaps(ReadOnlySpan<SticksHitObject> heads, double time, bool centerOut)
        {
            int overlapCount = 0;
            int headCount = Math.Min(heads.Length, max_visible_heads);
            if (centerOut)
            {
                for (int i = 0; i < headCount && overlapCount < overlaps.Length; i++)
                {
                    SticksHitObject first = heads[i];
                    for (int j = i + 1; j < headCount && overlapCount < overlaps.Length; j++)
                    {
                        SticksHitObject second = heads[j];
                        if (first.Side == second.Side || Math.Abs(first.StartTime - second.StartTime) >= 0.01)
                            continue;

                        if (!TryGetAngularOverlap(first.Angle, first.PrimaryHitAngle, second.Angle, second.PrimaryHitAngle,
                                out float startAngle, out float overlapSpan, out bool firstTickOverlaps, out bool secondTickOverlaps))
                            continue;

                        float radius = SticksPlayfield.GUIDE_RADIUS * SticksPlayfield.CenterOutProgressAt(
                            time, first.StartTime, first.ApproachDuration);
                        bool identicalShape = Math.Abs(SticksHitObject.DeltaAngle(first.Angle, second.Angle)) < 0.01f
                                              && Math.Abs(first.PrimaryHitAngle - second.PrimaryHitAngle) < 0.01f;

                        overlaps[overlapCount++].SetGeometry(radius, startAngle, overlapSpan,
                            first.Angle, second.Angle, firstTickOverlaps, secondTickOverlaps, identicalShape);
                    }
                }
            }

            for (int i = overlapCount; i < overlaps.Length; i++)
                overlaps[i].HideOverlap();
        }

        internal static bool TryGetAngularOverlap(
            float firstAngle,
            float firstSpan,
            float secondAngle,
            float secondSpan,
            out float startAngle,
            out float overlapSpan,
            out bool firstTickOverlaps,
            out bool secondTickOverlaps)
        {
            float firstHalfSpan = Math.Clamp(firstSpan / 2, 0.5f, 180);
            float secondHalfSpan = Math.Clamp(secondSpan / 2, 0.5f, 180);
            float secondCentre = SticksHitObject.DeltaAngle(firstAngle, secondAngle);
            float relativeStart = Math.Max(-firstHalfSpan, secondCentre - secondHalfSpan);
            float relativeEnd = Math.Min(firstHalfSpan, secondCentre + secondHalfSpan);

            overlapSpan = relativeEnd - relativeStart;
            if (overlapSpan <= 0.01f)
            {
                startAngle = 0;
                firstTickOverlaps = false;
                secondTickOverlaps = false;
                return false;
            }

            startAngle = SticksHitObject.NormaliseAngle(firstAngle + relativeStart);
            firstTickOverlaps = relativeStart <= 0 && relativeEnd >= 0;
            secondTickOverlaps = relativeStart <= secondCentre && relativeEnd >= secondCentre;
            return true;
        }

        private partial class OverlapVisual : CompositeDrawable
        {
            private readonly CircularProgress arc;
            private readonly SmoothPath leadingCap;
            private readonly SmoothPath trailingCap;
            private readonly SticksDoubleNoteCollar collar;
            private readonly SmoothPath firstTick;
            private readonly SmoothPath secondTick;

            public OverlapVisual()
            {
                Size = new Vector2(SticksPlayfield.SIZE);
                Alpha = 0;

                AddRangeInternal(new Drawable[]
                {
                    arc = new CircularProgress
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.Centre,
                        Position = new Vector2(SticksPlayfield.SIZE / 2),
                        RoundedCaps = true,
                        Colour = SticksPlayfield.OVERLAP_COLOUR,
                    },
                    leadingCap = createTick(SticksPlayfield.OVERLAP_COLOUR, 7),
                    trailingCap = createTick(SticksPlayfield.OVERLAP_COLOUR, 7),
                    collar = new SticksDoubleNoteCollar(),
                    firstTick = createTick(Color4.White, tick_half_length),
                    secondTick = createTick(Color4.White, tick_half_length),
                });
            }

            public void SetCollarTexture(Texture texture, IShader shader) => collar.SetTexture(texture, shader);

            public void HideOverlap()
            {
                Alpha = 0;
                collar.Alpha = 0;
            }

            public void SetColour(Color4 colour)
            {
                arc.Colour = colour;
                leadingCap.Colour = colour;
                trailingCap.Colour = colour;
            }

            public void SetGeometry(
                float radius,
                float startAngle,
                float span,
                float firstTickAngle,
                float secondTickAngle,
                bool showFirstTick,
                bool showSecondTick,
                bool showCaps)
            {
                float outerRadius = radius + marker_half_thickness;
                arc.Size = new Vector2(Math.Max(0.001f, outerRadius * 2));
                arc.InnerRadius = Math.Min(1, 2 * marker_half_thickness / Math.Max(0.001f, outerRadius));
                arc.Rotation = 90 + startAngle;
                arc.Progress = span / 360;

                positionTick(leadingCap, startAngle, radius, showCaps);
                positionTick(trailingCap, startAngle + span, radius, showCaps);
                collar.Position = SticksPlayfield.PointAt(firstTickAngle, radius);
                collar.Rotation = firstTickAngle;
                // Let the collar grow with a newly emerging head and fit inside narrow
                // angular windows, without rebuilding any of its geometry.
                float availableWidth = 2 * radius * MathF.Sin(Math.Min(180, span) * MathF.PI / 360) - 2 * marker_half_thickness;
                collar.Scale = new Vector2(Math.Clamp(availableWidth / SticksDoubleNoteTexture.DrawHeight, 0, 1));
                collar.Alpha = showCaps && showFirstTick && showSecondTick ? 1 : 0;
                positionTick(firstTick, firstTickAngle, radius, showFirstTick);
                positionTick(secondTick, secondTickAngle, radius,
                    showSecondTick && (!showFirstTick || Math.Abs(SticksHitObject.DeltaAngle(firstTickAngle, secondTickAngle)) >= 0.01f));
                Alpha = radius > 0.01f ? 1 : 0;
            }

            private static SmoothPath createTick(Color4 colour, float halfLength) => new SmoothPath
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.None,
                Size = new Vector2(halfLength * 2 + marker_half_thickness * 2, marker_half_thickness * 2),
                PathRadius = marker_half_thickness,
                Colour = colour,
                Vertices = new[]
                {
                    new Vector2(marker_half_thickness, marker_half_thickness),
                    new Vector2(marker_half_thickness + halfLength * 2, marker_half_thickness),
                },
            };

            private static void positionTick(SmoothPath tick, float angle, float radius, bool visible)
            {
                tick.Position = SticksPlayfield.PointAt(angle, radius);
                tick.Rotation = angle;
                tick.Alpha = visible ? 1 : 0;
            }
        }
    }
}
