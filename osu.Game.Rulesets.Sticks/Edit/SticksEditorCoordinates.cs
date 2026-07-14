// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit
{
    /// <summary>
    /// Shared mapping between editor pointer positions and Sticks' two circular lanes.
    /// </summary>
    public static class SticksEditorCoordinates
    {
        public const float LANE_INPUT_PADDING = 34;

        public static readonly Vector2 Centre = new Vector2(SticksPlayfield.SIZE / 2);

        public static bool TryGetPlacement(Vector2 localPosition, out StickSide side, out float angle)
        {
            Vector2 delta = localPosition - Centre;
            float radius = delta.Length;

            side = Math.Abs(radius - SticksPlayfield.OUTER_RADIUS) <= Math.Abs(radius - SticksPlayfield.INNER_RADIUS)
                ? StickSide.Left
                : StickSide.Right;
            angle = SticksHitObject.NormaliseAngle(MathF.Atan2(delta.Y, delta.X) * 180 / MathF.PI);

            return radius >= SticksPlayfield.INNER_RADIUS - LANE_INPUT_PADDING
                   && radius <= SticksPlayfield.OUTER_RADIUS + LANE_INPUT_PADDING;
        }

        public static Vector2 PositionFor(SticksHitObject hitObject) =>
            SticksPlayfield.PointAt(hitObject.Angle, SticksPlayfield.RadiusFor(hitObject.Side));

        public static Vector2 PositionFor(StickSide side, float angle) =>
            SticksPlayfield.PointAt(angle, SticksPlayfield.RadiusFor(side));

        public static float SnapAngle(float angle, float increment = 15)
        {
            return SticksHitObject.NormaliseAngle(SnapAngleOffset(angle, increment));
        }

        public static float SnapAngleOffset(float angleOffset, float increment = 15)
        {
            if (!float.IsFinite(increment) || increment <= 0)
                throw new ArgumentOutOfRangeException(nameof(increment));

            return MathF.Round(angleOffset / increment, MidpointRounding.AwayFromZero) * increment;
        }
    }
}
