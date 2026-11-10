using System;
using System.Runtime.CompilerServices;
using osu.Framework.Input.Handlers.Joystick;
using osu.Framework.Platform;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.UI;

namespace osu.Game.Rulesets.Sticks
{
    /// <summary>
    /// Keeps osu!'s own joystick deadzone switched off while Sticks is reading the controller, so
    /// stick positions arrive exactly as the controller reports them.
    /// </summary>
    /// <remarks>
    /// osu! applies its deadzone to each axis separately before a ruleset sees the stick. That bends
    /// angles toward the four straight directions, makes a full diagonal push read short of the
    /// edge, and lets a player change how far the stick must really travel. Sticks applies
    /// <see cref="SticksStickResponse"/> to the untouched position instead.
    /// <para>
    /// The deadzone is osu!'s saved setting and other rulesets rely on it, so the player's value is
    /// remembered and put back once nothing in Sticks is reading the controller. It is remembered
    /// in the Sticks settings so that the next launch can put it back after a crash.
    /// </para>
    /// </remarks>
    internal sealed class SticksRawStickInput : IDisposable
    {
        public const float NOTHING_REMEMBERED = -1;

        private static readonly object sync = new object();
        private static readonly ConditionalWeakTable<GameHost, Readers> readers_by_host = new ConditionalWeakTable<GameHost, Readers>();

        // Used when the Sticks settings are unavailable. The value then lasts for this run only.
        private static float rememberedThisRun = NOTHING_REMEMBERED;

        private readonly GameHost host;
        private readonly SticksRulesetConfigManager config;
        private bool released;

        private SticksRawStickInput(GameHost host, SticksRulesetConfigManager config)
        {
            this.host = host;
            this.config = config;
        }

        public static SticksRawStickInput Acquire(GameHost host, IRulesetConfigCache configs)
        {
            lock (sync)
                readers_by_host.GetOrCreateValue(host).Count++;

            var reader = new SticksRawStickInput(host, configFrom(configs));
            reader.Enforce();
            return reader;
        }

        /// <summary>
        /// Switches the deadzone off again if it has been changed. Call this every frame while
        /// reading the controller, because the player can open the settings at any time.
        /// </summary>
        public void Enforce()
        {
            foreach (var handler in host.AvailableInputHandlers)
            {
                // Writing to a locked setting throws, and this runs every frame.
                if (handler is not JoystickHandler joystick || joystick.DeadzoneThreshold.Value == 0 || joystick.DeadzoneThreshold.Disabled)
                    continue;

                remember(config, joystick.DeadzoneThreshold.Value);
                joystick.DeadzoneThreshold.Value = 0;
            }
        }

        /// <summary>
        /// Puts the player's deadzone back if Sticks switched it off and nothing is reading the
        /// controller now, which is the state a crash during play leaves behind.
        /// </summary>
        public static void RestoreIfUnused(GameHost host, IRulesetConfigCache configs)
        {
            lock (sync)
            {
                if (readers_by_host.GetOrCreateValue(host).Count > 0)
                    return;
            }

            restore(host, configFrom(configs));
        }

        // Drawable disposal runs in the background, and the deadzone is bound to loaded settings
        // controls, so it is put back on the update thread.
        public void Dispose() => host.UpdateThread.Scheduler.Add(release, forceScheduled: false);

        private void release()
        {
            lock (sync)
            {
                // Drawable cleanup can run more than once.
                if (released)
                    return;

                released = true;

                if (--readers_by_host.GetOrCreateValue(host).Count > 0)
                    return;
            }

            restore(host, config);
        }

        private static void restore(GameHost host, SticksRulesetConfigManager config)
        {
            float value = recall(config);

            if (!(value > 0))
                return;

            foreach (var handler in host.AvailableInputHandlers)
            {
                if (handler is not JoystickHandler joystick)
                    continue;

                // Keep the value for a later attempt rather than lose it.
                if (joystick.DeadzoneThreshold.Disabled)
                    return;

                // Any other value was chosen by the player after Sticks switched the deadzone off.
                if (joystick.DeadzoneThreshold.Value == 0)
                    joystick.DeadzoneThreshold.Value = value;
            }

            remember(config, NOTHING_REMEMBERED);
        }

        private static SticksRulesetConfigManager configFrom(IRulesetConfigCache configs)
        {
            try
            {
                return configs?.GetConfigFor(new SticksRuleset()) as SticksRulesetConfigManager;
            }
            catch (InvalidOperationException)
            {
                // The settings have not loaded yet, or Sticks is not registered in this context.
                return null;
            }
        }

        private static float recall(SticksRulesetConfigManager config) =>
            config?.Get<float>(SticksRulesetSetting.OsuJoystickDeadzone) ?? rememberedThisRun;

        private static void remember(SticksRulesetConfigManager config, float value)
        {
            if (config != null)
                config.SetValue(SticksRulesetSetting.OsuJoystickDeadzone, value);
            else
                rememberedThisRun = value;
        }

        private sealed class Readers
        {
            public int Count;
        }
    }
}
