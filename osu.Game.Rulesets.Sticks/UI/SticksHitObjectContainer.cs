// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.Sticks.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Sticks.UI
{
    public partial class SticksHitObjectContainer : HitObjectContainer
    {
        private const float stack_spacing = 12;
        private const int maximum_stack_offset = 5;

        private readonly List<(DrawableHitObject Drawable, SticksHitObject HitObject)> visibleHeads = new List<(DrawableHitObject, SticksHitObject)>();
        private readonly List<DrawableSticksSlider> visibleSliders = new List<DrawableSticksSlider>();
        private readonly Dictionary<DrawableHitObject, float> headStackOffsets = new Dictionary<DrawableHitObject, float>();

        public bool RadialStackedNoteSpacing { get; set; } = true;

        internal float HeadStackOffsetFor(DrawableHitObject drawable)
        {
            if (!RadialStackedNoteSpacing)
                return 0;

            if (headStackOffsets.TryGetValue(drawable, out float offset))
                return offset;

            // A drawable can enter its visible lifetime after this container's update pass but
            // before its own first update. Resolve that first frame from the already-live
            // obstruction lists so the head never renders at radius zero and then slides out.
            return tryGetUnjudgedHead(drawable) is SticksHitObject hitObject
                ? calculateHeadStackOffset(drawable, hitObject)
                : 0;
        }

        protected override void Update()
        {
            base.Update();
            updateHeadStackOffsets();
        }

        private void updateHeadStackOffsets()
        {
            visibleHeads.Clear();
            visibleSliders.Clear();
            headStackOffsets.Clear();

            if (!RadialStackedNoteSpacing)
                return;

            // InternalChildren contains every non-pooled object in the entire map, including
            // objects which are not currently alive. Iterating it here would make this per-frame
            // overlap pass quadratic in total map size. AliveEntries is bounded to the small AR
            // visibility window and is the only set relevant to on-screen head occlusion.
            foreach (DrawableHitObject drawableHitObject in AliveEntries.Values)
            {
                if (drawableHitObject is DrawableSticksSlider slider)
                    visibleSliders.Add(slider);

                if (tryGetUnjudgedHead(drawableHitObject) is SticksHitObject hitObject)
                    visibleHeads.Add((drawableHitObject, hitObject));
            }

            for (int i = 0; i < visibleHeads.Count; i++)
            {
                var target = visibleHeads[i];
                headStackOffsets[target.Drawable] = calculateHeadStackOffset(target.Drawable, target.HitObject);
            }
        }

        private float calculateHeadStackOffset(DrawableHitObject targetDrawable, SticksHitObject targetHitObject)
        {
            int rank = 0;

            for (int j = 0; j < visibleHeads.Count; j++)
            {
                var candidate = visibleHeads[j];

                if (candidate.HitObject.StartTime < targetHitObject.StartTime
                    && HeadsOverlap(candidate.HitObject, targetHitObject))
                    rank++;
            }

            for (int j = 0; j < visibleSliders.Count; j++)
            {
                DrawableSticksSlider slider = visibleSliders[j];

                if (ReferenceEquals(slider, targetDrawable)
                    || slider.HitObject.Side != targetHitObject.Side
                    || slider.HitObject.StartTime >= targetHitObject.StartTime)
                    continue;

                // An unjudged slider whose head overlaps this target was already counted as
                // a head above. Its path occupies that same radial slot, not an extra one.
                bool alreadyCountedByHead = !slider.HeadJudged
                                            && HeadsOverlap(slider.HitObject, targetHitObject);

                if (!alreadyCountedByHead
                    && slider.FuturePathObstructsHeadAt(Time.Current, targetHitObject.Angle, targetHitObject.PrimaryHitAngle / 2))
                    rank++;
            }

            return OffsetFor(targetHitObject.Side, rank);
        }

        internal static int StackRankFor(SticksHitObject target, IEnumerable<SticksHitObject> visibleHitObjects)
        {
            int rank = 0;

            foreach (SticksHitObject candidate in visibleHitObjects)
            {
                if (!ReferenceEquals(candidate, target)
                    && candidate.StartTime < target.StartTime
                    && HeadsOverlap(candidate, target))
                    rank++;
            }

            return rank;
        }

        internal static float OffsetFor(StickSide side, int rank)
        {
            float direction = side == StickSide.Left ? 1 : -1;
            return direction * Math.Min(Math.Max(rank, 0), maximum_stack_offset) * stack_spacing;
        }

        internal static bool HeadsOverlap(SticksHitObject first, SticksHitObject second)
        {
            if (first.Side != second.Side)
                return false;

            float combinedHalfSpan = (first.PrimaryHitAngle + second.PrimaryHitAngle) / 2;
            return Math.Abs(SticksHitObject.DeltaAngle(first.Angle, second.Angle)) < combinedHalfSpan;
        }

        private static SticksHitObject tryGetUnjudgedHead(DrawableHitObject drawable) => drawable switch
        {
            DrawableSticksFlick flick when !flick.Judged => flick.HitObject,
            DrawableSticksSlider slider when !slider.HeadJudged => slider.HitObject,
            DrawableSticksHold hold when !hold.HeadJudged => hold.HitObject,
            _ => null,
        };

        protected override int Compare(Drawable x, Drawable y)
        {
            if (x is DrawableHitObject xObject && y is DrawableHitObject yObject)
            {
                bool xIsSlider = xObject.HitObject is SticksSlider;
                bool yIsSlider = yObject.HitObject is SticksSlider;

                if (xIsSlider != yIsSlider)
                    return xIsSlider ? -1 : 1;
            }

            return base.Compare(x, y);
        }
    }
}
