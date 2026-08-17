#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Database;
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

        private bool disposed;

        public SticksReplayPersistence(SticksReplayStore store, RealmAccess realm)
            : this(
                store,
                id => realm.Run(r => r.All<ScoreInfo>().Any(s => s.ID == id && !s.DeletePending)),
                (id, onSaved) => realm.RegisterForNotifications(
                    r => r.All<ScoreInfo>().Where(s => s.ID == id && !s.DeletePending),
                    (scores, _) =>
                    {
                        if (scores.Any())
                            onSaved();
                    }))
        {
        }

        internal SticksReplayPersistence(
            SticksReplayStore store,
            Func<Guid, bool> isScoreSaved,
            Func<Guid, Action, IDisposable> subscribeToSavedScore)
        {
            this.store = store;
            this.isScoreSaved = isScoreSaved;
            this.subscribeToSavedScore = subscribeToSavedScore;

            store.DeleteOrphans(isScoreSaved);
        }

        public void Track(Score score)
        {
            SticksReplayStore.EnsureLocalIdentity(score);

            var pending = new PendingReplay(score);

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

            store.Save(pending.Score);
            pending.Dispose();
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
                    store.Save(replay.Score);

                replay.Dispose();
            }
        }

        private sealed class PendingReplay : IDisposable
        {
            public readonly Score Score;

            private readonly object sync = new object();
            private IDisposable? subscription;
            private bool disposed;

            public PendingReplay(Score score)
            {
                Score = score;
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
