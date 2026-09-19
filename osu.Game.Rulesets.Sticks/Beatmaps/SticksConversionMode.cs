namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Internal base planning strategies, retaining their historical names for conversion audits.
    /// The default arrangement pass is controlled separately by <see cref="SticksBeatmapConverter.UseCounterpoint"/>.
    /// </summary>
    public enum SticksConversionMode
    {
        // Historical strategies retained for conversion audits.
        Standard,
        Parity,

        // Current base conversion and the Parity variant of that base.
        Duet,
        ParityDuet,
    }
}
