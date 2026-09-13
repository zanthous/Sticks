using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Resolves strongly overlapping directional heads into one readable two-stick target.
    /// The threshold describes the visible primary arcs, independently of absolute angles.
    /// </summary>
    internal static class SticksChordGeometry
    {
        internal const double TIME_TOLERANCE = 0.01;
        internal const float ANGLE_TOLERANCE = 0.001f;

        public static bool IsDirectionalPair(SticksHitObject first, SticksHitObject second) =>
            first is not SticksClick && second is not SticksClick && first.Side != second.Side;

        public static bool ShouldStack(float firstAngle, float firstSpan, float secondAngle, float secondSpan)
        {
            if (!float.IsFinite(firstAngle) || !float.IsFinite(secondAngle)
                || !float.IsFinite(firstSpan) || !float.IsFinite(secondSpan) || firstSpan <= 0 || secondSpan <= 0)
                return false;

            float firstHalf = Math.Clamp(firstSpan / 2, 0.5f, 180);
            float secondHalf = Math.Clamp(secondSpan / 2, 0.5f, 180);
            float separation = Math.Abs(SticksHitObject.DeltaAngle(firstAngle, secondAngle));
            float overlap = Math.Max(0, Math.Min(firstHalf, separation + secondHalf) - Math.Max(-firstHalf, separation - secondHalf));
            // Half of the narrower full arc. A shared boundary is included, with
            // only float serialization noise tolerated around the comparison.
            return overlap + ANGLE_TOLERANCE >= Math.Min(firstHalf, secondHalf);
        }

        public static float SharedAngle(float first, float second, float sourceHeading)
        {
            first = SticksHitObject.NormaliseAngle(first);
            second = SticksHitObject.NormaliseAngle(second);
            float delta = SticksHitObject.DeltaAngle(first, second);
            if (Math.Abs(Math.Abs(delta) - 180) > ANGLE_TOLERANCE)
                return SticksHitObject.NormaliseAngle(first + delta / 2);

            // Antipodal proposals have two circular midpoints. Prefer the one
            // nearest the original shared target; a tie goes clockwise from that
            // heading. Neither decision depends on object enumeration order.
            float a = SticksHitObject.NormaliseAngle(first + 90);
            float b = SticksHitObject.NormaliseAngle(first - 90);
            float distanceA = Math.Abs(SticksHitObject.DeltaAngle(sourceHeading, a));
            float distanceB = Math.Abs(SticksHitObject.DeltaAngle(sourceHeading, b));
            if (Math.Abs(distanceA - distanceB) > ANGLE_TOLERANCE)
                return distanceA < distanceB ? a : b;
            return SticksHitObject.DeltaAngle(sourceHeading, a) >= 0 ? a : b;
        }

        public static void ResolveReadableChords(IEnumerable<SticksHitObject> hitObjects, float fullHitAngle)
        {
            SticksHitObject[] ordered = hitObjects.OrderBy(note => note.StartTime).ToArray();
            for (int first = 0; first < ordered.Length;)
            {
                int end = first + 1;
                while (end < ordered.Length && ordered[end].StartTime - ordered[first].StartTime < TIME_TOLERANCE)
                    end++;
                if (end - first == 2 && IsDirectionalPair(ordered[first], ordered[first + 1])
                    && ShouldStack(ordered[first].Angle, fullHitAngle, ordered[first + 1].Angle, fullHitAngle))
                {
                    float angle = SharedAngle(ordered[first].Angle, ordered[first + 1].Angle, ordered[first].Angle);
                    ordered[first].Angle = angle;
                    ordered[first + 1].Angle = angle;
                }
                first = end;
            }
        }
    }
}
