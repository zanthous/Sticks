#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    public partial class SticksBeatmapConverter
    {
        public const double MAX_SOURCE_SLIDER_ANGULAR_VELOCITY = 720;

        private readonly Dictionary<HitObject, SticksSlider> sourceSliderConversions = new Dictionary<HitObject, SticksSlider>();

        internal Action<SticksSlider, double, double>? SourceSliderBurstObserved { get; set; }
        internal Action<HitObject, SticksSlider>? SourceSliderObserved { get; set; }

        private void observeSourceSliders(Beatmap<SticksHitObject> converted)
        {
            if (SourceSliderObserved == null)
                return;

            var present = new HashSet<SticksHitObject>(converted.HitObjects);
            foreach (var pair in sourceSliderConversions)
            {
                if (present.Contains(pair.Value))
                    SourceSliderObserved(pair.Key, pair.Value);
            }
        }

        private void applySourceSliderSpeeds(Beatmap<SticksHitObject> converted, IBeatmap source, CancellationToken cancellationToken)
        {
            HitObject[] ordered = source.HitObjects.OrderBy(note => note.StartTime).ToArray();
            var present = new HashSet<SticksHitObject>(converted.HitObjects);
            double multiplier = source.Difficulty.SliderMultiplier;
            if (!double.IsFinite(multiplier) || multiplier <= 0)
                return;

            for (int i = 0; i < ordered.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HitObject original = ordered[i];
                double sourceSpeed = sourceSliderSpeed(original);
                if (sourceSpeed <= 0
                    || !sourceSliderConversions.TryGetValue(original, out SticksSlider? slider)
                    || !present.Contains(slider) || slider.IsStationary)
                    continue;

                // Standard's nominal slider travel is 100 * SliderMultiplier units per beat.
                // Map it to our existing 45 degrees per beat, retaining SV.
                double requestedSpeed = sourceSpeed * 45 / (100 * multiplier);

                double before = Enumerable.Range(0, slider.SegmentCount)
                    .Max(segment => Math.Abs(slider.SegmentArcAngleAt(segment)) / slider.SegmentDurationAt(segment) * 1000);
                if (!double.IsFinite(before) || before <= 0)
                    continue;

                // Keep the source speed profile across entire runs, including slow and long
                // sliders. Only the upper tail is clipped; nominal 800+ degrees/s requests
                // never become gameplay speeds, regardless of the source map's SV.
                double after = Math.Min(MAX_SOURCE_SLIDER_ANGULAR_VELOCITY, requestedSpeed);
                slider.SizeMultiplier = (float)Math.Clamp(after / 360, 1, 2);
                if (!double.IsFinite(after) || after <= 0 || Math.Abs(after - before) <= 0.001)
                    continue;
                double scale = after / before;

                // Run after arrangement: only the actual source slider changes. Copied partners
                // and sliders assembled from circles keep their original trajectories and speed.
                if (slider.HasCustomSegments)
                    slider.SetTimedSegments(slider.SegmentArcAngles.Select(arc => (float)(arc * scale)).ToArray(),
                        Enumerable.Range(0, slider.SegmentCount).Select(slider.SegmentDurationAt).ToArray());
                else
                    slider.ArcAngle = (float)(slider.ArcAngle * scale);

                SourceSliderBurstObserved?.Invoke(slider, sourceSpeed, before);
            }
        }

        private static double sourceSliderSpeed(HitObject original)
        {
            if (original is not IHasPath path || original is not IHasDuration { Duration: > 0 } duration)
                return 0;

            int spans = original is IHasRepeats repeats ? repeats.SpanCount() : 1;
            double speed = path.Path.Distance / (duration.Duration / spans) * 1000;
            return spans > 0 && double.IsFinite(speed) && speed > 0 ? speed : 0;
        }
    }
}
