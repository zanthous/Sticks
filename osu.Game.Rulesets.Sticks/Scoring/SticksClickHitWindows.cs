using System;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Sticks.Scoring
{
    public class SticksClickHitWindows : HitWindows
    {
        private readonly SticksHitWindows timing = new SticksHitWindows();

        public override bool IsHitResultAllowed(HitResult result) =>
            result is HitResult.Perfect or HitResult.Good or HitResult.Ok or HitResult.Miss;

        public override void SetDifficulty(double difficulty) => timing.SetDifficulty(difficulty);

        public override double WindowFor(HitResult result) => timing.WindowFor(result switch
        {
            // Lazer's difficulty preprocessor always queries Great, even when the best
            // awarded grade is Perfect. This lookup alias does not make Great achievable.
            HitResult.Perfect or HitResult.Great => HitResult.Great,
            HitResult.Good => HitResult.Ok,
            HitResult.Ok => HitResult.Meh,
            HitResult.Miss => HitResult.Miss,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
        });
    }
}
