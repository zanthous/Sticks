#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Sticks.Replays
{
    /// <summary>
    /// Retains recorded Sticks input only when lazer has retained the corresponding score.
    /// </summary>
    internal sealed class SticksReplayPersistence : IDisposable
    {
        private readonly SticksReplayStore store;
        private readonly Func<Guid, bool> isScoreSaved;
        private readonly Func<Guid, Action, IDisposable> subscribeToSavedScore;
        private readonly Dictionary<Guid, PendingReplay> pendingReplays = new Dictionary<Guid, PendingReplay>();
        private readonly object sync = new object();
        private readonly Action<Score, float>? scoreSaved;

        private bool disposed;

        public SticksReplayPersistence(SticksReplayStore store, RealmAccess realm, Action<Score, float>? scoreSaved = null)
            : this(
                store,
                id => realm.Run(r => r.All<ScoreInfo>().Any(s => s.ID == id && !s.DeletePending)),
                (id, onSaved) => realm.RegisterForNotifications(
                    r => r.All<ScoreInfo>().Where(s => s.ID == id && !s.DeletePending),
                    (scores, _) =>
                    {
                        if (scores.Any())
                            onSaved();
                    }), scoreSaved)
        {
        }

        internal SticksReplayPersistence(
            SticksReplayStore store,
            Func<Guid, bool> isScoreSaved,
            Func<Guid, Action, IDisposable> subscribeToSavedScore,
            Action<Score, float>? scoreSaved = null)
        {
            this.store = store;
            this.isScoreSaved = isScoreSaved;
            this.subscribeToSavedScore = subscribeToSavedScore;
            this.scoreSaved = scoreSaved;

            store.DeleteOrphans(isScoreSaved);
        }

        public void Track(Score score, float flickActivationThreshold = SticksInputTracker.DEFAULT_ACTIVATION_THRESHOLD, bool saveReplay = true)
        {
            if (saveReplay)
                SticksReplayStore.EnsureLocalIdentity(score);

            var pending = new PendingReplay(score, flickActivationThreshold, saveReplay);

            lock (sync)
            {
                if (disposed)
                    return;

                if (pendingReplays.Remove(score.ScoreInfo.ID, out PendingReplay? existing))
                    existing.Dispose();

                pendingReplays.Add(score.ScoreInfo.ID, pending);
            }

            try
            {
                pending.SetSubscription(subscribeToSavedScore(score.ScoreInfo.ID, () => completeIfSaved(score.ScoreInfo.ID)));
            }
            catch
            {
                removePending(score.ScoreInfo.ID)?.Dispose();
            }
        }

        private void completeIfSaved(Guid scoreId)
        {
            if (!checkScoreSaved(scoreId))
                return;

            PendingReplay? pending = removePending(scoreId);
            if (pending == null)
                return;

            save(pending);
            pending.Dispose();
        }

        private void save(PendingReplay pending)
        {
            if (pending.SaveReplay)
                store.Save(pending.Score, pending.FlickActivationThreshold);
            try { scoreSaved?.Invoke(pending.Score, pending.FlickActivationThreshold); }
            catch (Exception e) { Logger.Error(e, "Sticks could not record the local profile result."); }
        }

        private PendingReplay? removePending(Guid scoreId)
        {
            lock (sync)
            {
                if (!pendingReplays.Remove(scoreId, out PendingReplay? pending))
                    return null;

                return pending;
            }
        }

        private bool checkScoreSaved(Guid scoreId)
        {
            try
            {
                return isScoreSaved(scoreId);
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            PendingReplay[] pending;

            lock (sync)
            {
                if (disposed)
                    return;

                disposed = true;
                pending = pendingReplays.Values.ToArray();
                pendingReplays.Clear();
            }

            foreach (PendingReplay replay in pending)
            {
                // A notification can be one update behind the database write while the player is
                // exiting. Confirm synchronously before discarding the recording.
                if (checkScoreSaved(replay.Score.ScoreInfo.ID))
                    save(replay);

                replay.Dispose();
            }
        }

        private sealed class PendingReplay : IDisposable
        {
            public readonly Score Score;
            public readonly float FlickActivationThreshold;
            public readonly bool SaveReplay;

            private readonly object sync = new object();
            private IDisposable? subscription;
            private bool disposed;

            public PendingReplay(Score score, float flickActivationThreshold, bool saveReplay)
            {
                Score = score;
                FlickActivationThreshold = flickActivationThreshold;
                SaveReplay = saveReplay;
            }

            public void SetSubscription(IDisposable newSubscription)
            {
                lock (sync)
                {
                    if (!disposed)
                    {
                        subscription = newSubscription;
                        return;
                    }
                }

                newSubscription.Dispose();
            }

            public void Dispose()
            {
                IDisposable? toDispose;

                lock (sync)
                {
                    if (disposed)
                        return;

                    disposed = true;
                    toDispose = subscription;
                    subscription = null;
                }

                toDispose?.Dispose();
            }
        }
    }
}
