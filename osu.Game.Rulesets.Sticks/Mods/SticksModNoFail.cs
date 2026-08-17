using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModNoFail : ModNoFail, IApplicableToHUD, IApplicableToHealthProcessor
    {
        /// <summary>
        /// Low-health edge warnings share lazer's health-display visibility switch. No Fail has
        /// no actionable low-health state, so keep both the warning and health display hidden.
        /// </summary>
        void IApplicableToHUD.ApplyToHUD(HUDOverlay overlay) => overlay.ShowHealthBar.Value = false;

        /// <summary>
        /// Lazer requires a health processor to exist, but Sticks No Fail has no use for a
        /// changing health value. Locking its minimum to full prevents both continuous drain
        /// and judgement penalties from ever subtracting health.
        /// </summary>
        void IApplicableToHealthProcessor.ApplyToHealthProcessor(HealthProcessor healthProcessor)
        {
            healthProcessor.Health.Value = healthProcessor.Health.MaxValue;
            healthProcessor.Health.MinValue = healthProcessor.Health.MaxValue;
        }
    }
}
