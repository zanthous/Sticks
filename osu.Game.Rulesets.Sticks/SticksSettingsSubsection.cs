// Copyright (c) Zanthous. Licensed under the MIT Licence.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks
{
    public partial class SticksSettingsSubsection : RulesetSettingsSubsection
    {
        protected override LocalisableString Header => "Sticks";

        public SticksSettingsSubsection(SticksRuleset ruleset)
            : base(ruleset)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var config = (SticksRulesetConfigManager)Config;

            Children = new Drawable[]
            {
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = "Approach rate",
                    Current = config.GetBindable<float>(SticksRulesetSetting.ApproachRate),
                    KeyboardStep = 0.5f,
                    LabelFormat = value => $"AR {value:0.0} ({SticksHitObject.ApproachDurationFor(value):0} ms)",
                }),
            };
        }
    }
}
