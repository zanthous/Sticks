#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Profile.Sections;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Profiles;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.UI.Profile;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Play;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    internal partial class SticksProfileScreen : OsuScreen
    {
        private const int page_size = 50;
        private static readonly IReadOnlyDictionary<Guid, SticksLeaderboardClient.RemotePlay> no_shared_plays = new Dictionary<Guid, SticksLeaderboardClient.RemotePlay>();

        public override string Title => "Sticks profile";
        public override bool HideOverlaysOnEnter => true;
        public override float BackgroundParallaxAmount => 0;

        // Purple matches the overlap colour used where both sticks meet.
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        [Resolved] private Storage storage { get; set; } = null!;
        [Resolved] private IAPIProvider api { get; set; } = null!;
        [Resolved] private ScoreManager scores { get; set; } = null!;
        [Resolved] private BeatmapManager beatmaps { get; set; } = null!;
        [Resolved] private GameHost host { get; set; } = null!;
        [Resolved(CanBeNull = true)] private INotificationOverlay? notifications { get; set; }

        private readonly Func<Storage, SticksLeaderboardClient> createClient;
        private readonly Func<double, ScoreRank> rankFor;
        private readonly Bindable<SticksProfileTab> tab = new Bindable<SticksProfileTab>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private IBindable<APIUser> localUser = null!;

        private readonly Container userHeader;
        private readonly SticksProfileStatistics statistics;
        private readonly SticksProfileSection topPlaysSection;
        private readonly SticksProfileSection leaderboardSection;
        private readonly SticksSharedReplayPlayer sharedReplays;
        private SticksScoreSharingPanel sharing = null!;

        private SticksProfileScore[] topPlays = Array.Empty<SticksProfileScore>();
        // Your plays as the leaderboard has them, including ones set on your other computers.
        private IReadOnlyDictionary<Guid, SticksLeaderboardClient.RemotePlay> sharedPlays = no_shared_plays;
        private int? shownUserId;
        private int profileVersion;
        private int leaderboardVersion;
        private int leaderboardOffset;
        private bool leaderboardRequested;
        private CancellationTokenSource? accountOperation;
        private bool disposed;

        public SticksProfileScreen()
            : this(storage => new SticksLeaderboardClient(storage))
        {
        }

        internal SticksProfileScreen(Func<Storage, SticksLeaderboardClient> createClient)
        {
            this.createClient = createClient;

            rankFor = SticksProfileSummary.CreateRankLookup();

            InternalChildren = new Drawable[]
            {
                sharedReplays = new SticksSharedReplayPlayer(createClient, startReplay),
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
                            new TabBar { Current = tab },
                            new SticksProfileCard
                            {
                                Children = new Drawable[]
                                {
                                    topPlaysSection = new SticksProfileSection(UsersStrings.ShowExtraTopRanksBestTitle, CounterVisibilityState.AlwaysVisible,
                                        "Each play is weighted by its position · the best 100 count toward your pp")
                                    {
                                        MoreAction = showMorePlays,
                                        ColumnHeader = new SticksProfilePlayRow.ColumnHeader(),
                                    },
                                    leaderboardSection = new SticksProfileSection("Community Leaderboard", CounterVisibilityState.AlwaysHidden,
                                        "Opt-in · select a player to see their top plays")
                                    {
                                        MoreAction = () => loadLeaderboard(),
                                    },
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
            tab.BindValueChanged(t => showTab(t.NewValue), true);
            localUser = api.LocalUser.GetBoundCopy();
            localUser.BindValueChanged(_ => Schedule(onUserChanged));
            onUserChanged();
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);
            refresh();
        }

        private int userId => Math.Max(0, api.LocalUser.Value.Id);

        private void onUserChanged()
        {
            // The API also replaces the user object when refreshing its details; only a different account matters here.
            if (shownUserId == userId) return;
            shownUserId = userId;

            cancelAccountOperation();
            userHeader.Child = new SticksProfileUserHeader(userId > 0 ? api.LocalUser.Value : null, sharing = new SticksScoreSharingPanel
            {
                Connect = connect,
                Disconnect = disconnect,
                Cancel = cancelAccountOperation,
            });
            updateSharingState();
            refresh();
            // Retries any plays whose upload failed earlier, e.g. while offline.
            if (sharing.State == SticksSharingState.On) sync();
            resetLeaderboard();
        }

        private void showTab(SticksProfileTab value)
        {
            var (shown, hidden) = value == SticksProfileTab.TopPlays ? (topPlaysSection, leaderboardSection) : (leaderboardSection, topPlaysSection);
            hidden.Hide();
            shown.FadeInFromZero(300, Easing.OutQuint);
            if (value == SticksProfileTab.Leaderboard && !leaderboardRequested) loadLeaderboard();
        }

        #region Top plays

        private async void refresh()
        {
            if (disposed) return;
            int version = ++profileVersion;
            int user = userId;
            // Plays from your other computers come from the leaderboard, which is only contacted once you've opted in here.
            bool includeShared = user > 0 && sharingEnabled;
            if (topPlays.Length == 0) topPlaysSection.ShowLoading();

            SticksProfileScore[] local;
            try
            {
                local = await Task.Run(async () =>
                {
                    await SticksProfileTracker.WaitForPending().ConfigureAwait(false);
                    var all = new SticksProfileStore(storage).Read();
                    return SticksProfilePolicy.TopPlays(all, user, createClient(storage).WhitelistedSets);
                }).ConfigureAwait(false);
                await show(version, local, no_shared_plays, includeShared).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                showError(version, e);
                return;
            }

            if (!includeShared) return;

            var merged = local;
            var shared = no_shared_plays;
            try
            {
                var page = await createClient(storage).FindTopPlays(user).ConfigureAwait(false);
                if (page != null && page.Player.UserId == user)
                {
                    var plays = page.Entries.Where(p => p.Generation == SticksProfileScore.CurrentGeneration)
                                    .Select(p => (Play: p, Score: p.ToScore(user))).Where(p => p.Score.ScoreId != Guid.Empty).ToArray();
                    merged = SticksProfilePolicy.Merge(local, plays.Select(p => p.Score));
                    shared = plays.GroupBy(p => p.Score.ScoreId).ToDictionary(g => g.Key, g => g.First().Play);
                }
            }
            catch (Exception e)
            {
                // This computer's plays are shown either way.
                Logger.Log($"Could not load Sticks plays shared from your other computers: {e}");
            }

            try
            {
                if (local.Length > 0 && merged.Select(s => s.ScoreId).SequenceEqual(local.Select(s => s.ScoreId)))
                {
                    Schedule(() =>
                    {
                        if (!disposed && version == profileVersion) sharedPlays = shared;
                    });
                }
                else
                    await show(version, merged, shared, false).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                showError(version, e);
            }
        }

        private async Task show(int version, SticksProfileScore[] top, IReadOnlyDictionary<Guid, SticksLeaderboardClient.RemotePlay> shared, bool sharedPending)
        {
            var plays = await Task.Run(() => describe(top, shared, 0)).ConfigureAwait(false);
            var summary = SticksProfileSummary.From(top, rankFor);

            Schedule(() =>
            {
                if (disposed || version != profileVersion) return;
                topPlays = top;
                sharedPlays = shared;
                statistics.SetSummary(summary);
                topPlaysSection.Count = top.Length;
                topPlaysSection.ClearItems();
                addPlays(plays);
                if (top.Length > 0)
                    topPlaysSection.ClearNotice();
                // Plays from your other computers may still arrive.
                else if (sharedPending)
                    topPlaysSection.ShowLoading();
                else
                {
                    topPlaysSection.ShowNotice(new SticksProfileNotice(FontAwesome.Solid.Trophy, "No top plays yet",
                        "New No Mod passes on ranked osu! beatmaps and approved sets are tracked here. Plays from before tracking began aren't included."
                        + (sharing.State == SticksSharingState.Off ? " Turn on score sharing to include plays from your other computers." : "")));
                }
                topPlaysSection.SetMoreAvailable(top.Length > topPlaysSection.ItemCount);
            });
        }

        private void showError(int version, Exception e)
        {
            Logger.Error(e, "Could not open the Sticks profile.");
            Schedule(() =>
            {
                if (disposed || version != profileVersion) return;
                topPlaysSection.ShowNotice(new SticksProfileNotice(FontAwesome.Solid.ExclamationTriangle, "Couldn't load your top plays",
                    e is InvalidOperationException ? e.Message : "See the runtime log for details. Your saved scores are unaffected.", "Retry", refresh));
            });
        }

        private async void showMorePlays()
        {
            int version = profileVersion;
            var top = topPlays;
            var shared = sharedPlays;
            int start = topPlaysSection.ItemCount;
            try
            {
                var plays = await Task.Run(() => describe(top, shared, start)).ConfigureAwait(false);
                Schedule(() =>
                {
                    if (disposed || version != profileVersion) return;
                    addPlays(plays);
                    topPlaysSection.SetMoreAvailable(top.Length > topPlaysSection.ItemCount);
                });
            }
            catch (Exception e)
            {
                Logger.Error(e, "Could not load more Sticks top plays.");
                Schedule(() => topPlaysSection.SetMoreAvailable(true));
            }
        }

        private SticksProfilePlay[] describe(IReadOnlyList<SticksProfileScore> top, IReadOnlyDictionary<Guid, SticksLeaderboardClient.RemotePlay> shared, int start)
        {
            var downloads = SticksReplayStore.Downloads(storage);
            return top.Skip(start).Take(page_size).Select((entry, i) =>
            {
                Guid scoreId = entry.ScoreId;
                int beatmapId = entry.BeatmapId;
                shared.TryGetValue(scoreId, out var remote);
                ScoreInfo? info = scores.Query(s => s.ID == scoreId && !s.DeletePending);
                // A deleted score still has a known beatmap; show it rather than a bare ID.
                BeatmapInfo? map = info?.BeatmapInfo ?? beatmaps.QueryBeatmap(b => b.OnlineID == beatmapId);
                if (map?.BeatmapSet?.DeletePending == true) map = null;
                string fallback = $"Beatmap {beatmapId}";
                // Plays from your other computers may be on maps that aren't installed here; the leaderboard knows most titles.
                var title = map != null ? new RomanisableString(map.Metadata.TitleUnicode, map.Metadata.Title)
                    : remote?.Title != null ? new RomanisableString(remote.Title, remote.Title) : new RomanisableString(fallback, fallback);
                RomanisableString? artist = map != null ? new RomanisableString(map.Metadata.ArtistUnicode, map.Metadata.Artist)
                    : remote?.Artist != null ? new RomanisableString(remote.Artist, remote.Artist) : null;
                bool hasReplay = (info?.BeatmapInfo != null && storage.Exists($"ruleset-data/sticks/replays/{scoreId:N}.stkr"))
                                 || (remote != null && (remote.Replay?.Available == true || downloads.Contains(scoreId)));
                return new SticksProfilePlay(entry, start + i, title, artist, map?.DifficultyName ?? remote?.Difficulty, rankFor(entry.Accuracy), hasReplay);
            }).ToArray();
        }

        private void addPlays(IEnumerable<SticksProfilePlay> plays)
        {
            foreach (var play in plays)
                topPlaysSection.AddItem(new SticksProfilePlayRow(play, () => watch(play.Score.ScoreId)));
        }

        private void watch(Guid id)
        {
            try
            {
                var info = scores.Query(s => s.ID == id && !s.DeletePending);
                // A play from another computer, or one whose local copy is gone: watch it as the leaderboard has it.
                if ((info?.BeatmapInfo == null || !new SticksReplayStore(storage).Contains(id)) && sharedPlays.TryGetValue(id, out var shared))
                {
                    sharedReplays.Watch(shared, api.LocalUser.Value);
                    return;
                }

                if (info?.BeatmapInfo == null) throw new InvalidOperationException("This play's beatmap is no longer installed");
                var score = new Score { ScoreInfo = info };
                if (!new SticksReplayStore(storage).TryRestore(score)) throw new InvalidOperationException("This play has no readable local replay");
                startReplay(score, info.BeatmapInfo);
            }
            catch (Exception e)
            {
                Logger.Log($"Could not open the local Sticks replay: {e}");
                notifications?.Post(new SimpleErrorNotification
                {
                    Text = e is InvalidOperationException ? e.Message : "Could not open this replay; check that the beatmap and replay are still installed",
                });
            }
        }

        private void startReplay(Score score, BeatmapInfo map)
        {
            if (disposed || !this.IsCurrentScreen()) return;
            Beatmap.Value = beatmaps.GetWorkingBeatmap(map);
            this.Push(new ReplayPlayerLoader(score));
        }

        #endregion

        #region Leaderboard

        private void resetLeaderboard()
        {
            leaderboardVersion++;
            leaderboardOffset = 0;
            leaderboardRequested = false;
            leaderboardSection.ClearItems();
            leaderboardSection.ClearNotice();
            leaderboardSection.SetMoreAvailable(false);
            statistics.SetGlobalRank(null, sharingEnabled ? "Checking the leaderboard…" : unrankedReason);

            // The rank in the header needs the first page, but only fetch it unprompted for players who opted in.
            // A sync in progress resets the leaderboard again when it finishes, picking up any newly uploaded plays.
            if (tab.Value == SticksProfileTab.Leaderboard || sharingEnabled)
                loadLeaderboard();
        }

        private async void loadLeaderboard()
        {
            leaderboardRequested = true;
            SticksLeaderboardClient client = createClient(storage);
            if (!client.IsConfigured)
            {
                leaderboardSection.ShowNotice(new SticksProfileNotice(FontAwesome.Solid.Globe, "Leaderboard unavailable",
                    "This build of Sticks isn't connected to an online leaderboard. Your top plays and pp are still tracked on this computer."));
                return;
            }

            int version = leaderboardVersion;
            int offset = leaderboardOffset;
            int user = userId;
            if (offset == 0) leaderboardSection.ShowLoading();

            try
            {
                var players = await client.GetPlayers(offset).ConfigureAwait(false);
                Schedule(() =>
                {
                    if (disposed || version != leaderboardVersion) return;
                    leaderboardOffset = offset + players.Length;
                    foreach (var player in players)
                        leaderboardSection.AddItem(new SticksLeaderboardRow(player, user > 0 && player.UserId == user, () => openPlayer(player)));
                    leaderboardSection.SetMoreAvailable(players.Length == page_size);

                    if (offset > 0) return;
                    updateGlobalRank(players, user);
                    if (players.Length > 0)
                        leaderboardSection.ClearNotice();
                    else
                    {
                        leaderboardSection.ShowNotice(new SticksProfileNotice(FontAwesome.Solid.Users, "No players yet",
                            "Nobody has shared a Sticks score yet. Share yours to claim the top spot."));
                    }
                });
            }
            catch (Exception e)
            {
                Logger.Log($"Could not load the Sticks leaderboard: {e}");
                Schedule(() =>
                {
                    if (disposed || version != leaderboardVersion) return;
                    if (offset == 0)
                    {
                        statistics.SetGlobalRank(null, "The leaderboard couldn't be reached");
                        leaderboardSection.ShowNotice(new SticksProfileNotice(FontAwesome.Solid.ExclamationTriangle, "Couldn't load the leaderboard",
                            e is InvalidOperationException ? e.Message : "Check your connection and try again.", "Retry", () => loadLeaderboard()));
                    }
                    else
                        leaderboardSection.SetMoreAvailable(true);
                });
            }
        }

        private void openPlayer(SticksLeaderboardClient.Player player) => this.Push(new SticksPlayerScreen(player, createClient));

        private void updateGlobalRank(IReadOnlyCollection<SticksLeaderboardClient.Player> firstPage, int user)
        {
            var self = firstPage.FirstOrDefault(p => user > 0 && p.UserId == user);
            if (self != null)
                statistics.SetGlobalRank(self.Rank, $"{self.PP:N0}pp on the community leaderboard");
            else if (!sharingEnabled)
                statistics.SetGlobalRank(null, unrankedReason);
            else
                statistics.SetGlobalRank(null, firstPage.Count < page_size ? "Not ranked yet · sync your top plays" : $"Outside the top {page_size}");
        }

        private bool sharingEnabled => sharing.State is SticksSharingState.On or SticksSharingState.Syncing;

        private LocalisableString unrankedReason => sharing.State switch
        {
            SticksSharingState.Unavailable => "This build has no online leaderboard",
            SticksSharingState.Guest => "Log in to osu! and share your scores to get a global ranking",
            _ => "Share your scores to get a global ranking",
        };

        #endregion

        #region Score sharing

        private void updateSharingState(LocalisableString message = default, bool isError = false)
        {
            try
            {
                var client = createClient(storage);
                var state = !client.IsConfigured ? SticksSharingState.Unavailable
                    : userId == 0 ? SticksSharingState.Guest
                    : client.IsConnected(userId) ? SticksSharingState.On : SticksSharingState.Off;
                sharing.SetState(state, message, isError);
            }
            catch (Exception e)
            {
                Logger.Log($"Could not read Sticks leaderboard account settings: {e}");
                sharing.SetState(SticksSharingState.Off, "Could not read the account settings; local tracking is unaffected", true);
            }
        }

        private async void connect()
        {
            if (accountOperation != null) return;
            int user = userId;
            var operation = accountOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            sharing.SetState(SticksSharingState.Connecting);

            try
            {
                var client = createClient(storage);
                await client.Connect(user, url => Schedule(() => host.OpenUrlExternally(url)), operation.Token).ConfigureAwait(false);
                Schedule(() =>
                {
                    if (operation == accountOperation) sharing.SetState(SticksSharingState.Syncing);
                });
                await client.Sync(user).ConfigureAwait(false);
                finishAccountOperation(operation, "Connected · your top plays are shared");
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
                finishAccountOperation(operation, "Sign-in cancelled");
            }
            catch (Exception e)
            {
                Logger.Log($"Sticks leaderboard connection failed: {e}");
                finishAccountOperation(operation, e is InvalidOperationException ? e.Message : "Could not connect; local tracking is unaffected", true);
            }
        }

        private async void sync()
        {
            if (accountOperation != null) return;
            int user = userId;
            var operation = accountOperation = new CancellationTokenSource();
            sharing.SetState(SticksSharingState.Syncing);

            try
            {
                // Includes a play that is still being recorded.
                await SticksProfileTracker.WaitForPending().ConfigureAwait(false);
                await createClient(storage).Sync(user).ConfigureAwait(false);
                finishAccountOperation(operation, default);
            }
            catch (Exception e)
            {
                Logger.Log($"Sticks leaderboard sync failed: {e}");
                finishAccountOperation(operation, e is InvalidOperationException ? e.Message : "Couldn't reach the leaderboard · unsent plays will retry next time", true);
            }
        }

        private void disconnect()
        {
            if (accountOperation != null) return;
            try
            {
                createClient(storage).Disconnect();
                updateSharingState("Score sharing is off");
            }
            catch (Exception e)
            {
                Logger.Log($"Could not save Sticks account settings: {e}");
                updateSharingState("Could not save the disconnect; check storage permissions", true);
            }
            resetLeaderboard();
            refresh();
        }

        private void finishAccountOperation(CancellationTokenSource operation, LocalisableString message, bool isError = false) => Schedule(() =>
        {
            if (disposed || operation != accountOperation) return;
            accountOperation = null;
            operation.Dispose();
            updateSharingState(message, isError);
            resetLeaderboard();
            // Connecting or syncing can bring in plays from your other computers.
            refresh();
        });

        private void cancelAccountOperation() => accountOperation?.Cancel();

        #endregion

        public override bool OnExiting(ScreenExitEvent e)
        {
            bool blocked = base.OnExiting(e);
            if (!blocked) lifetime.Cancel();
            return blocked;
        }

        protected override void Dispose(bool isDisposing)
        {
            // ScreenStack disposes its screens and then its children, so this can run twice.
            if (!disposed)
            {
                disposed = true;
                lifetime.Cancel();
                lifetime.Dispose();
            }

            base.Dispose(isDisposing);
        }

        private partial class TabBar : CompositeDrawable
        {
            public Bindable<SticksProfileTab> Current { get; init; } = new Bindable<SticksProfileTab>();

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                InternalChildren = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Dark4 },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Padding = new MarginPadding { Horizontal = WaveOverlayContainer.HORIZONTAL_PADDING },
                        Children = new Drawable[]
                        {
                            new TabControlOverlayHeader<SticksProfileTab>.OverlayHeaderTabControl { Current = Current },
                            new TooltipSpriteText
                            {
                                Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight,
                                Text = "No Mod · ranked & approved maps", Font = OsuFont.GetFont(size: 12), Colour = colourProvider.Foreground1,
                                TooltipText = "Only No Mod passes on ranked osu! beatmaps and approved sets count toward Sticks pp",
                            },
                        },
                    },
                };
            }
        }
    }
}
