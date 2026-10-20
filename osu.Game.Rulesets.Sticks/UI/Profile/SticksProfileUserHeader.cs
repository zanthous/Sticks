#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Users;
using osu.Game.Users.Drawables;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI.Profile
{
    internal enum SticksSharingState
    {
        Unavailable,
        Guest,
        Off,
        Connecting,
        On,
        Syncing,
    }

    /// <summary>
    /// The player's cover, avatar and name, with actions such as score sharing on the right.
    /// </summary>
    internal partial class SticksProfileUserHeader : CompositeDrawable
    {
        private readonly APIUser? user;
        private readonly Drawable actions;

        // A null user is a guest.
        public SticksProfileUserHeader(APIUser? user, Drawable actions)
        {
            this.user = user;
            this.actions = actions;
            actions.Anchor = Anchor.CentreRight;
            actions.Origin = Anchor.CentreRight;
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            var subtitle = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both, Direction = FillDirection.Horizontal, Spacing = new Vector2(6, 0),
                Margin = new MarginPadding { Top = 4 },
            };

            if (user != null && user.CountryCode != CountryCode.Unknown)
            {
                subtitle.AddRange(new Drawable[]
                {
                    new UpdateableFlag(user.CountryCode) { Size = new Vector2(28, 20) },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Text = user.CountryCode.GetDescription(),
                        Font = OsuFont.GetFont(size: 14),
                    },
                });
            }
            else
            {
                subtitle.Add(new OsuSpriteText
                {
                    Text = user == null ? "Playing as guest · top plays stay on this computer" : "osu!sticks",
                    Font = OsuFont.GetFont(size: 14), Colour = colourProvider.Content2,
                });
            }

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background3 },
                SticksUserCover.Create(user),
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientHorizontal(colourProvider.Background3.Opacity(0.92f), colourProvider.Background3.Opacity(0.6f)),
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Horizontal = WaveOverlayContainer.HORIZONTAL_PADDING, Vertical = 20 },
                    Children = new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal, Spacing = new Vector2(20, 0),
                            Children = new Drawable[]
                            {
                                new UpdateableAvatar(user, isInteractive: false)
                                {
                                    Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Size = new Vector2(90),
                                    Masking = true, CornerRadius = 25,
                                    EdgeEffect = new EdgeEffectParameters
                                    {
                                        Type = EdgeEffectType.Shadow, Offset = new Vector2(0, 1), Radius = 3, Colour = Colour4.Black.Opacity(0.25f),
                                    },
                                },
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, AutoSizeAxes = Axes.Both, Direction = FillDirection.Vertical,
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Text = user?.Username ?? "Guest", Font = OsuFont.GetFont(size: 28, weight: FontWeight.Regular),
                                        },
                                        subtitle,
                                    },
                                },
                            },
                        },
                        actions,
                    },
                },
            };
        }
    }

    /// <summary>
    /// Shows whether score summaries are shared with the community leaderboard and offers the matching actions.
    /// </summary>
    internal partial class SticksScoreSharingPanel : CompositeDrawable
    {
        public Action? Connect { get; init; }
        public Action? Disconnect { get; init; }
        public Action? Cancel { get; init; }

        public SticksSharingState State { get; private set; }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private Circle indicator = null!;
        private OsuSpriteText status = null!;
        private FillFlowContainer buttons = null!;
        private OsuTextFlowContainer detail = null!;
        private LoadingSpinner spinner = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            AutoSizeAxes = Axes.Both;
            InternalChild = new FillFlowContainer
            {
                Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical, Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        Anchor = Anchor.TopRight, Origin = Anchor.TopRight, AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal, Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            indicator = new Circle { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Size = new Vector2(8) },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Text = "SCORE SHARING",
                                Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold), Colour = colourProvider.Content2,
                            },
                            status = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            },
                            spinner = new LoadingSpinner { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Size = new Vector2(12) },
                        },
                    },
                    buttons = new FillFlowContainer
                    {
                        Anchor = Anchor.TopRight, Origin = Anchor.TopRight, AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal, Spacing = new Vector2(8, 0),
                    },
                    detail = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 12))
                    {
                        Anchor = Anchor.TopRight, Origin = Anchor.TopRight, AutoSizeAxes = Axes.Both, TextAnchor = Anchor.TopRight,
                    },
                },
            };
        }

        public void SetState(SticksSharingState state, LocalisableString message = default, bool isError = false)
        {
            State = state;
            buttons.Clear();
            (LocalisableString label, Colour4 colour, LocalisableString hint) info = state switch
            {
                SticksSharingState.Unavailable => ("Local only", colourProvider.Foreground1, "This build has no online leaderboard"),
                SticksSharingState.Guest => ("Off", colourProvider.Foreground1, "Log in to osu! to share your scores"),
                SticksSharingState.Off => ("Off", colourProvider.Foreground1, "Uploads your top plays and their replays · anyone can watch them"),
                SticksSharingState.Connecting => ("Signing in", colours.Yellow, "Finish signing in with osu! in your browser"),
                SticksSharingState.Syncing => ("Syncing", colours.Lime1, "Uploading any unsent top plays and replays"),
                _ => ("On", colours.Lime1, "New top plays and their replays upload automatically"),
            };

            switch (state)
            {
                case SticksSharingState.Off:
                    buttons.Add(button("Share scores", Connect));
                    break;

                case SticksSharingState.Connecting:
                    buttons.Add(button("Cancel", Cancel, colourProvider.Background2));
                    break;

                case SticksSharingState.On:
                case SticksSharingState.Syncing:
                    buttons.Add(button("Disconnect", Disconnect, colourProvider.Background2).With(b => b.Enabled.Value = state == SticksSharingState.On));
                    break;
            }

            buttons.Alpha = buttons.Count > 0 ? 1 : 0;
            indicator.Colour = info.colour;
            status.Text = info.label;
            status.Colour = info.colour;
            spinner.State.Value = state is SticksSharingState.Connecting or SticksSharingState.Syncing ? Visibility.Visible : Visibility.Hidden;
            detail.Text = message == default ? info.hint : message;
            detail.Colour = isError ? colours.Red1 : colourProvider.Content2;
        }

        private static RoundedButton button(LocalisableString text, Action? action, Colour4? background = null)
        {
            var button = new RoundedButton { Width = 130, Height = 32, Text = text, Action = action };
            if (background != null) button.BackgroundColour = background.Value;
            return button;
        }
    }
}
