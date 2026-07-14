// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using osu.Game.Audio;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Objects;
using osuTK;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Lossless Sticks authoring data carried by ordinary mode-0 hit objects. This allows maps
    /// created by the companion mapper to remain importable and distributable in stock lazer.
    /// </summary>
    public static class SticksAuthoredBeatmapCodec
    {
        public const string MARKER_PREFIX = "sticks-v1~";
        public const string SEGMENT_MARKER_PREFIX = "sticks-v2~";

        public static string EncodeMarker(SticksHitObject hitObject)
        {
            string side = hitObject.Side == StickSide.Left ? "l" : "r";
            string angle = number(SticksHitObject.NormaliseAngle(hitObject.Angle));

            return hitObject switch
            {
                SticksSlider { HasCustomSegments: true } slider =>
                    $"{SEGMENT_MARKER_PREFIX}s~{side}~{angle}~{number(slider.Duration)}~{string.Join('_', slider.SegmentArcAngles.Select(segment => number(segment)))}.wav",
                SticksSlider slider => $"{MARKER_PREFIX}s~{side}~{angle}~{number(slider.Duration)}~{number(slider.ArcAngle)}~{slider.RepeatCount}.wav",
                SticksHold hold => $"{MARKER_PREFIX}h~{side}~{angle}~{number(hold.Duration)}.wav",
                SticksFlick => $"{MARKER_PREFIX}f~{side}~{angle}.wav",
                _ => throw new ArgumentException($"Unsupported authored Sticks object: {hitObject.GetType().Name}", nameof(hitObject)),
            };
        }

        /// <summary>
        /// Replaces the object's normal sample with its current lossless editor carrier marker.
        /// A <see cref="ConvertHitObjectParser.FileHitSampleInfo"/> is itself a normal sample and
        /// contains normal-skin fallback lookup names, so retaining a second normal sample would
        /// both double playback and violate the legacy encoder's single-normal-sample assumption.
        /// </summary>
        public static void SynchroniseMarker(SticksHitObject hitObject)
        {
            ArgumentNullException.ThrowIfNull(hitObject);

            string expectedFilename = EncodeMarker(hitObject);
            ConvertHitObjectParser.FileHitSampleInfo? existingMarker = hitObject.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>()
                                                                                       .FirstOrDefault(IsMarker);
            HitSampleInfo? regularNormal = hitObject.Samples.FirstOrDefault(sample => sample.Name == HitSampleInfo.HIT_NORMAL && !IsMarker(sample));
            HitSampleInfo? previousNormal = regularNormal ?? existingMarker;
            int volume = previousNormal?.Volume ?? hitObject.Samples.FirstOrDefault()?.Volume ?? 100;

            bool hasExactSingleMarkerNormal = existingMarker != null
                                              && string.Equals(existingMarker.Filename, expectedFilename, StringComparison.Ordinal)
                                              && existingMarker.Volume == volume
                                              && hitObject.Samples.Count(IsMarker) == 1
                                              && hitObject.Samples.Count(sample => sample.Name == HitSampleInfo.HIT_NORMAL) == 1;

            if (hasExactSingleMarkerNormal)
                return;

            var samples = hitObject.Samples.Where(sample => sample.Name != HitSampleInfo.HIT_NORMAL && !IsMarker(sample))
                                   .ToList();
            samples.Insert(0, new ConvertHitObjectParser.FileHitSampleInfo(expectedFilename, volume));
            hitObject.Samples = samples;
        }

        public static bool IsMarker(HitSampleInfo sample) =>
            sample is ConvertHitObjectParser.FileHitSampleInfo file
            && (Path.GetFileName(file.Filename).StartsWith(MARKER_PREFIX, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file.Filename).StartsWith(SEGMENT_MARKER_PREFIX, StringComparison.OrdinalIgnoreCase));

        public static bool TryDecode(HitObject source, out SticksHitObject? decoded)
        {
            decoded = null;
            var marker = source.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>()
                               .FirstOrDefault(sample => IsMarker(sample));

            if (marker == null)
                return false;

            string filename = Path.GetFileNameWithoutExtension(marker.Filename);
            string[] parts = filename.Split('~');
            bool isV1 = string.Equals(parts[0], "sticks-v1", StringComparison.OrdinalIgnoreCase);
            bool isV2 = string.Equals(parts[0], "sticks-v2", StringComparison.OrdinalIgnoreCase);
            if (parts.Length < 4 || (!isV1 && !isV2))
                return false;

            StickSide side = parts[2] switch
            {
                "l" => StickSide.Left,
                "r" => StickSide.Right,
                _ => (StickSide)(-1),
            };

            if (!Enum.IsDefined(side) || !parseFloat(parts[3], out float angle))
                return false;

            IList<HitSampleInfo> samples = decodedSamples(source, marker);

            if (isV2 && parts[1] == "s" && parts.Length == 6
                     && parse(parts[4], out double segmentedDuration) && segmentedDuration > 0)
            {
                string[] encodedSegments = parts[5].Split('_', StringSplitOptions.RemoveEmptyEntries);
                var segments = new List<float>(encodedSegments.Length);
                foreach (string encodedSegment in encodedSegments)
                {
                    if (!parseFloat(encodedSegment, out float segment) || Math.Abs(segment) < 1)
                        return false;
                    segments.Add(segment);
                }

                if (segments.Count == 0)
                    return false;

                var segmentedSlider = new SticksSlider
                {
                    StartTime = source.StartTime,
                    Duration = segmentedDuration,
                    Side = side,
                    Angle = SticksHitObject.NormaliseAngle(angle),
                    Samples = samples,
                };
                segmentedSlider.SetCustomSegments(segments);
                decoded = segmentedSlider;
                return true;
            }

            switch (parts[1])
            {
                case "f" when parts.Length == 4:
                    decoded = new SticksFlick
                    {
                        StartTime = source.StartTime,
                        Side = side,
                        Angle = SticksHitObject.NormaliseAngle(angle),
                        Samples = samples,
                    };
                    return true;

                case "h" when parts.Length == 5 && parse(parts[4], out double holdDuration) && holdDuration > 0:
                    decoded = new SticksHold
                    {
                        StartTime = source.StartTime,
                        Duration = holdDuration,
                        Side = side,
                        Angle = SticksHitObject.NormaliseAngle(angle),
                        Samples = samples,
                    };
                    return true;

                case "s" when parts.Length == 7
                                   && parse(parts[4], out double sliderDuration) && sliderDuration > 0
                                   && parseFloat(parts[5], out float arcAngle)
                                   && int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int repeats)
                                   && repeats >= 0:
                    decoded = new SticksSlider
                    {
                        StartTime = source.StartTime,
                        Duration = sliderDuration,
                        Side = side,
                        Angle = SticksHitObject.NormaliseAngle(angle),
                        ArcAngle = arcAngle,
                        RepeatCount = repeats,
                        Samples = samples,
                    };
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a standard mode-0 proxy suitable for legacy encoding. Flicks use hit circles;
        /// holds and sliders use duration-bearing spinner carriers so their occupied timeline range
        /// remains visible even before Sticks conversion.
        /// </summary>
        public static HitObject CreateLegacyProxy(SticksHitObject source)
        {
            float radians = source.Angle * MathF.PI / 180;
            float radius = source.Side == StickSide.Left ? 160 : 105;
            LegacyProxyHitObject proxy = source switch
            {
                SticksSlider slider => new LegacyDurationProxyHitObject { Duration = slider.Duration },
                SticksHold hold => new LegacyDurationProxyHitObject { Duration = hold.Duration },
                _ => new LegacyProxyHitObject(),
            };

            proxy.StartTime = source.StartTime;
            proxy.Position = new Vector2(256 + MathF.Cos(radians) * radius, 192 + MathF.Sin(radians) * radius);

            int volume = source.Samples.FirstOrDefault()?.Volume ?? 100;
            proxy.Samples.Add(new ConvertHitObjectParser.FileHitSampleInfo(EncodeMarker(source), volume));

            foreach (HitSampleInfo addition in source.Samples.Where(sample => sample.Name != HitSampleInfo.HIT_NORMAL))
                proxy.Samples.Add(addition.With());

            return proxy;
        }

        private static IList<HitSampleInfo> decodedSamples(HitObject source, ConvertHitObjectParser.FileHitSampleInfo marker)
        {
            var result = source.Samples.Where(sample => !ReferenceEquals(sample, marker))
                               .Select(sample => sample.With())
                               .ToList();

            if (result.All(sample => sample.Name != HitSampleInfo.HIT_NORMAL))
                result.Insert(0, new HitSampleInfo(HitSampleInfo.HIT_NORMAL, volume: marker.Volume));

            return result;
        }

        private static string number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static bool parse(string value, out double result) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);

        private static bool parseFloat(string value, out float result) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && float.IsFinite(result);

        private class LegacyProxyHitObject : HitObject, IHasPosition
        {
            public Vector2 Position { get; set; }

            public float X
            {
                get => Position.X;
                set => Position = new Vector2(value, Position.Y);
            }

            public float Y
            {
                get => Position.Y;
                set => Position = new Vector2(Position.X, value);
            }
        }

        private sealed class LegacyDurationProxyHitObject : LegacyProxyHitObject, IHasDuration
        {
            public double EndTime => StartTime + Duration;

            public double Duration { get; set; }
        }
    }
}
