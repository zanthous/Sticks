#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Overlays.Profile.Sections;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Rulesets.Sticks.Profiles;
using osu.Game.Scoring;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Sticks.UI.Profile
{
    // Position is zero-based and determines the pp weight. Artist and Difficulty are null when the beatmap is no longer installed.
    internal sealed record SticksProfilePlay(SticksProfileScore Score, int Position, RomanisableString Title, RomanisableString? Artist,
                                             string? Difficulty, ScoreRank Rank, bool HasReplay);

    /// <summary>
    /// A top play laid out like lazer's weighted profile score row, with hit statistics in columns and a replay button.
    /// </summary>
    internal partial class SticksProfilePlayRow : CompositeDrawable
    {
        public const float HEIGHT = 44;
        private const float performance_width = 100;
        private const float accuracy_width = 65;
        private const float weighted_width = 80;

        // Below this width the statistic columns give way to the title, and misses move into the details line.
        private const float columns_min_width = 1000;

        private static readonly StatColumn[] stat_columns =
        {
            new StatColumn("GREAT", 52, c => c.Blue, "Notes and clicks hit with a 300 for timing"),
            new StatColumn("OK", 40, c => c.Green, "Notes and clicks hit with a 100 for timing"),
            new StatColumn("MEH", 40, c => c.Yellow, "Notes and clicks hit with a 50 for timing"),
            // Lazer's darker miss red is hard to read on the purple rows.
            new StatColumn("MISS", 40, c => c.Red1, "Notes and clicks missed"),
            new StatColumn("BREAKS", 52, c => c.Orange1, "Tracking checkpoints missed on sliders and holds"),
        };

        private readonly SticksProfilePlay play;
        private readonly Action? watch;
        private Drawable statistics = null!;
        private Drawable inlineMisses = null!;

        public SticksProfilePlayRow(SticksProfilePlay play, Action? watch)
        {
            this.play = play;
            this.watch = watch;
            RelativeSizeAxes = Axes.X;
            Height = HEIGHT;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, OverlayColourProvider colourProvider)
        {
            var score = play.Score;
            bool counted = play.Position < 100;
            double weight = counted ? Math.Pow(0.95, play.Position) : 0;
            string breakdown = judgementBreakdown(score.Judgements);

            var details = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both, Direction = FillDirection.Horizontal, Spacing = new Vector2(15, 0),
                Children = new[]
                {
                    new OsuSpriteText
                    {
                        Text = play.Difficulty ?? "Beatmap not installed",
                        Font = OsuFont.GetFont(size: 12), Colour = play.Difficulty != null ? colours.Yellow : colourProvider.Foreground1,
                    },
                    new DrawableDate(DateTimeOffset.FromUnixTimeMilliseconds(score.PlayedAt), 12) { Colour = colourProvider.Foreground1 },
                    new OsuSpriteText
                    {
                        Text = $"{score.MaxCombo:N0}x", Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold), Colour = colourProvider.Foreground1,
                    },
                    inlineMisses = new TooltipSpriteText
                    {
                        Text = score.MissCount == 1 ? "1 miss" : $"{score.MissCount:N0} misses",
                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                        Colour = score.MissCount > 0 ? colours.Red1 : colourProvider.Foreground1,
                        TooltipText = breakdown,
                    },
                },
            };

            int[] values = Statistics(score);
            statistics = new StatisticsBlock(breakdown)
            {
                ChildrenEnumerable = stat_columns.Select((column, i) => cell(new OsuSpriteText
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre, Text = values[i].ToLocalisableString("N0"),
                    Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                    Colour = column.Colour(colours), Alpha = values[i] > 0 ? 1 : 0.35f,
                }, column.Width, Anchor.Centre)),
            };

            InternalChild = new ProfileItemContainer
            {
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Left = 20, Right = performance_width },
                        Children = new Drawable[]
                        {
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ColumnDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.AutoSize), new Dimension(GridSizeMode.Absolute, 10),
                                    new Dimension(), new Dimension(GridSizeMode.AutoSize),
                                },
                                Content = new[]
                                {
                                    new Drawable[]
                                    {
                                        new UpdateableRank(play.Rank) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Size = new Vector2(50, 20) },
                                        Empty(),
                                        new FillFlowContainer
                                        {
                                            Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical, Spacing = new Vector2(0, 2), Masking = true,
                                            Children = new Drawable[] { new TitleLine(play.Title, play.Artist), details },
                                        },
                                        columns(
                                            statistics,
                                            // An empty slot keeps the columns aligned with rows that have a replay.
                                            play.HasReplay && watch != null ? new WatchReplayButton(watch) : Empty(),
                                            new OsuSpriteText
                                            {
                                                Text = score.Accuracy.FormatAccuracy(),
                                                Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold, italics: true), Colour = colours.Yellow,
                                            },
                                            new FillFlowContainer
                                            {
                                                AutoSizeAxes = Axes.Both, Direction = FillDirection.Vertical, Alpha = counted ? 1 : 0.5f,
                                                Children = new Drawable[]
                                                {
                                                    new OsuSpriteText
                                                    {
                                                        Text = $"{score.PP * weight:N0}pp", Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold, italics: true),
                                                    },
                                                    new OsuSpriteText
                                                    {
                                                        Font = OsuFont.GetFont(size: 12),
                                                        Text = counted ? UsersStrings.ShowExtraTopRanksPpWeight(weight.ToLocalisableString("0%")) : "outside top 100",
                                                    },
                                                },
                                            }),
                                    },
                                },
                            },
                        },
                    },
                    new SticksPerformanceBadge(score.PP, performance_width, score.PP.ToLocalisableString("N2"))
                    {
                        Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight,
                    },
                },
            };
        }

        protected override void Update()
        {
            base.Update();
            bool wide = DrawWidth >= columns_min_width;
            statistics.Alpha = wide ? 1 : 0;
            inlineMisses.Alpha = wide ? 0 : 1;
        }

        /// <summary>
        /// Great, Ok, Meh, Miss and Breaks. Each note or click counts once, by its timing judgement; a missed note
        /// records both a timing and an angle miss, which the leaderboard also counts once.
        /// </summary>
        internal static int[] Statistics(SticksProfileScore score)
        {
            int[] j = score.Judgements;
            if (j.Length != SticksProfileScore.JudgementCount) return new int[stat_columns.Length];
            return new[] { j[0] + j[8], j[1] + j[9], j[2] + j[10], score.MissCount, j[13] };
        }

        private static string judgementBreakdown(int[] j)
        {
            if (j.Length != SticksProfileScore.JudgementCount) return string.Empty;
            string grades(string label, int i) => $"{label}: {j[i]:N0} × 300 · {j[i + 1]:N0} × 100 · {j[i + 2]:N0} × 50 · {j[i + 3]:N0} missed";
            var lines = new List<string> { grades("Timing", 0), grades("Angle", 4) };
            if (j[8] + j[9] + j[10] + j[11] > 0) lines.Add(grades("Clicks", 8));
            lines.Add($"Tracking checkpoints: {j[12]:N0} hit · {j[13]:N0} missed");
            lines.Add($"Object ends: {j[14]:N0} hit · {j[15]:N0} missed");
            return string.Join('\n', lines);
        }

        // Rows and the header share this, so labels sit exactly over their values.
        private static FillFlowContainer columns(Drawable statistics, Drawable watch, Drawable accuracy, Drawable weighted) => new FillFlowContainer
        {
            Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, AutoSizeAxes = Axes.X, RelativeSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal, Spacing = new Vector2(15, 0), Padding = new MarginPadding { Horizontal = 10 },
            Children = new[]
            {
                statistics,
                cell(watch, WatchReplayButton.SIZE, Anchor.CentreLeft),
                cell(accuracy, accuracy_width, Anchor.CentreLeft),
                cell(weighted, weighted_width, Anchor.CentreLeft),
            },
        };

        private static Container cell(Drawable content, float width, Anchor anchor)
        {
            content.Anchor = anchor;
            content.Origin = anchor;
            return new Container { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Width = width, RelativeSizeAxes = Axes.Y, Child = content };
        }

        private sealed record StatColumn(string Label, float Width, Func<OsuColour, Color4> Colour, string Description);

        private partial class StatisticsBlock : FillFlowContainer, IHasTooltip
        {
            public LocalisableString TooltipText { get; }

            public StatisticsBlock(LocalisableString tooltip)
            {
                TooltipText = tooltip;
                Anchor = Anchor.CentreLeft;
                Origin = Anchor.CentreLeft;
                AutoSizeAxes = Axes.X;
                RelativeSizeAxes = Axes.Y;
                Direction = FillDirection.Horizontal;
            }
        }

        /// <summary>
        /// Column labels for a list of play rows, coloured like lazer's judgement counts.
        /// </summary>
        public partial class ColumnHeader : CompositeDrawable
        {
            private Drawable statistics = null!;

            [BackgroundDependencyLoader]
            private void load(OsuColour colours, OverlayColourProvider colourProvider)
            {
                RelativeSizeAxes = Axes.X;
                Height = 22;

                TooltipSpriteText label(string text, Color4 colour, string tooltip = "") => new TooltipSpriteText
                {
                    Text = text, Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold), Colour = colour, TooltipText = tooltip,
                };

                statistics = new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, AutoSizeAxes = Axes.X, RelativeSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    ChildrenEnumerable = stat_columns.Select(column => cell(label(column.Label, column.Colour(colours), column.Description), column.Width, Anchor.Centre)),
                };

                InternalChildren = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Left = 20, Right = performance_width },
                        Child = columns(statistics, Empty(), label("ACCURACY", colourProvider.Foreground1), label("WEIGHTED", colourProvider.Foreground1)),
                    },
                    new Container
                    {
                        Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, Width = performance_width, RelativeSizeAxes = Axes.Y,
                        Child = label("PP", colourProvider.Foreground1).With(l =>
                        {
                            l.Anchor = Anchor.Centre;
                            l.Origin = Anchor.Centre;
                        }),
                    },
                };
            }

            protected override void Update()
            {
                base.Update();
                statistics.Alpha = DrawWidth >= columns_min_width ? 1 : 0;
            }
        }

        /// <summary>
        /// "Title by Artist", where the title gives way with an ellipsis before the artist does.
        /// </summary>
        private partial class TitleLine : CompositeDrawable
        {
            private readonly TruncatingSpriteText title;
            private readonly TruncatingSpriteText? artist;

            public TitleLine(RomanisableString titleText, RomanisableString? artistText)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                var flow = new FillFlowContainer { AutoSizeAxes = Axes.Both, Direction = FillDirection.Horizontal };
                flow.Add(title = new TruncatingSpriteText
                {
                    Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, Text = titleText,
                    Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold, italics: true),
                });
                if (artistText != null)
                {
                    flow.Add(artist = new TruncatingSpriteText
                    {
                        Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft,
                        Text = new RomanisableString($" by {artistText.Original}", $" by {artistText.Romanised}"),
                        Font = OsuFont.GetFont(size: 12, italics: true),
                    });
                }
                InternalChild = flow;
            }

            protected override void Update()
            {
                base.Update();
                if (artist != null) artist.MaxWidth = DrawWidth * 0.45f;
                title.MaxWidth = Math.Max(0, DrawWidth - (artist?.DrawWidth ?? 0));
            }
        }

        public partial class WatchReplayButton : OsuAnimatedButton
        {
            public const float SIZE = 28;

            public WatchReplayButton(Action watch)
            {
                Size = new Vector2(SIZE);
                Action = watch;
                TooltipText = "Watch replay";
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                AddRange(new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background3, Depth = float.MaxValue },
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(10), X = 1,
                        Icon = FontAwesome.Solid.Play, Colour = colourProvider.Light1,
                    },
                });
            }
        }
    }
}
