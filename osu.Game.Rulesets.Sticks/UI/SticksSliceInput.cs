using System;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    internal static class SticksSliceInput
    {
        // Swept geometry avoids missing a small target between two input frames.
        public static bool TryCross(SticksSlice note, Vector2 from, Vector2 to, out double progress)
        {
            progress = 0;
            Vector2 movement = to - from;
            if (movement.LengthSquared < 0.00000001f)
                return false;
            float radius = SticksSlice.RADIUS / SticksPlayfield.GUIDE_RADIUS;
            float angle = note.Angle * MathF.PI / 180;
            var target = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            if (note.Direction == SticksSliceDirection.Neutral)
            {
                progress = Math.Clamp(Vector2.Dot(target - from, movement) / movement.LengthSquared, 0, 1);
                return progress > 0 && (from + movement * (float)progress - target).LengthSquared <= radius * radius;
            }

            // Directional notes require crossing the centre angle, not simply entering
            // the circle on the requested side. Radial flicks cannot satisfy this.
            if (from.LengthSquared < 0.25f || to.LengthSquared < 0.25f)
                return false;
            float fromAngle = MathF.Atan2(from.Y, from.X) * 180 / MathF.PI;
            float toAngle = MathF.Atan2(to.Y, to.X) * 180 / MathF.PI;
            float travel = SticksHitObject.DeltaAngle(fromAngle, toAngle);
            float required = SticksHitObject.DeltaAngle(fromAngle, note.Angle);
            if (Math.Abs(travel) < 0.001f || Math.Sign(travel) != (int)note.Direction)
                return false;
            progress = required / travel;
            return progress > 0 && progress <= 1
                   && (from + movement * (float)progress - target).LengthSquared <= radius * radius;
        }
    }
}
