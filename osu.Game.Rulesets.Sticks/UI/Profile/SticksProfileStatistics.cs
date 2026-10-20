#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Overlays.Profile.Header.Components;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Profiles;
using osu.Game.Scoring;
using osu.Game.Utils;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI.Profile
{
    internal sealed record SticksProfileSummary(double PP, double Accuracy, int MaxCombo, int TopPlays, IReadOnlyDictionary<ScoreRank, int> Grades)
    {
        /// <summary>
        /// Grades a No Mod play from its accuracy alone, through the ruleset's own score processor.
        /// </summary>
        public static Func<double, ScoreRank> CreateRankLookup()
        {
            var processor = new SticksRuleset().CreateScoreProcessor();
            var noResults = new Dictionary<HitResult, int>();
            return accuracy => processor.RankFromScore(accuracy, noResults);
        }

        public static SticksProfileSummary From(IReadOnlyList<SticksProfileScore> topPlays, Func<double, ScoreRank> rankFor)
        {
            // Matches osu!'s hit accuracy: the weighted average of the plays that count toward pp.
            var counted = topPlays.Take(100).ToArray();
            double weights = counted.Select((_, i) => Math.Pow(0.95, i)).Sum();
            double accuracy = weights > 0 ? counted.Select((s, i) => s.Accuracy * Math.Pow(0.95, i)).Sum() / weights : 0;
            var grades = topPlays.GroupBy(s => rankFor(s.Accuracy)).ToDictionary(g => g.Key, g => g.Count());
            return new SticksProfileSummary(SticksProfilePolicy.TotalPP(topPlays), accuracy,
                topPlays.Count == 0 ? 0 : topPlays.Max(s => s.MaxCombo), topPlays.Count, grades);
        }
    }

    /// <summary>
    /// The rank, pp and grade strip beneath the user header, laid out like lazer's profile details.
    /// </summary>
    internal partial class SticksProfileStatistics : CompositeDrawable
    {
        private static readonly ScoreRank[] grades = { ScoreRank.X, ScoreRank.S, ScoreRank.A };

        private readonly Dictionary<ScoreRank, OsuSpriteText> gradeCounts = new Dictionary<ScoreRank, OsuSpriteText>();
        private ProfileValueDisplay globalRank = null!;
        private ProfileValueDisplay pp = null!;
        private ProfileValueDisplay accuracy = null!;
        private ProfileValueDisplay maxCombo = null!;
        private ProfileValueDisplay topPlays = null!;

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                new Container
                {
                    RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Horizontal = WaveOverlayContainer.HORIZONTAL_PADDING, Vertical = 15 },
                    Children = new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal, Spacing = new Vector2(20, 0),
                            Children = new Drawable[]
                            {
                                globalRank = bottom(new ProfileValueDisplay(true, 110) { Title = UsersStrings.ShowRankGlobalSimple }),
                                pp = bottom(new ProfileValueDisplay(true, 90) { Title = "pp" }),
                                bottom(new Box
                                {
                                    Width = 2, Height = 38, Colour = colourProvider.Background6,
                                    Margin = new MarginPadding { Bottom = 2, Horizontal = 5 },
                                }),
                                accuracy = bottom(new ProfileValueDisplay { Title = UsersStrings.ShowStatsHitAccuracy }),
                                maxCombo = bottom(new ProfileValueDisplay { Title = UsersStrings.ShowStatsMaximumCombo }),
                                topPlays = bottom(new ProfileValueDisplay { Title = "Top Plays" }),
                            },
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.BottomRight, Origin = Anchor.BottomRight, AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal, Spacing = new Vector2(5),
                            ChildrenEnumerable = grades.Select(createGrade),
                        },
                    },
                },
            };

            SetSummary(null);
            SetGlobalRank(null, "Share your scores to get a global ranking");
        }

        public void SetSummary(SticksProfileSummary? summary)
        {
            pp.Content.Text = summary?.PP.ToLocalisableString("#,##0") ?? (LocalisableString)"-";
            pp.Content.TooltipText = summary == null ? default : summary.PP.ToLocalisableString("#,##0.0");
            accuracy.Content.Text = summary == null || summary.TopPlays == 0 ? "-" : summary.Accuracy.FormatAccuracy();
            accuracy.Content.TooltipText = "Weighted across the plays that count toward pp";
            maxCombo.Content.Text = summary == null || summary.TopPlays == 0 ? "-" : $"{summary.MaxCombo:N0}x";
            topPlays.Content.Text = summary?.TopPlays.ToLocalisableString("N0") ?? (LocalisableString)"-";
            foreach (var (rank, text) in gradeCounts)
                text.Text = (summary?.Grades.GetValueOrDefault(rank) ?? 0).ToLocalisableString("N0");
        }

        public void SetGlobalRank(int? rank, LocalisableString tooltip)
        {
            globalRank.Content.Text = rank?.ToLocalisableString("\\##,##0") ?? (LocalisableString)"-";
            globalRank.Content.TooltipText = tooltip;
        }

        private Drawable createGrade(ScoreRank rank) => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Y, Width = 44, Direction = FillDirection.Vertical,
            Children = new Drawable[]
            {
                new DrawableRank(rank) { RelativeSizeAxes = Axes.X, Height = 22 },
                gradeCounts[rank] = new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                },
            },
        };

        private static T bottom<T>(T drawable) where T : Drawable
        {
            drawable.Anchor = Anchor.BottomLeft;
            drawable.Origin = Anchor.BottomLeft;
            return drawable;
        }
    }
}
