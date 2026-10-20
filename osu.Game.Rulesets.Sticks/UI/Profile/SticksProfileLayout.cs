#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.Profile.Sections;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI.Profile
{
    internal partial class SticksProfileTitleHeader : OverlayHeader
    {
        protected override OverlayTitle CreateTitle() => new SticksTitle();

        private partial class SticksTitle : OverlayTitle
        {
            public SticksTitle()
            {
                Title = "sticks profile";
                Icon = OsuIcon.Player;
            }
        }
    }

    /// <summary>
    /// The rounded, shadowed panel that profile sections sit in, as in lazer's profile overlay.
    /// </summary>
    internal partial class SticksProfileCard : Container
    {
        private readonly Container content;
        private readonly Box background;

        protected override Container<Drawable> Content => content;

        public SticksProfileCard()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Padding = new MarginPadding { Horizontal = 10, Top = 10 };
            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Masking = true, CornerRadius = 10,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow, Offset = new Vector2(0, 1), Radius = 3, Colour = Colour4.Black.Opacity(0.25f),
                },
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    content = new Container { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y },
                },
            };
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider) => background.Colour = colourProvider.Background4;
    }

    /// <summary>
    /// A paginated list in the style of a lazer profile subsection.
    /// </summary>
    internal partial class SticksProfileSection : CompositeDrawable
    {
        public Action? MoreAction { get; init; }

        public int Count
        {
            set => header.Current.Value = value;
        }

        public int ItemCount => items.Count;

        /// <summary>
        /// Labels for the rows' columns, shown above the list while it has rows.
        /// </summary>
        public Drawable? ColumnHeader
        {
            init => columnHeader.Child = value;
        }

        private readonly ProfileSubsectionHeader header;
        private readonly OsuSpriteText note;
        private readonly FillFlowContainer items;
        private readonly Container columnHeader;
        private readonly Container notice;
        private readonly ShowMoreButton more;

        public SticksProfileSection(LocalisableString title, CounterVisibilityState counter, LocalisableString note)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Horizontal = WaveOverlayContainer.HORIZONTAL_PADDING - 10, Vertical = 15 },
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            header = new ProfileSubsectionHeader(title, counter) { Margin = new MarginPadding { Left = 13 } },
                            this.note = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, Text = note, Font = OsuFont.GetFont(size: 12),
                            },
                        },
                    },
                    columnHeader = new Container { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Margin = new MarginPadding { Bottom = 2 } },
                    notice = new Container { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y },
                    items = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Spacing = new Vector2(0, 2) },
                    more = new ShowMoreButton
                    {
                        Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Margin = new MarginPadding { Top = 10 }, Alpha = 0,
                        Action = () => MoreAction?.Invoke(),
                    },
                },
            };
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider) => note.Colour = colourProvider.Foreground1;

        protected override void Update()
        {
            base.Update();
            columnHeader.Alpha = items.Count > 0 ? 1 : 0;
        }

        public void AddItem(Drawable item) => items.Add(item);

        public void ClearItems() => items.Clear();

        public void ShowLoading() => notice.Child = new Container
        {
            RelativeSizeAxes = Axes.X, Height = 120,
            Child = new LoadingSpinner { Anchor = Anchor.Centre, Origin = Anchor.Centre, State = { Value = Visibility.Visible } },
        };

        public void ShowNotice(SticksProfileNotice content) => notice.Child = content;

        public void ClearNotice() => notice.Clear();

        public void SetMoreAvailable(bool available)
        {
            more.IsLoading = false;
            more.Alpha = available ? 1 : 0;
        }
    }
}
