// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks
{
    public class SticksRuleset : Ruleset
    {
        public override string Description => "Sticks";

        public override string PlayingVerb => "Flicking sticks";

        public override string ShortName => "sticks";

        public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod> mods = null) =>
            new DrawableSticksRuleset(this, beatmap, mods);

        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new SticksBeatmapConverter(beatmap, this);

        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new SticksDifficultyCalculator(RulesetInfo, beatmap);

        public override ScoreProcessor CreateScoreProcessor() => new SticksScoreProcessor(this);

        public override HealthProcessor CreateHealthProcessor(double drainStartTime) => new SticksHealthProcessor();

        public override IConvertibleReplayFrame CreateConvertibleReplayFrame() => new SticksReplayFrame();

        public override IRulesetConfigManager CreateConfig(SettingsStore settings) => new SticksRulesetConfigManager(settings, RulesetInfo);

        public override RulesetSettingsSubsection CreateSettings() => new SticksSettingsSubsection(this);

        public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
        {
            ModType.Automation => new Mod[] { new SticksModAutoplay() },
            ModType.Conversion => new Mod[] { new SticksModDifficultyAdjust() },
            _ => Array.Empty<Mod>(),
        };

        public override IEnumerable<HitResult> GetValidHitResults() => new[]
        {
            HitResult.Great,
            HitResult.Ok,
            HitResult.Miss,
            HitResult.LargeTickHit,
            HitResult.LargeTickMiss,
            HitResult.IgnoreMiss,
        };

        public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) => new[]
        {
            new KeyBinding(InputKey.Space, SticksAction.Focus),
        };

        public override Drawable CreateIcon() => new SticksRulesetIcon();

        public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;
    }
}
