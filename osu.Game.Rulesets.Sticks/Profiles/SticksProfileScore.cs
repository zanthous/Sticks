#nullable enable

using System;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Scoring;
using osu.Game.Scoring;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Sticks.Profiles
{
    // Bump Generation when conversion or performance changes make existing results incomparable.
    internal sealed record SticksProfileScore
    {
        public const int CurrentGeneration = 1;
        public const int JudgementCount = 16;
        public Guid ScoreId { get; init; }
        public int UserId { get; init; }
        public int BeatmapId { get; init; }
        public int BeatmapSetId { get; init; }
        public string SourceMd5 { get; init; } = "";
        public long PlayedAt { get; init; }
        public int Generation { get; init; } = CurrentGeneration;
        public int DifficultyVersion { get; init; }
        public uint Mods { get; init; }
        public float Rate { get; init; }
        public double PP { get; init; }
        public double Accuracy { get; init; }
        public int MaxCombo { get; init; }
        public long TotalScore { get; init; }
        public float ActivationThreshold { get; init; }
        public bool Ranked { get; init; }
        public bool MatchesPublishedMap { get; init; }
        public int[] Judgements { get; init; } = new int[JudgementCount];

        // Matches the leaderboard service: a missed head records both a timing and an angle miss, so only
        // timing misses (3) and click misses (11) count. Tracking and tail misses are reported separately.
        public int MissCount => Judgements.Length == JudgementCount ? Judgements[3] + Judgements[11] : 0;

        public bool IsValid => ScoreId != Guid.Empty && UserId >= 0 && BeatmapId > 0 && BeatmapSetId > 0
                               && SourceMd5.Length == 32 && SourceMd5.All(Uri.IsHexDigit)
                               && PlayedAt > 0 && Generation > 0 && DifficultyVersion > 0
                               && Mods == 0 && Rate == 1
                               && double.IsFinite(PP) && PP >= 0 && PP <= 100000
                               && double.IsFinite(Accuracy) && Accuracy >= 0 && Accuracy <= 1
                               && MaxCombo >= 0 && TotalScore >= 0
                               && float.IsFinite(ActivationThreshold) && ActivationThreshold > 0 && ActivationThreshold <= 1
                               && Judgements.Length == JudgementCount && Judgements.All(n => n >= 0 && n <= 10000000);

        public static SticksProfileScore? Capture(ScoreInfo score, double pp, int difficultyVersion, float activationThreshold)
        {
            BeatmapInfo? map = score.BeatmapInfo;
            if (!score.Passed || map?.Ruleset.OnlineID != 0 || !SticksProfilePolicy.TryEncodeMods(score.Mods, out uint mods))
                return null;

            var counts = new int[JudgementCount];
            foreach (HitEvent hit in score.HitEvents)
            {
                int group = hit.HitObject switch
                {
                    SticksSliderTick or SticksSliderRepeat or SticksSliderExtension or SticksHoldTick => 12,
                    SticksSliderTail or SticksHoldTail => 14,
                    SticksClick or SticksSlice => 8,
                    ISticksAccuracyComponent { AccuracyComponent: SticksAccuracyComponent.Timing } => 0,
                    ISticksAccuracyComponent { AccuracyComponent: SticksAccuracyComponent.Angle } => 4,
                    _ => -1,
                };
                if (group < 0) continue;
                int grade = group >= 12 ? (hit.Result.IsHit() ? 0 : 1) : hit.Result switch
                {
                    HitResult.Great => 0, HitResult.Ok => 1, HitResult.Meh => 2, HitResult.Miss => 3, _ => -1,
                };
                if (grade >= 0) counts[group + grade]++;
            }

            var result = new SticksProfileScore
            {
                ScoreId = score.ID,
                UserId = Math.Max(0, score.UserID),
                BeatmapId = map.OnlineID,
                BeatmapSetId = map.BeatmapSet?.OnlineID ?? -1,
                SourceMd5 = map.MD5Hash.ToLowerInvariant(),
                PlayedAt = score.Date.ToUnixTimeMilliseconds(),
                DifficultyVersion = difficultyVersion,
                Mods = mods,
                Rate = (float)ModUtils.CalculateRateWithMods(score.Mods),
                PP = pp,
                Accuracy = score.Accuracy,
                MaxCombo = score.MaxCombo,
                TotalScore = score.TotalScore,
                ActivationThreshold = activationThreshold,
                // Ranked or Approved, matching osu! and the leaderboard service.
                Ranked = map.Status.GrantsPerformancePoints(),
                MatchesPublishedMap = map.Status != BeatmapOnlineStatus.LocallyModified && map.MD5Hash.Length == 32
                                      && string.Equals(map.MD5Hash, map.OnlineMD5Hash, StringComparison.OrdinalIgnoreCase),
                Judgements = counts,
            };
            return result.IsValid ? result : null;
        }
    }
}
