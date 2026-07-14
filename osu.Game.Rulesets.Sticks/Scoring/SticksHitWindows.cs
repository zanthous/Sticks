// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    public class SticksHitWindows : HitWindows
    {
        public static readonly DifficultyRange GREAT_WINDOW_RANGE = new DifficultyRange(80, 50, 20);
        public static readonly DifficultyRange OK_WINDOW_RANGE = new DifficultyRange(140, 100, 60);
        public static readonly DifficultyRange MEH_WINDOW_RANGE = new DifficultyRange(200, 150, 100);

        public const double MISS_WINDOW = 400;

        private double great;
        private double ok;
        private double meh;

        public override bool IsHitResultAllowed(HitResult result) =>
            result is HitResult.Great or HitResult.Ok or HitResult.Meh or HitResult.Miss;

        public override void SetDifficulty(double difficulty)
        {
            great = Math.Floor(IBeatmapDifficultyInfo.DifficultyRange(difficulty, GREAT_WINDOW_RANGE)) - 0.5;
            ok = Math.Floor(IBeatmapDifficultyInfo.DifficultyRange(difficulty, OK_WINDOW_RANGE)) - 0.5;
            meh = Math.Floor(IBeatmapDifficultyInfo.DifficultyRange(difficulty, MEH_WINDOW_RANGE)) - 0.5;
        }

        public override double WindowFor(HitResult result) => result switch
        {
            HitResult.Great => great,
            HitResult.Ok => ok,
            HitResult.Meh => meh,
            HitResult.Miss => MISS_WINDOW,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
        };
    }
}
