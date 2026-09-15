#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.Edit.Blueprints
{
    /// <summary>
    /// Existing notes keep their gameplay artwork; selections only add a lightweight highlight.
    /// The single placement preview reuses gameplay markers and the ribbon compositor.
    /// </summary>
    public partial class SticksBlueprintPiece : CompositeDrawable
    {
        private readonly bool showNote;
        private readonly CircularProgress selectionArc;
        private readonly Box selectionMarker;
        private SticksArcMarker? head;
        private SticksSliderHeadMarker? sliderHead;
        private SticksClickHalo? halo;
        private SticksRadialTimelinePath? body;
        private SticksPlayfield.SticksRibbonBuffer? bodyBuffer;
        private OsuSpriteText? detailText;
        private SticksHitObject? displayedObject;
        private double displayedTime;
        private double displayedApproachDuration;
        private float displayedRadius;
        private float displayedAngle;
        private float displayedSpan;
        private Color4 displayedColour;
        private StickSide displayedSide;
        private bool hasColour;

        [Resolved(CanBeNull = true)]
        private SticksHitObjectComposer? composer { get; set; }

        public Drawable Marker => selectionMarker;

        public Vector2 EndpointPosition { get; private set; }

        public Quad SelectionQuad
        {
            get
            {
                if (displayedObject is not SticksClick)
                    return Marker.ScreenSpaceDrawQuad;

                float radius = displayedRadius + 5;
                Vector2 centre = SticksEditorCoordinates.Centre;
                return new Quad(ToScreenSpace(centre + new Vector2(-radius, -radius)),
                    ToScreenSpace(centre + new Vector2(radius, -radius)),
                    ToScreenSpace(centre + new Vector2(-radius, radius)),
                    ToScreenSpace(centre + new Vector2(radius, radius)));
            }
        }

        public SticksBlueprintPiece(bool showNote = true)
        {
            this.showNote = showNote;
            Size = new Vector2(SticksPlayfield.SIZE);
            InternalChildren = new Drawable[]
            {
                selectionArc = new CircularProgress
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = SticksEditorCoordinates.Centre,
                    RoundedCaps = true,
                    Colour = Color4.White,
                    Alpha = 0,
                },
                selectionMarker = new Box
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Size = new Vector2(34),
                    Alpha = 0,
                    AlwaysPresent = true,
                },
            };
        }

        public void UpdateFrom(SticksHitObject hitObject, double? time = null, bool selected = false, bool bothSticks = false)
        {
            displayedObject = hitObject;
            displayedTime = time ?? hitObject.StartTime;
            displayedApproachDuration = composer?.PlayerApproachDuration ?? hitObject.ApproachDuration;
            displayedRadius = SticksEditorCoordinates.RadiusAt(displayedTime, hitObject.StartTime, displayedApproachDuration);
            displayedAngle = SticksEditorCoordinates.AngleAt(hitObject, displayedTime);
            displayedSpan = hitObject is SticksClick ? 360 : hitObject.PrimaryHitAngle;

            selectionMarker.Position = SticksPlayfield.PointAt(displayedAngle, displayedRadius);
            selectionMarker.Rotation = displayedAngle;
            float chordWidth = 2 * displayedRadius * MathF.Sin(Math.Min(180, displayedSpan) * MathF.PI / 360);
            selectionMarker.Size = new Vector2(28, Math.Max(28, chordWidth));
            setArc(selectionArc, displayedRadius, displayedAngle, displayedSpan, 8);
            selectionArc.Alpha = selected && displayedRadius >= 8 ? 0.28f : 0;

            if (!showNote)
                return;

            var playfield = composer?.Playfield as SticksPlayfield;
            Color4 colour = bothSticks
                ? playfield?.OverlapColour ?? SticksPlayfield.OVERLAP_COLOUR
                : playfield?.ColourFor(hitObject.Side)
                  ?? (hitObject.Side == StickSide.Left ? SticksPlayfield.LEFT_COLOUR : SticksPlayfield.RIGHT_COLOUR);
            bool changedColour = !hasColour || displayedSide != hitObject.Side || displayedColour != colour;

            if (hitObject is SticksClick)
            {
                if (halo == null)
                {
                    AddInternal(halo = new SticksClickHalo
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.Centre,
                        Position = SticksEditorCoordinates.Centre,
                    });
                }

                halo.SetGeometry(displayedRadius, colour);
                halo.Show();
                head?.Hide();
                sliderHead?.Hide();
            }
            else if (hitObject is SticksSlider slider && slider.TotalAngularDistance > 0)
            {
                if (sliderHead == null)
                {
                    AddInternal(sliderHead = new SticksSliderHeadMarker(slider.Side, slider.InitialDirection, colour, true));
                }
                else if (changedColour || sliderHead.Direction != slider.InitialDirection)
                    sliderHead.SetLaneAndDirection(slider.Side, slider.InitialDirection, colour);

                sliderHead.Angle = displayedAngle;
                sliderHead.Span = displayedSpan;
                sliderHead.SetRadialOffset(displayedRadius - SticksPlayfield.RadiusFor(slider.Side), true);
                sliderHead.Show();
                head?.Hide();
                halo?.Hide();
            }
            else
            {
                if (head == null)
                {
                    AddInternal(head = new SticksArcMarker(hitObject.Side, colour, true));
                }
                else if (changedColour)
                    head.SetLane(hitObject.Side, colour);

                head.Angle = displayedAngle;
                head.Span = displayedSpan;
                head.SetRadialOffset(displayedRadius - SticksPlayfield.RadiusFor(hitObject.Side), true);
                head.Show();
                sliderHead?.Hide();
                halo?.Hide();
            }

            hasColour = true;
            displayedColour = colour;
            displayedSide = hitObject.Side;

            if (hitObject is SticksSlider or SticksHold)
            {
                ensureBody(hitObject.Side);
                bodyBuffer!.SetPalette(bothSticks ? colour : playfield?.ColourFor(StickSide.Left) ?? SticksPlayfield.LEFT_COLOUR,
                    bothSticks ? colour : playfield?.ColourFor(StickSide.Right) ?? SticksPlayfield.RIGHT_COLOUR,
                    playfield?.OverlapColour ?? SticksPlayfield.OVERLAP_COLOUR);
                bodyBuffer.Show();
                double duration = hitObject is SticksSlider s ? s.Duration : ((SticksHold)hitObject).Duration;
                // Fit the entire path while placing it. Existing notes use the actual
                // editor clock and approach duration through their gameplay drawable.
                double previewDuration = time.HasValue ? displayedApproachDuration : Math.Max(displayedApproachDuration, duration * 1.2);
                EndpointPosition = SticksPlayfield.PointAt(SticksEditorCoordinates.AngleAt(hitObject, hitObject.StartTime + duration),
                    SticksEditorCoordinates.RadiusAt(displayedTime, hitObject.StartTime + duration, previewDuration));
                if (hitObject is SticksSlider previewSlider)
                    body!.SetSliderGeometry(previewSlider, displayedTime, previewDuration);
                else
                    body!.SetHoldGeometry((SticksHold)hitObject, displayedTime, previewDuration);

                detailText!.Position = SticksPlayfield.PointAt(hitObject.Angle, SticksPlayfield.GUIDE_RADIUS + 30);
                detailText.Colour = colour;
                // Both initial placement and continuation previews contain the pending span.
                // Show its signed, unwrapped travel so full turns and reversals can be matched.
                detailText.Text = hitObject is SticksSlider pending
                    ? FormattableString.Invariant($"{duration:0} ms · {pending.ArcAngle:+0.##;-0.##;0}°")
                    : $"{duration:0} ms";
                detailText.Show();
            }
            else
            {
                bodyBuffer?.Hide();
                detailText?.Hide();
            }
        }

        private void ensureBody(StickSide side)
        {
            if (body != null)
                return;

            // Blueprints are siblings of the drawable ruleset, outside its resource scope.
            // Share its existing shader manager so the Sticks compositor can be resolved.
            var shaders = composer?.Playfield.Dependencies.Get<ShaderManager>();
            AddInternal(bodyBuffer = new SticksPlayfield.SticksRibbonBuffer(shaders)
            {
                RelativeSizeAxes = Axes.Both,
                Depth = 1,
                BackgroundColour = new Color4(0, 0, 0, 0),
                Child = body = new SticksRadialTimelinePath(side),
            });
            AddInternal(detailText = new OsuSpriteText
            {
                Name = "Slider placement details",
                Origin = Anchor.Centre,
                Font = OsuFont.Default.With(size: 13, weight: FontWeight.Bold),
            });
        }

        public bool ReceiveAt(Vector2 screenSpacePosition)
        {
            if (displayedObject == null || displayedRadius < 8)
                return false;

            Vector2 local = ToLocalSpace(screenSpacePosition);
            Vector2 delta = local - SticksEditorCoordinates.Centre;
            float radius = delta.Length;
            if (displayedObject is SticksClick)
                return Math.Abs(radius - displayedRadius) <= 12;

            if (!SticksEditorCoordinates.TryGetAngle(local, out float angle))
                return false;

            float tolerance = displayedSpan / 2 + 3;
            if (Math.Abs(radius - displayedRadius) <= 14
                && Math.Abs(SticksHitObject.DeltaAngle(displayedAngle, angle)) <= tolerance)
                return true;

            // Radius encodes the timestamp, so selecting the visible ribbon needs
            // no tessellation or cached copy of the path.
            if (!showNote && radius <= SticksPlayfield.GUIDE_RADIUS && displayedObject is SticksSlider or SticksHold)
            {
                double pathTime = displayedTime + (1 - radius / SticksPlayfield.GUIDE_RADIUS) * displayedApproachDuration;
                double endTime = displayedObject is SticksSlider slider ? slider.EndTime : ((SticksHold)displayedObject).EndTime;
                return pathTime >= displayedObject.StartTime && pathTime <= endTime
                       && Math.Abs(SticksHitObject.DeltaAngle(SticksEditorCoordinates.AngleAt(displayedObject, pathTime), angle)) <= tolerance;
            }

            return false;
        }

        private static void setArc(CircularProgress arc, float radius, float angle, float span, float halfThickness)
        {
            float outerRadius = Math.Max(0.001f, radius + halfThickness);
            arc.Size = new Vector2(outerRadius * 2);
            arc.InnerRadius = Math.Min(1, halfThickness * 2 / outerRadius);
            arc.Rotation = 90 + angle - span / 2;
            arc.Progress = span / 360;
        }
    }
}
