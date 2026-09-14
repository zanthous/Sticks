using System;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// Resolves one visual accent for an exact two-hand stack, even when the first
    /// drawable has expired before the second hand is judged. No score is changed.
    /// </summary>
    internal sealed class SticksPerfectHitTracker
    {
        private const int capacity = 64;
        private readonly PendingStack[] pending = new PendingStack[capacity];
        private int next;

        public bool TryResolve(SticksHitObject source, HitResult result, SticksHitObject partner, out bool bothSticks)
        {
            bothSticks = false;
            if (source is SticksClick)
                return false;

            for (int i = 0; i < pending.Length; i++)
            {
                PendingStack first = pending[i];
                if (first.Partner != source)
                    continue;

                pending[i] = default;
                bothSticks = true;
                return first.Perfect && result == HitResult.Perfect && IsExactStack(first.Source, source);
            }

            if (partner != null && IsExactStack(source, partner))
            {
                pending[next] = new PendingStack(source, partner, result == HitResult.Perfect);
                next = (next + 1) % pending.Length;
                return false;
            }

            return result == HitResult.Perfect;
        }

        public void Clear()
        {
            Array.Clear(pending);
            next = 0;
        }

        public static bool IsExactStack(SticksHitObject a, SticksHitObject b) =>
            a != null && b != null && a.Side != b.Side && a is not SticksClick && b is not SticksClick
            && Math.Abs(a.StartTime - b.StartTime) < 0.01
            && Math.Abs(SticksHitObject.DeltaAngle(a.Angle, b.Angle)) < 0.01f
            && Math.Abs(a.PrimaryHitAngle - b.PrimaryHitAngle) < 0.01f;

        private readonly record struct PendingStack(SticksHitObject Source, SticksHitObject Partner, bool Perfect);
    }
}
