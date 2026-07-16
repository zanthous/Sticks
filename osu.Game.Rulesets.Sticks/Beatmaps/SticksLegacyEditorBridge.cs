// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

#nullable enable

using System;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Adapts an in-memory Sticks editor beatmap to lazer's mode-0 legacy save pipeline without
    /// changing the Realm-facing identity of the Sticks ruleset.
    /// </summary>
    public static class SticksLegacyEditorBridge
    {
        /// <summary>
        /// Creates the processor used only by <see cref="EditorBeatmap"/>. Ordinary gameplay
        /// beatmaps intentionally return <see langword="null"/> and retain custom online ID -1.
        /// </summary>
        public static IBeatmapProcessor? TryCreateProcessor(IBeatmap beatmap)
        {
            if (beatmap is not EditorBeatmap editorBeatmap)
                return null;

            if (!string.Equals(editorBeatmap.BeatmapInfo.Ruleset.ShortName, "sticks", StringComparison.Ordinal))
                throw new InvalidOperationException("The Sticks legacy editor bridge may only activate for a Sticks difficulty.");

            RulesetInfo currentRuleset = editorBeatmap.BeatmapInfo.Ruleset;
            int currentOnlineID = currentRuleset.OnlineID;
            if (currentOnlineID != -1 && currentOnlineID != 0)
                throw new InvalidOperationException($"Unexpected Sticks editor ruleset ID {currentOnlineID}.");

            // Editor.cs creates its change handler and captures the initial encoded state after
            // EditorBeatmap has created its ruleset processor. This detached identity therefore
            // affects only this editor session and lets LegacyBeatmapEncoder emit Mode: 0.
            RulesetInfo editorRuleset = currentRuleset.Clone();
            editorRuleset.OnlineID = 0;
            editorBeatmap.BeatmapInfo.Ruleset = editorRuleset;

            var processor = new SticksLegacyEditorBeatmapProcessor(editorBeatmap);
            processor.PreProcess();
            return processor;
        }

        private sealed class SticksLegacyEditorBeatmapProcessor : BeatmapProcessor
        {
            public SticksLegacyEditorBeatmapProcessor(IBeatmap beatmap)
                : base(beatmap)
            {
            }

            public override void PreProcess()
            {
                SticksBeatmapConverter.AssignSyncedNoteLinks(Beatmap.HitObjects.OfType<SticksHitObject>());

                foreach (SticksHitObject hitObject in Beatmap.HitObjects)
                    hitObject.EnsureLegacyEditorMarker();

                base.PreProcess();
            }
        }
    }
}
