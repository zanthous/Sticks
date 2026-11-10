using osuTK;

namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>
    /// Converts a stick position exactly as the controller reports it into the stick units used by
    /// gesture detection and stored in replays.
    /// </summary>
    /// <remarks>
    /// Stick units date from when osu! applied its joystick deadzone before Sticks saw the stick.
    /// Every replay is recorded in them and judged again when watched, so the units and the rules
    /// applied to them cannot change. Only the way a real stick position reaches them has: it now
    /// depends on the distance from the centre alone, so every direction behaves the same.
    /// <list type="bullet">
    /// <item>A flick triggers at the real activation setting.</item>
    /// <item>The stick recharges <see cref="PHYSICAL_RECHARGE_OFFSET"/> below that setting. This is
    /// where it recharged on average across directions under osu!'s default deadzone.</item>
    /// <item>The innermost <see cref="DEAD_CENTRE"/> reads as centred, so a resting stick is still.</item>
    /// </list>
    /// </remarks>
    internal static class SticksStickResponse
    {
        /// <summary>
        /// Real distance from the centre within which a stick reads as centred.
        /// </summary>
        public const float DEAD_CENTRE = 0.1f;

        /// <summary>
        /// Real distance below the activation setting at which a stick recharges.
        /// </summary>
        public const float PHYSICAL_RECHARGE_OFFSET = 0.24f;

        // Recharge points for every activation setting lie in this band of real distance. Stick
        // units place them RECHARGE_OFFSET below the setting, so the band is moved by the difference.
        private const float recharge_band_start = SticksInputTracker.MIN_ACTIVATION_THRESHOLD - PHYSICAL_RECHARGE_OFFSET;
        private const float recharge_band_end = SticksInputTracker.MAX_ACTIVATION_THRESHOLD - PHYSICAL_RECHARGE_OFFSET;
        private const float recharge_band_shift = SticksInputTracker.RECHARGE_OFFSET - PHYSICAL_RECHARGE_OFFSET;

        // From the lowest activation setting outwards a stick unit is the real distance.
        private const float real_from = SticksInputTracker.MIN_ACTIVATION_THRESHOLD;

        public static Vector2 FromRaw(Vector2 raw)
        {
            float distance = raw.Length;

            // Also leaves a stick reported beyond its nominal range untouched, as it always was.
            if (distance >= real_from)
                return raw;

            float units = FromRaw(distance);
            return units == 0 ? Vector2.Zero : raw * (units / distance);
        }

        public static float FromRaw(float distance)
        {
            if (!(distance > DEAD_CENTRE))
                return 0;

            if (distance >= real_from)
                return distance;

            if (distance >= recharge_band_end)
                return between(distance, recharge_band_end, recharge_band_end - recharge_band_shift, real_from, real_from);

            if (distance >= recharge_band_start)
                return distance - recharge_band_shift;

            return between(distance, DEAD_CENTRE, 0, recharge_band_start, recharge_band_start - recharge_band_shift);
        }

        /// <summary>
        /// The real distance from the centre which reads as the given stick units.
        /// </summary>
        public static float ToRaw(float units)
        {
            if (!(units > 0))
                return 0;

            if (units >= real_from)
                return units;

            if (units >= recharge_band_end - recharge_band_shift)
                return between(units, recharge_band_end - recharge_band_shift, recharge_band_end, real_from, real_from);

            if (units >= recharge_band_start - recharge_band_shift)
                return units + recharge_band_shift;

            return between(units, 0, DEAD_CENTRE, recharge_band_start - recharge_band_shift, recharge_band_start);
        }

        private static float between(float value, float fromValue, float fromResult, float toValue, float toResult) =>
            fromResult + (value - fromValue) / (toValue - fromValue) * (toResult - fromResult);
    }
}
