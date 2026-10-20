#nullable enable

using System;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Sticks.Difficulty;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Sticks.Profiles
{
    internal sealed class SticksProfileTracker
    {
        private readonly Storage storage;
        private readonly BeatmapManager beatmaps;
        private static readonly object queueLock = new object();
        private static Task pending = Task.CompletedTask;

        public SticksProfileTracker(Storage storage, BeatmapManager beatmaps)
        {
            this.storage = storage;
            this.beatmaps = beatmaps;
        }

        public void Record(Score score, float activationThreshold)
        {
            // Called only for a new, saved SoloPlayer result. Never enumerate old scores.
            if (!score.ScoreInfo.Passed) return;
            ScoreInfo snapshot = Snapshot(score.ScoreInfo);
            if (!snapshot.Passed || snapshot.BeatmapInfo?.Ruleset.OnlineID != 0
                || !SticksProfilePolicy.TryEncodeMods(snapshot.Mods, out _)) return;
            WorkingBeatmap working = beatmaps.GetWorkingBeatmap(snapshot.BeatmapInfo);
            lock (queueLock)
                pending = pending.ContinueWith(previous =>
                {
                    try
                    {
                        var calculator = new SticksDifficultyCalculator(new SticksRuleset().RulesetInfo, working);
                        var attributes = calculator.Calculate(snapshot.Mods);
                        double pp = new SticksPerformanceCalculator().Calculate(snapshot, attributes).Total;
                        var record = SticksProfileScore.Capture(snapshot, pp, calculator.Version, activationThreshold);
                        if (record != null && new SticksProfileStore(storage).Append(record))
                            _ = SticksLeaderboardClient.TrySync(storage, record.UserId);
                    }
                    catch (Exception e) { Logger.Error(e, "Sticks profile calculation failed; gameplay and the saved score are unaffected."); }
                }, TaskScheduler.Default);
        }

        // The tracked score is the live gameplay instance. Lazer stamps the date only on the copy it imports,
        // so without this every result would fail validation as having been played in year 1.
        internal static ScoreInfo Snapshot(ScoreInfo live)
        {
            ScoreInfo snapshot = live.DeepClone();
            if (snapshot.Date == default) snapshot.Date = DateTimeOffset.Now;
            return snapshot;
        }

        public static Task WaitForPending()
        {
            lock (queueLock) return pending;
        }
    }
}
