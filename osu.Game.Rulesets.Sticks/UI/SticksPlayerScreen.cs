#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Profile.Sections;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Rulesets.Sticks.Profiles;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.UI.Profile;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// A player's shared Sticks top plays as the community leaderboard has them, with their uploaded replays.
    /// </summary>
    internal partial class SticksPlayerScreen : OsuScreen
    {
        public override string Title => "Sticks player";
        public override bool HideOverlaysOnEnter => true;
        public override float BackgroundParallaxAmount => 0;

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        [Resolved] private Storage storage { get; set; } = null!;
        [Resolved] private IAPIProvider api { get; set; } = null!;
        [Resolved] private BeatmapManager beatmaps { get; set; } = null!;
        [Resolved] private RulesetStore rulesets { get; set; } = null!;
        [Resolved(CanBeNull = true)] private INotificationOverlay? notifications { get; set; }
        [Resolved(CanBeNull = true)] private OsuGame? game { get; set; }

        private readonly SticksLeaderboardClient.Player player;
        private readonly Func<Storage, SticksLeaderboardClient> createClient;
        private readonly Func<double, ScoreRank> rankFor = SticksProfileSummary.CreateRankLookup();
        private readonly Container userHeader;
        private readonly SticksProfileStatistics statistics;
        private readonly SticksProfileSection plays;
        private bool watching;
        private bool disposed;

        public SticksPlayerScreen(SticksLeaderboardClient.Player player, Func<Storage, SticksLeaderboardClient> createClient)
        {
            this.player = player;
            this.createClient = createClient;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background5 },
                new OsuScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical,
                        // Leaves room for the global back button.
                        Padding = new MarginPadding { Bottom = 80 },
                        Children = new Drawable[]
                        {
                            new SticksProfileTitleHeader(),
                            userHeader = new Container { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y },
                            statistics = new SticksProfileStatistics(),
                            new SticksProfileCard
                            {
                                Child = plays = new SticksProfileSection(UsersStrings.ShowExtraTopRanksBestTitle, CounterVisibilityState.AlwaysVisible,
                                    "Shared on the community leaderboard · No Mod")
                                {
                                    ColumnHeader = new SticksProfilePlayRow.ColumnHeader(),
                                },
                            },
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            showUser(new APIUser { Id = player.UserId, Username = player.Username });
            statistics.SetGlobalRank(player.Rank, $"{player.PP:N0}pp on the community leaderboard");

            // The leaderboard only knows the name; lazer's API adds the cover and country when signed in.
            if (api.IsLoggedIn)
            {
                var request = new GetUserRequest(player.UserId);
                request.Success += user => Schedule(() =>
                {
                    if (!disposed) showUser(user);
                });
                api.Queue(request);
            }

            loadPlays();
        }

        private void showUser(APIUser user) => userHeader.Child = new SticksProfileUserHeader(user, new RoundedButton
        {
            Width = 130, Height = 32, Text = "osu! profile", BackgroundColour = colourProvider.Background2,
            Action = () => game?.ShowUser(user),
        });

        private async void loadPlays()
        {
            plays.ShowLoading();
            try
            {
                var page = await createClient(storage).GetTopPlays(player.UserId).ConfigureAwait(false);
                var described = await Task.Run(() => describe(page.Entries)).ConfigureAwait(false);
                var summary = SticksProfileSummary.From(described.Select(d => d.Play.Score).ToArray(), rankFor);
                // The service's own total, rather than one recomputed from the plays it returned.
                if (page.Player.PP is double total) summary = summary with { PP = total };

                Schedule(() =>
                {
                    if (disposed) return;
                    statistics.SetSummary(summary);
                    plays.Count = page.Total;
                    plays.ClearItems();
                    plays.ClearNotice();
                    foreach (var (play, remote) in described)
                        plays.AddItem(new SticksProfilePlayRow(play, () => watch(remote)));
                    if (described.Length == 0)
                        plays.ShowNotice(new SticksProfileNotice(FontAwesome.Solid.Trophy, "No shared plays", $"{player.Username} hasn't shared any top plays yet."));
                });
            }
            catch (Exception e)
            {
                Logger.Log($"Could not load Sticks top plays for player {player.UserId}: {e}");
                Schedule(() =>
                {
                    if (disposed) return;
                    plays.ShowNotice(new SticksProfileNotice(FontAwesome.Solid.ExclamationTriangle, "Couldn't load top plays",
                        e is InvalidOperationException ? e.Message : "Check your connection and try again.", "Retry", loadPlays));
                });
            }
        }

        private (SticksProfilePlay Play, SticksLeaderboardClient.RemotePlay Remote)[] describe(IEnumerable<SticksLeaderboardClient.RemotePlay> entries)
        {
            var local = new SticksReplayStore(storage);
            var downloads = SticksReplayStore.Downloads(storage);

            return entries.Select((entry, i) =>
            {
                Guid.TryParseExact(entry.ScoreId, "N", out Guid scoreId);
                int beatmapId = entry.BeatmapId;
                // The service only has titles for maps it looked up after replays were added; fall back to an installed copy.
                BeatmapInfo? map = entry.Title == null ? beatmaps.QueryBeatmap(b => b.OnlineID == beatmapId) : null;
                string fallback = $"Beatmap {beatmapId}";
                var title = entry.Title != null ? new RomanisableString(entry.Title, entry.Title)
                    : map != null ? new RomanisableString(map.Metadata.TitleUnicode, map.Metadata.Title) : new RomanisableString(fallback, fallback);
                RomanisableString? artist = entry.Artist != null ? new RomanisableString(entry.Artist, entry.Artist)
                    : map != null ? new RomanisableString(map.Metadata.ArtistUnicode, map.Metadata.Artist) : null;

                var score = new SticksProfileScore
                {
                    ScoreId = scoreId, UserId = player.UserId, BeatmapId = beatmapId, BeatmapSetId = entry.BeatmapSetId,
                    SourceMd5 = entry.SourceMd5, PlayedAt = entry.PlayedAt, Generation = entry.Generation, PP = entry.PP,
                    Accuracy = entry.Accuracy, MaxCombo = entry.MaxCombo, TotalScore = entry.TotalScore,
                    ActivationThreshold = entry.ActivationThreshold, Ranked = true, MatchesPublishedMap = true, Judgements = entry.Judgements,
                };
                bool hasReplay = entry.Replay?.Available == true || local.Contains(scoreId) || downloads.Contains(scoreId);
                return (new SticksProfilePlay(score, (entry.Rank ?? i + 1) - 1, title, artist, entry.Difficulty ?? map?.DifficultyName,
                    rankFor(entry.Accuracy), hasReplay), entry);
            }).ToArray();
        }

        private async void watch(SticksLeaderboardClient.RemotePlay play)
        {
            if (watching) return;
            watching = true;

            try
            {
                Guid scoreId = Guid.ParseExact(play.ScoreId, "N");
                string md5 = play.SourceMd5.ToLowerInvariant();
                // Playback reconverts the exact beatmap revision the play was set on.
                BeatmapInfo? map = beatmaps.QueryBeatmap(b => b.MD5Hash == md5);
                if (map == null || map.BeatmapSet?.DeletePending == true)
                {
                    watching = false;
                    notifications?.Post(new SimpleNotification
                    {
                        Text = "Install this beatmap version to watch the replay. Click to open the beatmap.",
                        Activated = () =>
                        {
                            game?.ShowBeatmap(play.BeatmapId);
                            return true;
                        },
                    });
                    return;
                }

                // Your own plays are already on this computer.
                var downloads = SticksReplayStore.Downloads(storage);
                if (!new SticksReplayStore(storage).Contains(scoreId) && !downloads.Contains(scoreId))
                {
                    string path = play.Replay?.DownloadPath ?? throw new InvalidOperationException("This replay is no longer available");
                    byte[] package = await createClient(storage).DownloadReplay(path).ConfigureAwait(false);
                    byte[] raw = SticksReplayPackage.Decode(package, scoreId, md5, SticksReplayPackage.LocalMaxRawBytes);
                    if (!downloads.SaveRaw(scoreId, raw)) throw new InvalidOperationException("Couldn't save the replay; check storage permissions");
                }

                Schedule(() => startReplay(play, scoreId, map));
            }
            catch (Exception e)
            {
                Logger.Log($"Could not open a downloaded Sticks replay: {e}");
                Schedule(() =>
                {
                    watching = false;
                    notifications?.Post(new SimpleErrorNotification
                    {
                        Text = e is InvalidOperationException or System.IO.InvalidDataException ? e.Message : "Couldn't download this replay; check your connection",
                    });
                });
            }
        }

        private void startReplay(SticksLeaderboardClient.RemotePlay play, Guid scoreId, BeatmapInfo map)
        {
            watching = false;
            if (disposed || !this.IsCurrentScreen()) return;

            var score = new Score
            {
                ScoreInfo = new ScoreInfo(map, rulesets.GetRuleset("sticks"))
                {
                    ID = scoreId,
                    User = new APIUser { Id = player.UserId, Username = player.Username },
                    Accuracy = play.Accuracy, MaxCombo = play.MaxCombo, TotalScore = play.TotalScore,
                    Date = DateTimeOffset.FromUnixTimeMilliseconds(play.PlayedAt), Rank = rankFor(play.Accuracy),
                },
            };

            if (!new SticksReplayStore(storage).TryRestore(score) && !SticksReplayStore.Downloads(storage).TryRestore(score))
            {
                notifications?.Post(new SimpleErrorNotification { Text = "This replay couldn't be read" });
                return;
            }

            Beatmap.Value = beatmaps.GetWorkingBeatmap(map);
            this.Push(new ReplayPlayerLoader(score));
        }

        protected override void Dispose(bool isDisposing)
        {
            disposed = true;
            base.Dispose(isDisposing);
        }
    }
}
