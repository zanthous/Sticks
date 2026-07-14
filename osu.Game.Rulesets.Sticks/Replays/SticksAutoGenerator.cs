// Copyright (c) Zanthous. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Replays
{
    public class SticksAutoGenerator : AutoGenerator<SticksReplayFrame>
    {
        private const double neutral_lead_time = 50;
        private const double edge_release_delay = 30;
        private const double slider_sample_interval = 32;

        public new Beatmap<SticksHitObject> Beatmap => (Beatmap<SticksHitObject>)base.Beatmap;

        public SticksAutoGenerator(IBeatmap beatmap)
            : base(beatmap)
        {
        }

        protected override void GenerateFrames()
        {
            var left = new StickTrack();
            var right = new StickTrack();
            left.Set(0, Vector2.Zero);
            right.Set(0, Vector2.Zero);

            foreach (SticksHitObject hitObject in Beatmap.HitObjects.OrderBy(hitObject => hitObject.StartTime))
            {
                StickTrack track = hitObject.Side == StickSide.Left ? left : right;

                switch (hitObject)
                {
                    case SticksFlick flick:
                        addFlick(track, flick);
                        break;

                    case SticksSlider slider:
                        addSlider(track, slider);
                        break;

                    case SticksHold hold:
                        addHold(track, hold);
                        break;
                }
            }

            left.Freeze();
            right.Freeze();

            foreach (double time in left.Times.Concat(right.Times).Distinct().OrderBy(time => time))
                Frames.Add(new SticksReplayFrame(time, left.ValueAt(time), right.ValueAt(time)));
        }

        private static void addFlick(StickTrack track, SticksFlick flick)
        {
            track.Set(flick.StartTime - neutral_lead_time, Vector2.Zero);
            track.Set(flick.StartTime - 1, Vector2.Zero);
            track.Set(flick.StartTime, vectorAt(flick.Angle));
            track.Set(flick.StartTime + edge_release_delay, Vector2.Zero);
        }

        private static void addSlider(StickTrack track, SticksSlider slider)
        {
            track.Set(slider.StartTime - neutral_lead_time, Vector2.Zero);
            track.Set(slider.StartTime - 1, Vector2.Zero);
            track.Set(slider.StartTime, vectorAt(slider.Angle));

            for (double time = slider.StartTime; time < slider.EndTime; time += slider_sample_interval)
                track.Set(time, vectorAt(slider.AngleAt(time)));

            for (int segment = 0; segment < slider.SegmentCount - 1; segment++)
            {
                double reversalTime = slider.SegmentEndTimeAt(segment);
                track.Set(reversalTime, vectorAt(slider.AngleAt(reversalTime)));
            }

            foreach (SticksHitObject nested in slider.NestedHitObjects.OfType<SticksHitObject>())
                track.Set(nested.StartTime, vectorAt(slider.AngleAt(nested.StartTime)));

            track.Set(slider.EndTime, vectorAt(slider.AngleAt(slider.EndTime)));
            track.Set(slider.EndTime + edge_release_delay, Vector2.Zero);
        }

        private static void addHold(StickTrack track, SticksHold hold)
        {
            Vector2 direction = vectorAt(hold.Angle);
            track.Set(hold.StartTime - neutral_lead_time, Vector2.Zero);
            track.Set(hold.StartTime - 1, Vector2.Zero);
            track.Set(hold.StartTime, direction);
            track.Set(hold.EndTime, direction);
            track.Set(hold.EndTime + edge_release_delay, Vector2.Zero);
        }

        private static Vector2 vectorAt(float angle)
        {
            float radians = angle * MathF.PI / 180;
            return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
        }

        private sealed class StickTrack
        {
            private readonly SortedDictionary<double, Vector2> values = new SortedDictionary<double, Vector2>();
            private double[] times = Array.Empty<double>();
            private Vector2[] vectors = Array.Empty<Vector2>();

            public IReadOnlyList<double> Times => times;

            public void Set(double time, Vector2 value) => values[time] = value;

            public void Freeze()
            {
                times = values.Keys.ToArray();
                vectors = values.Values.ToArray();
            }

            public Vector2 ValueAt(double time)
            {
                int index = Array.BinarySearch(times, time);
                if (index >= 0)
                    return vectors[index];

                int next = ~index;
                if (next <= 0)
                    return vectors[0];
                if (next >= times.Length)
                    return vectors[^1];

                int previous = next - 1;
                float progress = (float)((time - times[previous]) / (times[next] - times[previous]));
                return vectors[previous] + (vectors[next] - vectors[previous]) * progress;
            }
        }
    }
}
