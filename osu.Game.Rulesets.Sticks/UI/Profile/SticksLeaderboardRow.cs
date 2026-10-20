#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Profile.Sections;
using osu.Game.Rulesets.Sticks.Profiles;
using osu.Game.Users.Drawables;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI.Profile
{
    /// <summary>
    /// A community leaderboard entry in the style of lazer's profile rows. Selecting it opens that player's Sticks profile.
    /// </summary>
    internal partial class SticksLeaderboardRow : OsuClickableContainer
    {
        public const float HEIGHT = 40;
        private const float performance_width = 130;

        private readonly SticksLeaderboardClient.Player player;
        private readonly bool isLocalUser;

        public SticksLeaderboardRow(SticksLeaderboardClient.Player player, bool isLocalUser, Action open)
        {
            this.player = player;
            this.isLocalUser = isLocalUser;
            Action = open;
            RelativeSizeAxes = Axes.X;
            Height = HEIGHT;
            TooltipText = "View top plays";
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, OverlayColourProvider colourProvider)
        {
            var user = new APIUser { Id = player.UserId, Username = player.Username };

            Child = new RowContainer(isLocalUser)
            {
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Y, Width = 3, Colour = colourProvider.Highlight1, Alpha = isLocalUser ? 1 : 0,
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal, Spacing = new Vector2(12, 0), Padding = new MarginPadding { Left = 15 },
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Width = 50, AutoSizeAxes = Axes.Y,
                                Child = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight,
                                    Text = player.Rank.ToLocalisableString("\\##,##0"),
                                    Font = OsuFont.GetFont(size: player.Rank <= 3 ? 16 : 14, weight: FontWeight.Bold),
                                    Colour = rankColour(colours, colourProvider),
                                },
                            },
                            new UpdateableAvatar(user, isInteractive: false, showGuestOnNull: false)
                            {
                                Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Size = new Vector2(28), Masking = true, CornerRadius = 6,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Text = player.Username,
                                Font = OsuFont.GetFont(size: 14, weight: isLocalUser ? FontWeight.Bold : FontWeight.SemiBold),
                            },
                            new YouPill { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Alpha = isLocalUser ? 1 : 0 },
                        },
                    },
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, Size = new Vector2(10), X = -(performance_width + 12),
                        Icon = FontAwesome.Solid.ChevronRight, Colour = colourProvider.Foreground1,
                    },
                    new SticksPerformanceBadge(player.PP, performance_width, player.PP.ToLocalisableString("N2"))
                    {
                        Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight,
                    },
                },
            };
        }

        private osuTK.Graphics.Color4 rankColour(OsuColour colours, OverlayColourProvider colourProvider) => player.Rank switch
        {
            1 => colours.Yellow,
            2 => OsuColour.Gray(0.85f),
            3 => colours.Orange1,
            _ => colourProvider.Content2,
        };

        private partial class RowContainer : ProfileItemContainer
        {
            private readonly bool highlighted;

            public RowContainer(bool highlighted) => this.highlighted = highlighted;

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                if (!highlighted) return;
                IdleColour = colourProvider.Background1;
                HoverColour = colourProvider.Light4;
            }
        }

        private partial class YouPill : CircularContainer
        {
            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                AutoSizeAxes = Axes.Both;
                Masking = true;
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Highlight1 },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre, Origin = Anchor.Centre, Margin = new MarginPadding { Horizontal = 8, Vertical = 1 },
                        Text = "YOU", Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold), Colour = colourProvider.Background6,
                    },
                };
            }
        }
    }
}
