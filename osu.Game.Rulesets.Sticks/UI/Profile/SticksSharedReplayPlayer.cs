#nullable enable

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Sticks.Profiles;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Sticks.UI.Profile
{
    /// <summary>
    /// Watches a play as the community leaderboard has it, downloading its replay unless it is already on this computer.
    /// </summary>
    internal partial class SticksSharedReplayPlayer : Component
    {
        [Resolved] private Storage storage { get; set; } = null!;
        [Resolved] private BeatmapManager beatmaps { get; set; } = null!;
        [Resolved] private RulesetStore rulesets { get; set; } = null!;
        [Resolved(CanBeNull = true)] private INotificationOverlay? notifications { get; set; }
        [Resolved(CanBeNull = true)] private OsuGame? game { get; set; }

        private readonly Func<Storage, SticksLeaderboardClient> createClient;
        private readonly Action<Score, BeatmapInfo> start;
        private readonly Func<double, ScoreRank> rankFor = SticksProfileSummary.CreateRankLookup();
        private bool watching;

        public SticksSharedReplayPlayer(Func<Storage, SticksLeaderboardClient> createClient, Action<Score, BeatmapInfo> start)
        {
            this.createClient = createClient;
            this.start = start;
        }

        public async void Watch(SticksLeaderboardClient.RemotePlay play, APIUser owner)
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

                Schedule(() => startReplay(play, owner, scoreId, map));
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

        private void startReplay(SticksLeaderboardClient.RemotePlay play, APIUser owner, Guid scoreId, BeatmapInfo map)
        {
            watching = false;

            var score = new Score
            {
                ScoreInfo = new ScoreInfo(map, rulesets.GetRuleset("sticks"))
                {
                    ID = scoreId,
                    User = owner,
                    Accuracy = play.Accuracy, MaxCombo = play.MaxCombo, TotalScore = play.TotalScore,
                    Date = DateTimeOffset.FromUnixTimeMilliseconds(play.PlayedAt), Rank = rankFor(play.Accuracy),
                },
            };

            if (!new SticksReplayStore(storage).TryRestore(score) && !SticksReplayStore.Downloads(storage).TryRestore(score))
            {
                notifications?.Post(new SimpleErrorNotification { Text = "This replay couldn't be read" });
                return;
            }

            start(score, map);
        }
    }
}
