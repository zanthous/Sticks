// Copyright (c) Zanthous. Licensed under the MIT Licence.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Creates the mode-0 carrier beatmap used to persist an authored Sticks difficulty through
    /// lazer's legacy beatmap storage without changing the difficulty's database ruleset.
    /// </summary>
    public static class SticksEditorCarrierBeatmap
    {
        /// <summary>
        /// Creates an osu!standard-compatible carrier containing lossless marker proxies for every
        /// authored Sticks object.
        /// </summary>
        /// <remarks>
        /// The returned beatmap intentionally intercepts later <see cref="IBeatmap.BeatmapInfo"/>
        /// assignments. <see cref="BeatmapManager.Save"/> assigns the database beatmap info before
        /// encoding; allowing that assignment through would make the legacy encoder reject the
        /// external ruleset. The carrier retains a cloned mode-0 info for encoding while the
        /// separately supplied database info remains Sticks.
        /// </remarks>
        public static IBeatmap Create(IBeatmap authoredBeatmap, RulesetInfo standardRuleset)
        {
            ArgumentNullException.ThrowIfNull(authoredBeatmap);
            ArgumentNullException.ThrowIfNull(standardRuleset);

            if (standardRuleset.OnlineID != 0)
                throw new ArgumentException("The carrier ruleset must be osu!standard (online ID 0).", nameof(standardRuleset));

            return new CarrierBeatmap(authoredBeatmap, standardRuleset);
        }

        private sealed class CarrierBeatmap : Beatmap, IBeatmap
        {
            private readonly RulesetInfo standardRuleset;

            public CarrierBeatmap(IBeatmap source, RulesetInfo standardRuleset)
            {
                this.standardRuleset = standardRuleset.Clone();

                setCarrierInfo(source.BeatmapInfo);

                Difficulty = source.Difficulty.Clone();
                ControlPointInfo = source.ControlPointInfo.DeepClone();
                Breaks = new osu.Framework.Lists.SortedList<BreakPeriod>(Comparer<BreakPeriod>.Default);
                foreach (BreakPeriod breakPeriod in source.Breaks)
                    Breaks.Add(new BreakPeriod(breakPeriod.StartTime, breakPeriod.EndTime));
                AudioLeadIn = source.AudioLeadIn;
                StackLeniency = source.StackLeniency;
                SpecialStyle = source.SpecialStyle;
                LetterboxInBreaks = source.LetterboxInBreaks;
                WidescreenStoryboard = source.WidescreenStoryboard;
                EpilepsyWarning = source.EpilepsyWarning;
                SamplesMatchPlaybackRate = source.SamplesMatchPlaybackRate;
                DistanceSpacing = source.DistanceSpacing;
                GridSize = source.GridSize;
                TimelineZoom = source.TimelineZoom;
                Countdown = source.Countdown;
                CountdownOffset = source.CountdownOffset;
                Bookmarks = source.Bookmarks.ToArray();
                BeatmapVersion = source.BeatmapVersion;

                foreach (HitObject hitObject in source.HitObjects)
                {
                    if (hitObject is not Objects.SticksHitObject sticksObject)
                        throw new ArgumentException($"Unsupported object in authored Sticks beatmap: {hitObject.GetType().Name}.", nameof(source));

                    HitObjects.Add(SticksAuthoredBeatmapCodec.CreateLegacyProxy(sticksObject));
                }
            }

            BeatmapInfo IBeatmap.BeatmapInfo
            {
                get => base.BeatmapInfo;
                set => setCarrierInfo(value);
            }

            private void setCarrierInfo(BeatmapInfo source)
            {
                BeatmapInfo info = source.Clone();
                info.Ruleset = standardRuleset;
                base.BeatmapInfo = info;
            }
        }
    }
}
