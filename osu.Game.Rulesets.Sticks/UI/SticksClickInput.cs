namespace osu.Game.Rulesets.Sticks.UI
{
    /// <summary>Track each button independently so a held shoulder cannot swallow a stick press.</summary>
    internal sealed class SticksClickInput
    {
        private readonly SticksStrumButtonState shoulder = new SticksStrumButtonState();
        private readonly SticksStrumButtonState trigger = new SticksStrumButtonState();
        private readonly SticksStrumButtonState stick = new SticksStrumButtonState();

        public bool Update(bool shoulderPressed, bool triggerPressed, bool stickPressed, bool strum)
        {
            bool shoulderEdge = shoulder.Update(shoulderPressed);
            bool triggerEdge = trigger.Update(triggerPressed);
            bool stickEdge = stick.Update(stickPressed);
            return stickEdge || (!strum && (shoulderEdge || triggerEdge));
        }
    }
}
