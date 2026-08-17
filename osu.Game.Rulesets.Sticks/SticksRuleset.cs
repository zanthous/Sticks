using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Localisation;
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
using osu.Game.Rulesets.Sticks.Difficulty;
using osu.Game.Rulesets.Sticks.Edit;
using osu.Game.Rulesets.Sticks.Edit.Setup;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Replays;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Rulesets.Sticks.UI;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Edit.Setup;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Utils;
using osuTK;

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

        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new SticksBeatmapConverter(beatmap, this)
        {
            DisableBeatmapHitsounds = SticksRulesetConfigManager.DisableBeatmapHitsoundsForConversion,
        };

        public override IBeatmapProcessor CreateBeatmapProcessor(IBeatmap beatmap) => SticksLegacyEditorBridge.TryCreateProcessor(beatmap);

        public override HitObjectComposer CreateHitObjectComposer() => new SticksHitObjectComposer(this);

        public override IEnumerable<Drawable> CreateEditorSetupSections() =>
        [
            new MetadataSection(),
            new SticksDifficultySection(),
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(SetupScreen.SPACING),
                Children = new Drawable[]
                {
                    new ResourcesSection
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                    new ColoursSection
                    {
                        RelativeSizeAxes = Axes.X,
                    },
                },
            },
            new DesignSection(),
        ];

        public override IBeatmapVerifier CreateBeatmapVerifier() => new SticksBeatmapVerifier();

        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new SticksDifficultyCalculator(RulesetInfo, beatmap);

        public override PerformanceCalculator CreatePerformanceCalculator() => new SticksPerformanceCalculator();

        public override ScoreProcessor CreateScoreProcessor() => new SticksScoreProcessor(this);

        public override HealthProcessor CreateHealthProcessor(double drainStartTime) => new SticksHealthProcessor(drainStartTime);

        public override ScoreMultiplierCalculator CreateScoreMultiplierCalculator(ScoreMultiplierContext context) =>
            new SticksScoreMultiplierCalculator(context);

        public override IConvertibleReplayFrame CreateConvertibleReplayFrame() => new SticksReplayFrame();

        public override IRulesetConfigManager CreateConfig(SettingsStore settings) => new SticksRulesetConfigManager(settings, RulesetInfo);

        public override RulesetSettingsSubsection CreateSettings() => new SticksSettingsSubsection(this);

        public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
        {
            ModType.Automation => new Mod[]
            {
                new SticksModAutoplay(),
                new SticksModRelax(),
            },
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

        public override LocalisableString GetDisplayNameForHitResult(HitResult result) => result switch
        {
            HitResult.LargeTickHit => "tracking checkpoint",
            HitResult.SliderTailHit => "object end",
            _ => base.GetDisplayNameForHitResult(result),
        };

        public override StatisticItem[] CreateStatisticsForScore(ScoreInfo score, IBeatmap playableBeatmap)
        {
            SticksScoreStatistics.Summary summary = SticksScoreStatistics.Calculate(score.HitEvents);

            return new[]
            {
                new StatisticItem("Performance Breakdown", () => new PerformanceBreakdownChart(score, playableBeatmap)
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                }),
                new StatisticItem("Timing Distribution", () => new HitEventTimingDistributionGraph(summary.TimingEvents)
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 250,
                }, true),
                new StatisticItem("Sticks Accuracy", () => new SimpleStatisticTable(2, new SimpleStatisticItem[]
                {
                    new AverageHitError(summary.TimingEvents),
                    new UnstableRate(summary.TimingEvents),
                    new SimpleStatisticItem<string>("Average angle error")
                    {
                        Value = formatDegrees(summary.AverageAngleError),
                    },
                    new SimpleStatisticItem<string>("95th percentile angle error")
                    {
                        Value = formatDegrees(summary.AngleError95thPercentile),
                    },
                    new SimpleStatisticItem<string>("Tracking completion")
                    {
                        Value = formatCompletion(summary.TrackingHits, summary.TrackingTotal),
                    },
                    new SimpleStatisticItem<string>("Tail completion")
                    {
                        Value = formatCompletion(summary.TailHits, summary.TailTotal),
                    },
                }), true),
            };
        }

        public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
        {
            IBeatmapDifficultyInfo original = beatmapInfo.Difficulty;
            BeatmapDifficulty adjusted = GetAdjustedDisplayDifficulty(beatmapInfo, mods);
            var difficultyAdjust = mods.OfType<SticksModDifficultyAdjust>().FirstOrDefault();

            float originalHitAngle = SticksHitObject.HitAngleForCircleSize(original.CircleSize);
            float adjustedHitAngle = SticksHitObject.HitAngleForCircleSize(adjusted.CircleSize);
            float primaryHitAngle = difficultyAdjust?.PrimaryHitAngle.Value ?? adjustedHitAngle;
            float secondaryHitAngle = difficultyAdjust?.SecondaryHitAngle.Value ?? adjustedHitAngle / 2;

            yield return new RulesetBeatmapAttribute(SongSelectStrings.CircleSize, "CS", original.CircleSize, adjusted.CircleSize, 10)
            {
                Description = "Controls the angular precision required by Sticks notes.",
                AdditionalMetrics =
                [
                    new RulesetBeatmapAttribute.AdditionalMetric("Primary hit angle", LocalisableString.Interpolate($"{primaryHitAngle:0.#}°")),
                    new RulesetBeatmapAttribute.AdditionalMetric("Secondary hit angle", LocalisableString.Interpolate($"{secondaryHitAngle:0.#}°")),
                    new RulesetBeatmapAttribute.AdditionalMetric("Unmodified hit angles", LocalisableString.Interpolate($"{originalHitAngle:0.#}° / {originalHitAngle / 2:0.#}°")),
                ],
            };

            var hitWindows = new SticksHitWindows();
            hitWindows.SetDifficulty(adjusted.OverallDifficulty);
            double rate = ModUtils.CalculateRateWithMods(mods);

            yield return new RulesetBeatmapAttribute(SongSelectStrings.Accuracy, "OD", original.OverallDifficulty, adjusted.OverallDifficulty, 10)
            {
                Description = "Controls the timing windows for the timing half of each note judgement.",
                AdditionalMetrics = hitWindows.GetAllAvailableWindows()
                                              .Reverse()
                                              .Select(window => new RulesetBeatmapAttribute.AdditionalMetric(
                                                  $"{window.result.GetDescription().ToUpperInvariant()} hit window",
                                                  LocalisableString.Interpolate($"±{hitWindows.WindowFor(window.result) / rate:0.##} ms")))
                                              .ToArray(),
            };

            yield return new RulesetBeatmapAttribute(SongSelectStrings.HPDrain, "HP", original.DrainRate, adjusted.DrainRate, 10)
            {
                Description = "Controls passive health drain and judgement penalties.",
            };
        }

        private static string formatDegrees(double? value) => value.HasValue ? $"{value.Value:0.0}°" : "N/A";

        private static string formatCompletion(int hits, int total) => total > 0 ? $"{(double)hits / total:P1}" : "N/A";

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
