using System;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Edit
{
    /// <summary>
    /// Maps the shared gameplay ring to editor positions. Placement uses the two sides
    /// of that ring to choose a hand, without putting the notes on separate circles.
    /// </summary>
    public static class SticksEditorCoordinates
    {
        public const float LANE_INPUT_PADDING = 34;
        public const float BOTH_STICKS_MIN_RADIUS = SticksPlayfield.OUTER_RADIUS + LANE_INPUT_PADDING;
        public const float MAX_PLACEMENT_RADIUS = 360;

        public static readonly Vector2 Centre = new Vector2(SticksPlayfield.SIZE / 2);

        public static bool TryGetPlacement(Vector2 localPosition, out StickSide side, out float angle)
            => TryGetPlacement(localPosition, out side, out angle, out _);

        public static bool TryGetPlacement(Vector2 localPosition, out StickSide side, out float angle, out bool bothSticks)
        {
            Vector2 delta = localPosition - Centre;
            float radius = delta.Length;

            side = Math.Abs(radius - SticksPlayfield.OUTER_RADIUS) <= Math.Abs(radius - SticksPlayfield.INNER_RADIUS)
                ? StickSide.Left
                : StickSide.Right;
            bothSticks = radius > BOTH_STICKS_MIN_RADIUS;
            bool validAngle = TryGetAngle(localPosition, out angle);

            return validAngle && radius >= SticksPlayfield.INNER_RADIUS - LANE_INPUT_PADDING
                   && radius <= MAX_PLACEMENT_RADIUS;
        }

        public static Vector2 PositionFor(SticksHitObject hitObject) =>
            PositionFor(hitObject.Side, hitObject.Angle);

        public static Vector2 PositionFor(StickSide side, float angle) =>
            SticksPlayfield.PointAt(angle, SticksPlayfield.GUIDE_RADIUS);

        public static float RadiusAt(double time, double hitTime, double approachDuration) =>
            SticksPlayfield.GUIDE_RADIUS * SticksPlayfield.CenterOutProgressAt(time, hitTime, approachDuration);

        public static float AngleAt(SticksHitObject hitObject, double time) => hitObject is SticksSlider slider
            ? slider.AngleAt(Math.Clamp(time, slider.StartTime, slider.EndTime))
            : hitObject.Angle;

        public static Vector2 PositionFor(SticksHitObject hitObject, double time) =>
            SticksPlayfield.PointAt(AngleAt(hitObject, time), RadiusAt(time, hitObject.StartTime, hitObject.ApproachDuration));

        public static bool TryGetAngle(Vector2 localPosition, out float angle)
        {
            Vector2 delta = localPosition - Centre;
            float radius = delta.Length;
            angle = SticksHitObject.NormaliseAngle(MathF.Atan2(delta.Y, delta.X) * 180 / MathF.PI);
            return float.IsFinite(radius) && radius >= 8
                   && radius <= MAX_PLACEMENT_RADIUS;
        }

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
