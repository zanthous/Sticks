#nullable enable

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Settings;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI.Profile
{
    /// <summary>
    /// The prominent entry to the profile at the top of the Sticks settings, tinted with the player's stick colours.
    /// </summary>
    internal partial class SticksProfileSettingsButton : SettingsButtonV2
    {
        public LocalisableString Description { get; init; }

        private readonly IBindable<Colour4> leftColour;
        private readonly IBindable<Colour4> rightColour;
        private Box gradient = null!;

        public SticksProfileSettingsButton(IBindable<Colour4> leftColour, IBindable<Colour4> rightColour)
        {
            this.leftColour = leftColour.GetBoundCopy();
            this.rightColour = rightColour.GetBoundCopy();
            Height = 60;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // The base text stays set so settings search still matches it.
            SpriteText.Alpha = 0;

            AddRange(new Drawable[]
            {
                gradient = new Box { RelativeSizeAxes = Axes.Both, Depth = float.MaxValue - 1 },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both, Depth = -1, Padding = new MarginPadding { Horizontal = 18 },
                    ColumnDimensions = new[]
                    {
                        new Dimension(GridSizeMode.AutoSize), new Dimension(GridSizeMode.Absolute, 14),
                        new Dimension(), new Dimension(GridSizeMode.AutoSize),
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new SpriteIcon { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Size = new Vector2(24), Icon = OsuIcon.Ranking },
                            Empty(),
                            new FillFlowContainer
                            {
                                Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical, Spacing = new Vector2(0, 1),
                                Children = new Drawable[]
                                {
                                    new TruncatingSpriteText
                                    {
                                        RelativeSizeAxes = Axes.X, Text = Text, Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                                    },
                                    new TruncatingSpriteText
                                    {
                                        RelativeSizeAxes = Axes.X, Text = Description, Font = OsuFont.GetFont(size: 13), Alpha = 0.85f,
                                    },
                                },
                            },
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, Size = new Vector2(14), Icon = FontAwesome.Solid.ChevronRight,
                            },
                        },
                    },
                },
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Keep lazer's button triangles visible over the gradient.
            if (Triangles != null)
            {
                Content.ChangeChildDepth(Triangles, float.MaxValue - 2);
                Triangles.Colour = Colour4.White.Opacity(0.12f);
            }

            leftColour.BindValueChanged(_ => updateGradient(false, 0));
            rightColour.BindValueChanged(_ => updateGradient(false, 0), true);
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateGradient(true, 300);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            updateGradient(false, 300);
            base.OnHoverLost(e);
        }

        private void updateGradient(bool hovered, double duration)
        {
            float darken = hovered ? 0.1f : 0.45f;
            gradient.FadeColour(ColourInfo.GradientHorizontal(leftColour.Value.Darken(darken), rightColour.Value.Darken(darken)), duration, Easing.OutQuint);
        }
    }
}
