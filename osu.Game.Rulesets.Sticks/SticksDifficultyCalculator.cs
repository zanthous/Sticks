// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
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
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osu.Game.Storyboards;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Sticks
{
    public class SticksDifficultyCalculator : DifficultyCalculator
    {
        public override int Version => 202608160;

        private SticksDifficultyModel.IncrementalState incrementalState;
        private IBeatmap incrementalBeatmap;
        private int processedTopLevelObjectCount;
        private HitObject firstProcessedObject;
        private HitObject lastProcessedObject;
        private double incrementalClockRate;
        private float incrementalOverallDifficulty;
        private int incrementalMaxCombo;
        private int processedDifficultyCheckpointCount;
        private readonly CancellationCapturingWorkingBeatmap cancellationContext;

        /// <summary>
        /// Number of actual model object evaluations used by the current calculation. This stays
        /// linear for distinct timestamps; simultaneous groups only re-evaluate that small group.
        /// </summary>
        public int IncrementalObjectEvaluationCount => incrementalState?.ObjectEvaluationCount ?? 0;

        /// <summary>
        /// Number of lazer difficulty-object cancellation checkpoints traversed. These checkpoints
        /// follow lazer's end-time ordering and are not necessarily interleaved one-for-one with
        /// exact-prefix attributes when long duration objects overlap later objects.
        /// </summary>
        public int ProcessedDifficultyCheckpointCount => Volatile.Read(ref processedDifficultyCheckpointCount);

        public SticksDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : this(ruleset, new CancellationCapturingWorkingBeatmap(beatmap))
        {
        }

        private SticksDifficultyCalculator(IRulesetInfo ruleset, CancellationCapturingWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
            cancellationContext = beatmap;
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
        {
            CancellationToken cancellationToken = cancellationContext.CurrentToken;
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<HitObject> objects = getChronologicallyOrderedObjects(beatmap.HitObjects);
            double clockRate = ModUtils.CalculateRateWithMods(mods);
            float overallDifficulty = beatmap.Difficulty.OverallDifficulty;

            // Full Calculate() calls are independent queries and may follow arbitrary in-place
            // editor mutation. Prefix reuse is both useful and safe only for the progressive
            // wrapper owned by one synchronous CalculateTimed() traversal.
            bool allowPrefixReuse = !ReferenceEquals(beatmap, Beatmap);
            ensureIncrementalState(beatmap, objects, clockRate, overallDifficulty, allowPrefixReuse, cancellationToken);

            if (incrementalState.ObjectCount == 0)
                return new SticksDifficultyAttributes { Mods = mods };

            cancellationToken.ThrowIfCancellationRequested();
            SticksDifficultyBreakdown difficulty = incrementalState.GetBreakdown();
            cancellationToken.ThrowIfCancellationRequested();

            return new SticksDifficultyAttributes
            {
                Mods = mods,
                StarRating = difficulty.StarRating,
                MaxCombo = incrementalMaxCombo,
                MechanicalDifficulty = difficulty.Mechanical,
                ReadingDifficulty = difficulty.Reading,
                ControlDifficulty = difficulty.Control,
                CoordinationDifficulty = difficulty.Coordination,
                AngularPrecision = difficulty.AngularPrecision,
                TimingPrecision = difficulty.TimingPrecision,
                AccuracyObjectCount = objects.Count(hitObject => hitObject is SticksHitObject),
                TrackingObjectCount = countNested<SticksSliderTick>(objects)
                                      + countNested<SticksSliderRepeat>(objects)
                                      + countNested<SticksSliderExtension>(objects)
                                      + countNested<SticksHoldTick>(objects),
                TailObjectCount = countNested<SticksSliderTail>(objects)
                                  + countNested<SticksHoldTail>(objects),
                OverallDifficulty = overallDifficulty,
                ClockRate = clockRate,
                MechanicalDifficultStrainCount = difficulty.MechanicalDifficultStrainCount,
                ReadingDifficultStrainCount = difficulty.ReadingDifficultStrainCount,
                ControlDifficultStrainCount = difficulty.ControlDifficultStrainCount,
                CoordinationDifficultStrainCount = difficulty.CoordinationDifficultStrainCount,
            };
        }

        private void ensureIncrementalState(IBeatmap beatmap, IReadOnlyList<HitObject> objects, double clockRate, float overallDifficulty,
                                            bool allowPrefixReuse, CancellationToken cancellationToken)
        {
            bool prefixMatches = allowPrefixReuse
                                 && incrementalState != null
                                 && ReferenceEquals(incrementalBeatmap, beatmap)
                                 && incrementalClockRate == clockRate
                                 && incrementalOverallDifficulty == overallDifficulty
                                 // Equal count means a fresh full calculation or a repeated query.
                                 // Reset so in-place editor mutations cannot reuse stale state.
                                 && processedTopLevelObjectCount < objects.Count
                                 && (processedTopLevelObjectCount == 0
                                     || (ReferenceEquals(firstProcessedObject, objects[0])
                                         && ReferenceEquals(lastProcessedObject, objects[processedTopLevelObjectCount - 1])));

            if (!prefixMatches)
            {
                incrementalState = new SticksDifficultyModel.IncrementalState(clockRate, overallDifficulty);
                incrementalBeatmap = beatmap;
                processedTopLevelObjectCount = 0;
                firstProcessedObject = null;
                lastProcessedObject = null;
                incrementalClockRate = clockRate;
                incrementalOverallDifficulty = overallDifficulty;
                incrementalMaxCombo = 0;
            }

            for (int i = processedTopLevelObjectCount; i < objects.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HitObject hitObject = objects[i];

                if (hitObject is SticksHitObject sticksHitObject)
                    incrementalState.Append(sticksHitObject);

                incrementalMaxCombo += maxComboFor(hitObject);
            }

            processedTopLevelObjectCount = objects.Count;
            firstProcessedObject = objects.Count > 0 ? objects[0] : null;
            lastProcessedObject = objects.Count > 0 ? objects[^1] : null;
        }

        private static IReadOnlyList<HitObject> getChronologicallyOrderedObjects(IReadOnlyList<HitObject> objects)
        {
            for (int i = 1; i < objects.Count; i++)
            {
                if (objects[i].StartTime < objects[i - 1].StartTime)
                    return objects.OrderBy(hitObject => hitObject.StartTime).ToArray();
            }

            return objects;
        }

        private static int maxComboFor(IEnumerable<HitObject> hitObjects)
        {
            int combo = 0;

            foreach (HitObject hitObject in hitObjects)
                combo += maxComboFor(hitObject);

            return combo;
        }

        private static int maxComboFor(HitObject hitObject)
        {
            bool isComboNeutralAngle = hitObject is ISticksAccuracyComponent
            {
                AccuracyComponent: SticksAccuracyComponent.Angle,
            };

            int combo = !isComboNeutralAngle && hitObject.Judgement.MaxResult.AffectsCombo() ? 1 : 0;
            return combo + maxComboFor(hitObject.NestedHitObjects);
        }

        private static int countNested<T>(IEnumerable<HitObject> hitObjects)
            where T : HitObject
        {
            int count = 0;

            foreach (HitObject hitObject in hitObjects)
            {
                if (hitObject is T)
                    count++;

                count += countNested<T>(hitObject.NestedHitObjects);
            }

            return count;
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

            // This independent reference path owns its ordering. Timed calculation bypasses it
            // and appends directly to the calculator-owned incremental state.
            return SticksDifficultyModel.CalculateOrdered(objects, clockRate, od);
        }

        /// <summary>
        /// Recalculates one complete prefix independently. Kept as a reference path for exactness
        /// tests and in-process comparison against lazer's incremental <c>CalculateTimed()</c> path.
        /// </summary>
        public static SticksDifficultyBreakdown CalculateDifficultyIndependent(IEnumerable<SticksHitObject> hitObjects, double clockRate = 1,
                                                                               double overallDifficulty = double.NaN) =>
            CalculateDifficulty(hitObjects, clockRate, overallDifficulty);

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

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods)
        {
            SticksHitObject[] objects = beatmap.HitObjects.OfType<SticksHitObject>()
                                                      .OrderBy(hitObject => hitObject.StartTime)
                                                      .ToArray();
            var difficultyObjects = new List<DifficultyHitObject>(objects.Length);
            double clockRate = ModUtils.CalculateRateWithMods(mods);

            for (int i = 0; i < objects.Length; i++)
            {
                SticksHitObject previous = i > 0 ? objects[i - 1] : objects[i];
                difficultyObjects.Add(new SticksDifficultyHitObject(objects[i], previous, clockRate, difficultyObjects, i));
            }

            return difficultyObjects;
        }

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods)
        {
            Interlocked.Exchange(ref processedDifficultyCheckpointCount, 0);
            return new Skill[]
            {
                new SticksDifficultyCancellationBridgeSkill(mods, () =>
                {
                    int checkpoint = Interlocked.Increment(ref processedDifficultyCheckpointCount);
                    OnDifficultyCheckpointProcessed(checkpoint);
                }),
            };
        }

        /// <summary>
        /// Instrumentation hook invoked synchronously when lazer traverses a difficulty-object
        /// cancellation checkpoint. Runtime calculators leave this as a no-op.
        /// </summary>
        protected virtual void OnDifficultyCheckpointProcessed(int checkpoint)
        {
        }

        protected override Mod[] DifficultyAdjustmentMods => new Mod[]
        {
            new SticksModDoubleTime(),
            new SticksModHalfTime(),
            new SticksModEasy(),
            new SticksModHardRock(),
        };

        private sealed class SticksDifficultyHitObject : DifficultyHitObject
        {
            public SticksDifficultyHitObject(SticksHitObject hitObject, SticksHitObject lastObject, double clockRate,
                                             List<DifficultyHitObject> objects, int index)
                : base(hitObject, lastObject, clockRate, objects, index)
            {
            }
        }

        /// <summary>
        /// Restores the cancellation checks performed by lazer before each skill object.
        /// </summary>
        /// <remarks>
        /// The actual one-pass model state intentionally lives in <see cref="CreateDifficultyAttributes"/>.
        /// Base CalculateTimed advances skills by object end time, while its progressive beatmap grows
        /// in top-level list order. With a long slider on one stick overlapping later notes on the other,
        /// skill state can therefore run ahead of the exact prefix being requested. This bridge keeps
        /// cancellation without allowing that end-time ordering to corrupt Sticks prefix attributes.
        /// It cannot guarantee one cancellation opportunity per requested prefix: overlapping long
        /// duration objects may cause lazer to consume several bridge checkpoints before emitting the
        /// first of those prefix attributes.
        /// </remarks>
        private sealed class SticksDifficultyCancellationBridgeSkill : Skill
        {
            private readonly Action processed;

            public SticksDifficultyCancellationBridgeSkill(Mod[] mods, Action processed)
                : base(mods)
            {
                this.processed = processed;
            }

            protected override double ProcessInternal(DifficultyHitObject current)
            {
                processed();
                return 0;
            }

            public override double DifficultyValue() => 0;
        }

        /// <summary>
        /// Captures the effective token selected by lazer's non-virtual difficulty entry points.
        /// This lets model work performed while attributes are emitted remain cancellable even
        /// when an overlapping duration object makes the base skill traversal run ahead.
        /// </summary>
        private sealed class CancellationCapturingWorkingBeatmap : IWorkingBeatmap
        {
            private readonly IWorkingBeatmap inner;

            public CancellationToken CurrentToken { get; private set; }

            public IBeatmapInfo BeatmapInfo => inner.BeatmapInfo;
            public bool BeatmapLoaded => inner.BeatmapLoaded;
            public bool TrackLoaded => inner.TrackLoaded;
            public IBeatmap Beatmap => inner.Beatmap;
            public Waveform Waveform => inner.Waveform;
            public Storyboard Storyboard => inner.Storyboard;
            public ISkin Skin => inner.Skin;
            public Track Track => inner.Track;

            public CancellationCapturingWorkingBeatmap(IWorkingBeatmap inner)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public Texture GetBackground() => inner.GetBackground();

            public Texture GetPanelBackground() => inner.GetPanelBackground();

            public IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods = null) =>
                inner.GetPlayableBeatmap(ruleset, mods);

            public IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken)
            {
                CurrentToken = cancellationToken;
                return inner.GetPlayableBeatmap(ruleset, mods, cancellationToken);
            }

            public Track LoadTrack() => inner.LoadTrack();

            public Stream GetStream(string storagePath) => inner.GetStream(storagePath);

            public void BeginAsyncLoad() => inner.BeginAsyncLoad();

            public void CancelAsyncLoad() => inner.CancelAsyncLoad();

            public void PrepareTrackForPreview(bool looping, double? offsetFromPreviewPoint = null) =>
                inner.PrepareTrackForPreview(looping, offsetFromPreviewPoint);
        }
    }
}
