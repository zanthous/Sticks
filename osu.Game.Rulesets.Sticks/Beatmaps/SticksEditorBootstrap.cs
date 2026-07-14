// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

#nullable enable

using System;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Creates a database-backed Sticks difficulty from the currently selected osu!standard map.
    /// This bypasses lazer's pre-editor custom-difficulty save, which otherwise attempts to encode
    /// an external ruleset directly before its composer can be loaded.
    /// </summary>
    public static class SticksEditorBootstrap
    {
        public static WorkingBeatmap CreateDifficulty(BeatmapManager beatmapManager, WorkingBeatmap referenceBeatmap, RulesetInfo standardRuleset,
                                                      RulesetInfo sticksRuleset, bool retainConvertedObjects)
        {
            ArgumentNullException.ThrowIfNull(beatmapManager);
            ArgumentNullException.ThrowIfNull(referenceBeatmap);
            ArgumentNullException.ThrowIfNull(standardRuleset);
            ArgumentNullException.ThrowIfNull(sticksRuleset);

            if (referenceBeatmap.BeatmapInfo.Ruleset.OnlineID != 0)
                throw new InvalidOperationException("Select an osu!standard difficulty before creating a Sticks difficulty.");
            if (standardRuleset.OnlineID != 0 || standardRuleset.ShortName != "osu")
                throw new InvalidOperationException("osu!standard is unavailable.");
            if (sticksRuleset.ShortName != "sticks" || sticksRuleset.OnlineID != -1)
                throw new InvalidOperationException("The installed Sticks ruleset does not have a valid custom-ruleset identity.");

            Guid referenceID = referenceBeatmap.BeatmapInfo.ID;
            BeatmapInfo referenceInfo = beatmapManager.QueryBeatmap(info => info.ID == referenceID)
                                        ?? throw new InvalidOperationException("The selected beatmap is not available in the local database.");
            BeatmapSetInfo targetSet = referenceInfo.BeatmapSet
                                       ?? throw new InvalidOperationException("The selected difficulty has no beatmap set.");
            WorkingBeatmap freshReference = beatmapManager.GetWorkingBeatmap(referenceInfo);

            // Copying while the difficulty is still mode 0 lets stock lazer perform its initial
            // database/file creation. It is converted and reassigned to Sticks immediately after.
            WorkingBeatmap copied = beatmapManager.CopyExistingDifficulty(targetSet, freshReference);
            BeatmapInfo copiedInfo = copied.BeatmapInfo;

            try
            {
                BeatmapSetInfo copiedSet = copiedInfo.BeatmapSet
                                           ?? throw new InvalidOperationException("The copied difficulty has no beatmap set.");

                IBeatmap authored = copied.GetPlayableBeatmap(sticksRuleset);
                if (!retainConvertedObjects)
                {
                    if (authored is not Beatmap<SticksHitObject> sticksBeatmap)
                        throw new InvalidOperationException("Sticks conversion returned an unexpected beatmap type.");

                    sticksBeatmap.HitObjects.Clear();
                }

                copiedInfo.DifficultyName = uniqueDifficultyName(copiedSet, copiedInfo.ID, retainConvertedObjects ? "Sticks Converted" : "Sticks");
                copiedInfo.Ruleset = sticksRuleset.Clone();

                int copiedIndex = copiedSet.Beatmaps.IndexOf(copiedSet.Beatmaps.Single(info => info.ID == copiedInfo.ID));
                if (!ReferenceEquals(copiedSet.Beatmaps[copiedIndex], copiedInfo))
                    copiedSet.Beatmaps[copiedIndex] = copiedInfo;
                copiedInfo.BeatmapSet = copiedSet;

                IBeatmap carrier = SticksEditorCarrierBeatmap.Create(authored, standardRuleset);
                beatmapManager.Save(copiedInfo, carrier, copied.Skin, copied.Storyboard);

                WorkingBeatmap saved = beatmapManager.GetWorkingBeatmap(copiedInfo, refetch: true);
                if (saved.BeatmapInfo.Ruleset.ShortName != "sticks" || saved.BeatmapInfo.Ruleset.OnlineID != -1)
                    throw new InvalidOperationException("The new difficulty was not persisted with the Sticks custom-ruleset identity.");

                return saved;
            }
            catch
            {
                // CopyExistingDifficulty has already persisted a temporary mode-0 copy. Never
                // leave that orphan behind if conversion or carrier persistence fails.
                beatmapManager.DeleteDifficultyImmediately(copiedInfo);
                throw;
            }
        }

        private static string uniqueDifficultyName(BeatmapSetInfo set, Guid currentID, string baseName)
        {
            string candidate = baseName;
            int suffix = 2;

            while (set.Beatmaps.Any(info => info.ID != currentID && string.Equals(info.DifficultyName, candidate, StringComparison.OrdinalIgnoreCase)))
                candidate = $"{baseName} ({suffix++})";

            return candidate;
        }
    }
}
