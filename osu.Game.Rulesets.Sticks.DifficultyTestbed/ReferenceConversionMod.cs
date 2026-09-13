using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Sticks.Beatmaps;

namespace osu.Game.Rulesets.Sticks.DifficultyTestbed;

/// <summary>
/// Selects an explicit historical strategy for comparisons without depending on the current
/// gameplay default or the meaning of a public mod. Separate types keep playable-map caches distinct.
/// </summary>
internal abstract class ReferenceConversionMod : Mod, IApplicableToBeatmapConverter
{
    private readonly SticksConversionMode mode;

    protected ReferenceConversionMod(SticksConversionMode mode) => this.mode = mode;

    public override string Name => $"Reference {mode}";
    public override string Acronym => $"REF-{mode}";
    public override LocalisableString Description => "Explicit historical converter strategy for local comparisons.";
    public override ModType Type => ModType.System;

    public void ApplyToBeatmapConverter(IBeatmapConverter converter)
    {
        if (converter is SticksBeatmapConverter sticks)
        {
            sticks.ConversionMode = mode;
            sticks.UseCounterpoint = false;
        }
    }

    public static Mod Create(SticksConversionMode mode) => mode switch
    {
        SticksConversionMode.Standard => new StandardReference(),
        SticksConversionMode.Parity => new ParityReference(),
        SticksConversionMode.Duet => new DuetReference(),
        SticksConversionMode.ParityDuet => new ParityDuetReference(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private sealed class StandardReference : ReferenceConversionMod
    {
        public StandardReference() : base(SticksConversionMode.Standard) { }
    }

    private sealed class ParityReference : ReferenceConversionMod
    {
        public ParityReference() : base(SticksConversionMode.Parity) { }
    }

    private sealed class DuetReference : ReferenceConversionMod
    {
        public DuetReference() : base(SticksConversionMode.Duet) { }
    }

    private sealed class ParityDuetReference : ReferenceConversionMod
    {
        public ParityDuetReference() : base(SticksConversionMode.ParityDuet) { }
    }
}
