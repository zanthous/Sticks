// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Sticks
{
    public class SticksDifficultyCalculator : DifficultyCalculator
    {
        public override int Version => 202607140;

        public SticksDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
        {
            SticksHitObject[] objects = beatmap.HitObjects.OfType<SticksHitObject>().OrderBy(hitObject => hitObject.StartTime).ToArray();
            if (objects.Length == 0)
                return new SticksDifficultyAttributes { Mods = mods };

            SticksDifficultyBreakdown difficulty = CalculateDifficulty(
                objects,
                ModUtils.CalculateRateWithMods(mods),
                beatmap.Difficulty.OverallDifficulty);

            return new SticksDifficultyAttributes
            {
                Mods = mods,
                StarRating = difficulty.StarRating,
                MaxCombo = maxComboFor(beatmap.HitObjects),
                MechanicalDifficulty = difficulty.Mechanical,
                ReadingDifficulty = difficulty.Reading,
                ControlDifficulty = difficulty.Control,
                CoordinationDifficulty = difficulty.Coordination,
                AngularPrecision = difficulty.AngularPrecision,
                TimingPrecision = difficulty.TimingPrecision,
            };
        }

        private static int maxComboFor(IEnumerable<HitObject> hitObjects)
        {
            int combo = 0;

            foreach (HitObject hitObject in hitObjects)
            {
                bool isComboNeutralAngle = hitObject is ISticksAccuracyComponent
                {
                    AccuracyComponent: SticksAccuracyComponent.Angle,
                };

                if (!isComboNeutralAngle && hitObject.Judgement.MaxResult.AffectsCombo())
                    combo++;

                combo += maxComboFor(hitObject.NestedHitObjects);
            }

            return combo;
        }

        public static double CalculateStarRating(IEnumerable<SticksHitObject> hitObjects, double clockRate = 1,
                                                 double overallDifficulty = double.NaN) =>
            CalculateDifficulty(hitObjects, clockRate, overallDifficulty).StarRating;

        public static SticksDifficultyBreakdown CalculateDifficulty(IEnumerable<SticksHitObject> hitObjects, double clockRate = 1,
                                                                    double overallDifficulty = double.NaN)
        {
            SticksHitObject[] objects = hitObjects.OrderBy(hitObject => hitObject.StartTime).ToArray();
            if (objects.Length == 0)
                return default;

            float od = double.IsFinite(overallDifficulty)
                ? (float)overallDifficulty
                : inferOverallDifficulty(objects);

            return SticksDifficultyModel.Calculate(objects, clockRate, od);
        }

        private static float inferOverallDifficulty(IEnumerable<SticksHitObject> objects)
        {
            foreach (HitObject hitObject in flatten(objects))
            {
                if (hitObject.HitWindows is not SticksHitWindows hitWindows)
                    continue;

                double greatWindow = hitWindows.WindowFor(HitResult.Great);
                if (greatWindow > 0)
                    return (float)Math.Clamp((79.5 - greatWindow) / 6, 0, 10);
            }

            return SticksDifficultyScaling.REFERENCE_OVERALL_DIFFICULTY;

            static IEnumerable<HitObject> flatten(IEnumerable<HitObject> source)
            {
                foreach (HitObject hitObject in source)
                {
                    yield return hitObject;

                    foreach (HitObject nested in flatten(hitObject.NestedHitObjects))
                        yield return nested;
                }
            }
        }

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods) => Enumerable.Empty<DifficultyHitObject>();

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods) => Array.Empty<Skill>();

        protected override Mod[] DifficultyAdjustmentMods => new Mod[]
        {
            new SticksModDoubleTime(),
            new SticksModHalfTime(),
            new SticksModEasy(),
            new SticksModHardRock(),
        };
    }
}
