#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Recognises repeated source phrases without changing their geometry or rhythm.
    /// Keys remove translation, rotation and tempo, while retaining spatial distances.
    /// </summary>
    internal sealed class SticksCounterpointMotifs
    {
        private const int maximum_heads = 8;
        private const int samples_per_path = 4;
        private readonly string?[] keys;
        private readonly Dictionary<string, int> occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        public SticksCounterpointMotifs(HitObject[] source, IBeatmap beatmap, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            keys = new string?[source.Length];
            var nextOccurrenceIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < source.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? key = keys[i] = createKey(source, beatmap, i, out int headCount);
                // Sliding windows within one occurrence must not make a uniform
                // stream look like several independently repeated phrases.
                if (key != null && i >= nextOccurrenceIndices.GetValueOrDefault(key))
                {
                    occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
                    nextOccurrenceIndices[key] = i + headCount;
                }
            }
        }

        public string? KeyAt(int sourceIndex) => (uint)sourceIndex < (uint)keys.Length ? keys[sourceIndex] : null;

        public int OccurrencesAt(int sourceIndex) => KeyAt(sourceIndex) is string key ? occurrences[key] : 0;

        private static string? createKey(HitObject[] source, IBeatmap beatmap, int start, out int count)
        {
            count = 0;
            double startTime = source[start].StartTime;
            if (!double.IsFinite(startTime))
                return null;
            var timing = beatmap.ControlPointInfo.TimingPointAt(startTime);
            double beatLength = timing.BeatLength;
            if (!double.IsFinite(beatLength) || beatLength <= 0)
                return null;

            double occupiedUntil = startTime;
            for (int i = start; i < source.Length && count < maximum_heads; i++)
            {
                HitObject note = source[i];
                double end = note is IHasDuration duration ? duration.EndTime : note.StartTime;
                if (!double.IsFinite(note.StartTime) || !double.IsFinite(end) || end < note.StartTime
                    || (i > start && note.StartTime < source[i - 1].StartTime)
                    || note.StartTime - occupiedUntil > 2 * beatLength + 1
                    || beatmap.ControlPointInfo.TimingPointAt(note.StartTime).Time != timing.Time
                    || beatmap.ControlPointInfo.TimingPointAt(Math.Max(note.StartTime, end - 0.001)).Time != timing.Time)
                    break;
                occupiedUntil = Math.Max(occupiedUntil, end);
                count++;
            }

            if (count < 4)
                return null;

            Span<Vector2> points = stackalloc Vector2[maximum_heads * (samples_per_path + 1)];
            Span<int> pointCounts = stackalloc int[maximum_heads];
            int pointCount = 0;
            for (int i = 0; i < count; i++)
            {
                pointCounts[i] = 0;
                if (source[start + i] is not IHasPosition positioned)
                    continue;
                points[pointCount++] = positioned.Position;
                pointCounts[i] = 1;
                if (source[start + i] is IHasPath path)
                {
                    for (int sample = 1; sample <= samples_per_path; sample++)
                        points[pointCount++] = positioned.Position + path.Path.PositionAt((double)sample / samples_per_path);
                    pointCounts[i] += samples_per_path;
                }
            }

            Vector2 origin = pointCount > 0 ? points[0] : Vector2.Zero;
            double axisX = 1;
            double axisY = 0;
            bool foundAxis = false;
            for (int i = 0; i < pointCount; i++)
            {
                if (!float.IsFinite(points[i].X) || !float.IsFinite(points[i].Y))
                    return null;
                double x = (double)points[i].X - origin.X;
                double y = (double)points[i].Y - origin.Y;
                double length = Math.Sqrt(x * x + y * y);
                if (!foundAxis && length > 0.5)
                {
                    axisX = x / length;
                    axisY = y / length;
                    foundAxis = true;
                }
            }

            var key = new StringBuilder(512);
            key.Append(count).Append('|');
            int pointIndex = 0;
            for (int i = 0; i < count; i++)
            {
                HitObject note = source[start + i];
                key.Append(note.GetType().FullName).Append(':');
                appendQuantised(key, (note.StartTime - startTime) / beatLength, 96);
                appendQuantised(key, note is IHasDuration duration ? duration.Duration / beatLength : 0, 96);
                key.Append(note is IHasRepeats repeats ? repeats.RepeatCount : 0).Append(':');
                key.Append(pointCounts[i]).Append(':');
                for (int j = 0; j < pointCounts[i]; j++)
                {
                    Vector2 point = points[pointIndex++];
                    double x = (double)point.X - origin.X;
                    double y = (double)point.Y - origin.Y;
                    // Two-pixel bins tolerate normal float/position serialization noise.
                    // These coordinates are never written back to source or converted notes.
                    appendQuantised(key, x * axisX + y * axisY, 0.5);
                    appendQuantised(key, -x * axisY + y * axisX, 0.5);
                }
                key.Append('|');
            }
            return key.ToString();
        }

        private static void appendQuantised(StringBuilder key, double value, double scale)
        {
            double rounded = Math.Round(value * scale, MidpointRounding.AwayFromZero);
            // Rotated zero coordinates can carry IEEE negative zero. They are
            // geometrically identical and must also have identical text keys.
            key.Append(rounded == 0 ? "0" : rounded.ToString(CultureInfo.InvariantCulture)).Append(',');
        }
    }
}
