// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks.Mods
{
    public class SticksModDifficultyAdjust : Mod, IApplicableToHitObject, IApplicableToBeatmapConverter, IApplicableToDrawableRuleset<SticksHitObject>
    {
        public override string Name => "Difficulty Adjust";

        public override string Acronym => "DA";

        public override LocalisableString Description => "Experiment with Sticks gameplay dimensions and converter features.";

        public override ModType Type => ModType.Conversion;

        public override bool RequiresConfiguration => true;

        [SettingSource("Primary hit angle", "Full angular width awarded a 300.", 0)]
        public BindableFloat PrimaryHitAngle { get; } = new BindableFloat(SticksHitObject.VISIBLE_ARC_SPAN)
        {
            MinValue = 4,
            MaxValue = 90,
            Precision = 1,
        };

        [SettingSource("Secondary hit angle", "Additional full angular width awarded a 100.", 1)]
        public BindableFloat SecondaryHitAngle { get; } = new BindableFloat(SticksHitObject.VISIBLE_ARC_SPAN)
        {
            MinValue = 0,
            MaxValue = 90,
            Precision = 1,
        };

        [SettingSource("Disable reversals", "Convert repeated sliders into continuous one-way motion.", 2)]
        public BindableBool DisableReversals { get; } = new BindableBool();

        [SettingSource("Show cursor trails", "Show a short trail behind both stick cursors.", 3)]
        public BindableBool ShowCursorTrails { get; } = new BindableBool();

        [SettingSource("Show synced-note links", "Draw a subtle tether between flicks which must be played together. Enabled by default; toggle off to hide it.", 4)]
        public BindableBool ShowSyncedNoteLinks { get; } = new BindableBool(true);

        [SettingSource("Chord link style", "Choose whether chord notes connect to each other or independently to the playfield centre.", 5)]
        public Bindable<ChordLinkStyle> ChordLinkStyle { get; } = new Bindable<ChordLinkStyle>(global::osu.Game.Rulesets.Sticks.Objects.ChordLinkStyle.ToCentre);

        public void ApplyToHitObject(HitObject hitObject)
        {
            if (hitObject is SticksHitObject sticksObject)
                applyAngles(sticksObject);
        }

        public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
        {
            if (beatmapConverter is SticksBeatmapConverter sticksConverter)
                sticksConverter.DisableReversals = DisableReversals.Value;
        }

        public void ApplyToDrawableRuleset(DrawableRuleset<SticksHitObject> drawableRuleset)
        {
            if (drawableRuleset.Playfield is SticksPlayfield playfield)
                playfield.ShowCursorTrails = ShowCursorTrails.Value;
        }

        private void applyAngles(SticksHitObject hitObject)
        {
            hitObject.PrimaryHitAngle = PrimaryHitAngle.Value;
            hitObject.SecondaryHitAngle = SecondaryHitAngle.Value;
            hitObject.ShowSyncedNoteLink = ShowSyncedNoteLinks.Value;
            hitObject.ChordLinkStyle = ChordLinkStyle.Value;

            foreach (HitObject nested in hitObject.NestedHitObjects)
            {
                if (nested is SticksHitObject sticksNested)
                    applyAngles(sticksNested);
            }
        }
    }
}
