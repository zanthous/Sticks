// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Scoring.Legacy;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Configuration;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks
{
    public class SticksRuleset : Ruleset
    {
        /// <summary>
        /// Stock lazer currently gates its legacy-backed editor save and undo implementation on
        /// <see cref="ILegacyRuleset"/>, while external rulesets must remain outside the four
        /// server-side legacy IDs. The ruleset store must therefore discover this non-legacy
        /// bootstrap type, but normal instantiation is redirected to the nested editor-compatible
        /// implementation below.
        /// </summary>
        public SticksRuleset()
        {
            // Keep the database-facing identity unambiguously custom. In particular, never claim
            // mode 0 here: doing so would collide with osu!standard during ruleset discovery.
            RulesetInfo.OnlineID = -1;
            RulesetInfo.InstantiationInfo = editorCompatibleInstantiationInfo;
        }

        private static string editorCompatibleInstantiationInfo =>
            $"{typeof(EditorCompatibleSticksRuleset).FullName}, {typeof(SticksRuleset).Assembly.GetName().Name}";

        public override string Description => "Sticks";

        public override string PlayingVerb => "Flicking sticks";

        public override string ShortName => "sticks";

        public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod> mods = null) =>
            new DrawableSticksRuleset(this, beatmap, mods);

        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new SticksBeatmapConverter(beatmap, this);

        public override IBeatmapProcessor CreateBeatmapProcessor(IBeatmap beatmap) => SticksLegacyEditorBridge.TryCreateProcessor(beatmap);

        public override HitObjectComposer CreateHitObjectComposer() => new SticksHitObjectComposer(this);

        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new SticksDifficultyCalculator(RulesetInfo, beatmap);

        public override ScoreProcessor CreateScoreProcessor() => new SticksScoreProcessor(this);

        public override HealthProcessor CreateHealthProcessor(double drainStartTime) => new SticksHealthProcessor(drainStartTime);

        public override ScoreMultiplierCalculator CreateScoreMultiplierCalculator(ScoreMultiplierContext context) =>
            new SticksScoreMultiplierCalculator(context);

        public override IConvertibleReplayFrame CreateConvertibleReplayFrame() => new SticksReplayFrame();

        public override IRulesetConfigManager CreateConfig(SettingsStore settings) => new SticksRulesetConfigManager(settings, RulesetInfo);

        public override RulesetSettingsSubsection CreateSettings() => new SticksSettingsSubsection(this);

        public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
        {
            ModType.Automation => new Mod[] { new SticksModAutoplay() },
            ModType.DifficultyReduction => new Mod[]
            {
                new SticksModEasy(),
                new SticksModNoFail(),
                new SticksModHalfTime(),
            },
            ModType.DifficultyIncrease => new Mod[]
            {
                new SticksModHardRock(),
                new MultiMod(new SticksModSuddenDeath(), new SticksModPerfect()),
                new SticksModDoubleTime(),
            },
            ModType.Conversion => new Mod[] { new SticksModDifficultyAdjust() },
            _ => Array.Empty<Mod>(),
        };

        public override IEnumerable<HitResult> GetValidHitResults() => new[]
        {
            HitResult.Great,
            HitResult.Ok,
            HitResult.Meh,
            HitResult.Miss,
            HitResult.LargeTickHit,
            HitResult.LargeTickMiss,
            HitResult.SliderTailHit,
            HitResult.IgnoreHit,
            HitResult.IgnoreMiss,
        };

        public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) => new[]
        {
            new KeyBinding(InputKey.Space, SticksAction.Focus),
        };

        public override Drawable CreateIcon() => new SticksRulesetIcon();

        public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

        /// <summary>
        /// Instantiated from a persisted Sticks <see cref="RulesetInfo"/>. Nested public types are
        /// intentionally not returned by <see cref="Type.IsPublic"/>, so the assembly ruleset
        /// scanner continues to discover only <see cref="SticksRuleset"/> and Realm registration
        /// remains on the normal external-ruleset path.
        /// </summary>
        public sealed class EditorCompatibleSticksRuleset : SticksRuleset, ILegacyRuleset
        {
            int ILegacyRuleset.LegacyID => 0;

            ILegacyScoreSimulator ILegacyRuleset.CreateLegacyScoreSimulator() => new SticksLegacyScoreSimulator();
        }
    }
}
