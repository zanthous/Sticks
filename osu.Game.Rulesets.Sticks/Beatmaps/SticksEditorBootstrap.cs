// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

#nullable enable

using System;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Sticks.Configuration;
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
        public static WorkingBeatmap CreateDifficulty(BeatmapManager beatmapManager, RealmAccess realmAccess, WorkingBeatmap referenceBeatmap, RulesetInfo standardRuleset,
                                                      RulesetInfo sticksRuleset, bool retainConvertedObjects)
        {
            ArgumentNullException.ThrowIfNull(beatmapManager);
            ArgumentNullException.ThrowIfNull(realmAccess);
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
            const string buildIdentifier = "editor-conversion-063";
            string stage = "procedurally converting the selected osu!standard difficulty";
            WorkingBeatmap? copied = null;

            try
            {
                IBeatmap authored = convertStandardSource(freshReference, sticksRuleset);
                if (!retainConvertedObjects)
                {
                    if (authored is not Beatmap<SticksHitObject> sticksBeatmap)
                        throw new InvalidOperationException("Sticks conversion returned an unexpected beatmap type.");

                    sticksBeatmap.HitObjects.Clear();
                }

                stage = "creating the database-backed difficulty shell";

                // Create only the database-backed shell here. CopyExistingDifficulty() asks stock
                // lazer to create a playable copy first, which re-enters ruleset conversion before
                // this explicit standard-to-Sticks operation can force procedural conversion.
                copied = beatmapManager.CreateNewDifficulty(targetSet, freshReference, standardRuleset);
                BeatmapInfo copiedInfo = copied.BeatmapInfo;
                BeatmapSetInfo copiedSet = copiedInfo.BeatmapSet
                                           ?? throw new InvalidOperationException("The copied difficulty has no beatmap set.");

                copiedInfo.DifficultyName = uniqueDifficultyName(copiedSet, copiedInfo.ID, retainConvertedObjects ? "Sticks Converted" : "Sticks");

                // Save the complete carrier once while the database shell is still mode 0.
                // BeatmapManager invokes post-save processing synchronously; switching identity
                // before this write makes that processor reopen the pre-save standard source as
                // Sticks and mistake its arbitrary samples for damaged authored carrier data.
                stage = "writing the complete mode-0 carrier";
                IBeatmap carrier = SticksEditorCarrierBeatmap.Create(authored, standardRuleset);
                beatmapManager.Save(copiedInfo, carrier, copied.Skin, copied.Storyboard);

                // The first save replaced the shell file. Discard its decoded working-beatmap
                // cache before the second save asks post-processing to open it as Sticks.
                beatmapManager.GetWorkingBeatmap(copiedInfo, refetch: true);

                stage = "assigning and persisting the Sticks difficulty identity";
                copiedInfo.Ruleset = sticksRuleset.Clone();
                copiedInfo.StarRating = -1;

                int copiedIndex = copiedSet.Beatmaps.IndexOf(copiedSet.Beatmaps.Single(info => info.ID == copiedInfo.ID));
                if (!ReferenceEquals(copiedSet.Beatmaps[copiedIndex], copiedInfo))
                    copiedSet.Beatmaps[copiedIndex] = copiedInfo;
                copiedInfo.BeatmapSet = copiedSet;

                // The carrier file is already complete. Change only the database identity here;
                // a second BeatmapManager.Save() would synchronously reprocess the whole set and
                // can reopen stale pre-save source data before this bootstrap returns.
                realmAccess.Write(realm =>
                {
                    BeatmapInfo liveBeatmap = realm.Find<BeatmapInfo>(copiedInfo.ID)
                                                   ?? throw new InvalidOperationException("The saved difficulty disappeared before its Sticks identity could be assigned.");
                    RulesetInfo liveRuleset = realm.Find<RulesetInfo>(sticksRuleset.ShortName)
                                                   ?? throw new InvalidOperationException("The installed Sticks ruleset is missing from the database.");

                    liveBeatmap.Ruleset = liveRuleset;
                    liveBeatmap.StarRating = -1;
                });

                stage = "reopening the saved Sticks difficulty";
                WorkingBeatmap saved = beatmapManager.GetWorkingBeatmap(copiedInfo, refetch: true);
                if (saved.BeatmapInfo.Ruleset.ShortName != "sticks" || saved.BeatmapInfo.Ruleset.OnlineID != -1)
                    throw new InvalidOperationException("The new difficulty was not persisted with the Sticks custom-ruleset identity.");

                return saved;
            }
            catch (Exception exception)
            {
                if (copied != null)
                {
                    // CreateNewDifficulty has already persisted a temporary mode-0 shell. Never
                    // leave that orphan behind if conversion or carrier persistence fails.
                    beatmapManager.DeleteDifficultyImmediately(copied.BeatmapInfo);
                }

                throw new InvalidOperationException($"[{buildIdentifier}] Failed while {stage}: {exception.Message}", exception);
            }
        }

        private static IBeatmap convertStandardSource(WorkingBeatmap source, RulesetInfo sticksRuleset)
        {
            Ruleset rulesetInstance = sticksRuleset.CreateInstance()
                                      ?? throw new InvalidOperationException("Creating the Sticks ruleset instance failed.");

            // This command is only exposed for a source difficulty already verified as mode 0.
            // Force procedural conversion so arbitrary osu!standard sample filenames cannot be
            // mistaken for the hidden carrier data used when reopening an authored Sticks map.
            var converter = new SticksBeatmapConverter(source.Beatmap, rulesetInstance, forceProceduralConversion: true)
            {
                DisableBeatmapHitsounds = SticksRulesetConfigManager.DisableBeatmapHitsoundsForConversion,
            };
            IBeatmap converted = converter.Convert(CancellationToken.None);

            IBeatmapProcessor? processor = rulesetInstance.CreateBeatmapProcessor(converted);
            processor?.PreProcess();

            foreach (var hitObject in converted.HitObjects)
                hitObject.ApplyDefaults(converted.ControlPointInfo, converted.Difficulty, CancellationToken.None);

            processor?.PostProcess();
            return converted;
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
