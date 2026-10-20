#nullable enable

using osu.Framework.Allocation;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI.Profile
{
    /// <summary>
    /// The chevron-backed pp column from lazer's profile score rows.
    /// </summary>
    internal partial class SticksPerformanceBadge : CompositeDrawable
    {
        private const float shear = 0.45f;

        private readonly double pp;
        private readonly LocalisableString tooltip;

        public SticksPerformanceBadge(double pp, float width = 100, LocalisableString tooltip = default)
        {
            this.pp = pp;
            this.tooltip = tooltip;
            RelativeSizeAxes = Axes.Y;
            Width = width;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            var font = OsuFont.GetFont(weight: FontWeight.Bold);
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    Anchor = Anchor.TopRight, Origin = Anchor.TopRight,
                    RelativeSizeAxes = Axes.Both, Height = 0.5f,
                    Colour = colourProvider.Background3, Shear = new Vector2(-shear, 0), EdgeSmoothness = new Vector2(2, 0),
                },
                new Box
                {
                    Anchor = Anchor.TopRight, Origin = Anchor.TopRight,
                    RelativeSizeAxes = Axes.Both, RelativePositionAxes = Axes.Y, Height = -0.5f, Position = new Vector2(0, 1),
                    Colour = colourProvider.Background3, Shear = new Vector2(shear, 0), EdgeSmoothness = new Vector2(2, 0),
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Vertical = 5, Left = 30, Right = 20 },
                    Child = new FillFlowContainer
                    {
                        Anchor = Anchor.Centre, Origin = Anchor.Centre, AutoSizeAxes = Axes.Both, Direction = FillDirection.Horizontal,
                        Children = new Drawable[]
                        {
                            new TooltipSpriteText
                            {
                                Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, Font = font,
                                Text = pp.ToLocalisableString("N0"), TooltipText = tooltip, Colour = colourProvider.Highlight1,
                            },
                            new TooltipSpriteText
                            {
                                Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, Font = font.With(size: 12),
                                Text = "pp", TooltipText = tooltip, Colour = colourProvider.Light3,
                            },
                        },
                    },
                },
            };
        }
    }

    internal partial class TooltipSpriteText : OsuSpriteText, IHasTooltip
    {
        public LocalisableString TooltipText { get; set; }
    }
}
