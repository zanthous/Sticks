#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Sticks.Profiles
{
    internal static class SticksProfilePolicy
    {
        public static bool TryEncodeMods(IEnumerable<Mod> mods, out uint mask)
        {
            mask = 0;
            return !mods.Any();
        }

        public static bool IsEligible(SticksProfileScore score, ISet<int> whitelistedSets) =>
            score.IsValid && score.Generation == SticksProfileScore.CurrentGeneration && score.MatchesPublishedMap
            && (score.Ranked || whitelistedSets.Contains(score.BeatmapSetId));

        public static SticksProfileScore[] TopPlays(IEnumerable<SticksProfileScore> scores, int userId, ISet<int> whitelistedSets)
        {
            var records = scores.ToArray();
            // A newly played published revision supersedes old checksums, even if its pp is lower.
            // Local edits are never allowed to invalidate a legitimate published revision.
            var currentHashes = records.Where(s => s.IsValid && s.Generation == SticksProfileScore.CurrentGeneration && s.MatchesPublishedMap)
                .GroupBy(s => s.BeatmapId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.PlayedAt).First().SourceMd5);
            return records.Where(s => s.UserId == userId && IsEligible(s, whitelistedSets)
                                      && currentHashes.TryGetValue(s.BeatmapId, out string? hash) && hash == s.SourceMd5)
                  .GroupBy(s => s.BeatmapId)
                  .Select(group => group.OrderByDescending(s => s.PP).ThenBy(s => s.PlayedAt).ThenBy(s => s.ScoreId).First())
                  .OrderByDescending(s => s.PP).ThenBy(s => s.PlayedAt).ThenBy(s => s.ScoreId).ToArray();
        }

        // The complete local list is retained; only the best 100 contribute to the shared total.
        public static double TotalPP(IEnumerable<SticksProfileScore> topPlays) =>
            topPlays.Take(100).Select((score, index) => score.PP * Math.Pow(0.95, index)).Sum();
    }
}
