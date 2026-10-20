#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI.Profile
{
    /// <summary>
    /// A centred empty, unavailable or error state for a profile section.
    /// </summary>
    internal partial class SticksProfileNotice : CompositeDrawable
    {
        private readonly IconUsage icon;
        private readonly LocalisableString title;
        private readonly LocalisableString body;
        private readonly LocalisableString actionText;
        private readonly Action? action;

        public SticksProfileNotice(IconUsage icon, LocalisableString title, LocalisableString body, LocalisableString actionText = default, Action? action = null)
        {
            this.icon = icon;
            this.title = title;
            this.body = body;
            this.actionText = actionText;
            this.action = action;
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            OsuTextFlowContainer description;
            var flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Vertical = 30 }, Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Size = new Vector2(26), Icon = icon,
                        Colour = colourProvider.Highlight1, Margin = new MarginPadding { Bottom = 4 },
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Text = title,
                        Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold), Colour = colourProvider.Content1,
                    },
                    description = new OsuTextFlowContainer(t =>
                    {
                        t.Font = OsuFont.GetFont(size: 14);
                        t.Colour = colourProvider.Content2;
                    })
                    {
                        Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, RelativeSizeAxes = Axes.X, Width = 0.7f,
                        AutoSizeAxes = Axes.Y, TextAnchor = Anchor.TopCentre,
                    },
                },
            };
            description.AddText(body);

            if (action != null)
            {
                flow.Add(new RoundedButton
                {
                    Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Width = 170, Height = 34,
                    Margin = new MarginPadding { Top = 8 }, Text = actionText, Action = action,
                });
            }

            InternalChild = flow;
        }
    }
}
