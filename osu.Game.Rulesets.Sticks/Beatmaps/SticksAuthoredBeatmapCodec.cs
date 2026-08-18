#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Lossless Sticks authoring data carried by ordinary mode-0 hit objects. This keeps authored
    /// maps importable and distributable in stock lazer without modifying the client.
    /// </summary>
    public static class SticksAuthoredBeatmapCodec
    {
        private const string marker_namespace_prefix = "sticks-v";

        public const string MARKER_PREFIX = "sticks-v1~";
        public const string SEGMENT_MARKER_PREFIX = "sticks-v2~";

        public enum MarkerStatus
        {
            None,
            ValidSupported,
            MalformedSupported,
            UnsupportedVersion,
        }

        public readonly record struct MarkerInspection(MarkerStatus Status, int MarkerCount, int? Version, string? Filename, SticksHitObject? Decoded);

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

        /// <summary>
        /// Whether a sample occupies the reserved Sticks carrier namespace. This deliberately
        /// recognises malformed and future versions as metadata so an older ruleset cannot mistake
        /// them for ordinary samples and procedurally replace their authored data.
        /// </summary>
        public static bool IsMarker(HitSampleInfo sample) =>
            sample is ConvertHitObjectParser.FileHitSampleInfo file
            && isReservedMarkerName(getFileName(file.Filename));

        public static MarkerInspection InspectMarker(HitObject source)
        {
            ArgumentNullException.ThrowIfNull(source);

            ConvertHitObjectParser.FileHitSampleInfo[] markers = source.Samples.OfType<ConvertHitObjectParser.FileHitSampleInfo>()
                                                                         .Where(IsMarker)
                                                                         .ToArray();

            if (markers.Length == 0)
                return new MarkerInspection(MarkerStatus.None, 0, null, null, null);

            ConvertHitObjectParser.FileHitSampleInfo marker = markers[0];
            MarkerVersionParseStatus versionStatus = parseMarkerVersion(marker.Filename, out int? version);

            if (markers.Length != 1)
                return new MarkerInspection(MarkerStatus.MalformedSupported, markers.Length, version, marker.Filename, null);

            if (versionStatus == MarkerVersionParseStatus.Malformed)
                return new MarkerInspection(MarkerStatus.MalformedSupported, 1, null, marker.Filename, null);

            if (versionStatus == MarkerVersionParseStatus.Overflow || version is not 1 and not 2)
                return new MarkerInspection(MarkerStatus.UnsupportedVersion, 1, version, marker.Filename, null);

            SticksHitObject? decoded = tryDecodeSupportedMarker(source, marker);
            return decoded == null
                ? new MarkerInspection(MarkerStatus.MalformedSupported, 1, version, marker.Filename, null)
                : new MarkerInspection(MarkerStatus.ValidSupported, 1, version, marker.Filename, decoded);
        }

        public static bool TryDecode(HitObject source, out SticksHitObject? decoded)
        {
            MarkerInspection inspection = InspectMarker(source);
            decoded = inspection.Decoded;
            return inspection.Status == MarkerStatus.ValidSupported;
        }

        private static SticksHitObject? tryDecodeSupportedMarker(HitObject source, ConvertHitObjectParser.FileHitSampleInfo marker)
        {
            string fileName = getFileName(marker.Filename);
            int extensionStart = fileName.LastIndexOf('.');

            if (extensionStart < 0 || !fileName.AsSpan(extensionStart).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!double.IsFinite(source.StartTime))
                return null;

            string filename = fileName[..extensionStart];
            string[] parts = filename.Split('~');
            bool isV1 = string.Equals(parts[0], "sticks-v1", StringComparison.OrdinalIgnoreCase);
            bool isV2 = string.Equals(parts[0], "sticks-v2", StringComparison.OrdinalIgnoreCase);
            if (parts.Length < 4 || (!isV1 && !isV2))
                return null;

            StickSide side = parts[2] switch
            {
                "l" => StickSide.Left,
                "r" => StickSide.Right,
                _ => (StickSide)(-1),
            };

            if (!Enum.IsDefined(side) || !parseFloat(parts[3], out float angle))
                return null;

            IList<HitSampleInfo> samples = decodedSamples(source, marker);

            if (isV2)
            {
                if (parts[1] != "s" || parts.Length != 6
                    || !parse(parts[4], out double segmentedDuration) || !hasValidDuration(source.StartTime, segmentedDuration))
                    return null;

                string[] encodedSegments = parts[5].Split('_');
                if (encodedSegments.Length == 0 || encodedSegments.Length > SticksSlider.MAX_SEGMENT_COUNT)
                    return null;

                var segments = new List<float>(encodedSegments.Length);
                foreach (string encodedSegment in encodedSegments)
                {
                    if (!parseFloat(encodedSegment, out float segment) || Math.Abs(segment) < 1)
                        return null;
                    segments.Add(segment);
                }

                if (!hasValidSliderPath(angle, segments))
                    return null;

                var segmentedSlider = new SticksSlider
                {
                    StartTime = source.StartTime,
                    Duration = segmentedDuration,
                    Side = side,
                    Angle = SticksHitObject.NormaliseAngle(angle),
                    Samples = samples,
                };
                segmentedSlider.SetCustomSegments(segments);
                return segmentedSlider;
            }

            switch (parts[1])
            {
                case "f" when parts.Length == 4:
                    return new SticksFlick
                    {
                        StartTime = source.StartTime,
                        Side = side,
                        Angle = SticksHitObject.NormaliseAngle(angle),
                        Samples = samples,
                    };

                case "h" when parts.Length == 5
                                   && parse(parts[4], out double holdDuration)
                                   && hasValidDuration(source.StartTime, holdDuration):
                    return new SticksHold
                    {
                        StartTime = source.StartTime,
                        Duration = holdDuration,
                        Side = side,
                        Angle = SticksHitObject.NormaliseAngle(angle),
                        Samples = samples,
                    };

                case "s" when parts.Length == 7
                                   && parse(parts[4], out double sliderDuration)
                                   && hasValidDuration(source.StartTime, sliderDuration)
                                   && parseFloat(parts[5], out float arcAngle)
                                   && Math.Abs(arcAngle) >= 1
                                   && int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int repeats)
                                   && repeats >= 0
                                   && repeats < SticksSlider.MAX_SEGMENT_COUNT
                                   && hasValidSliderPath(angle, Enumerable.Range(0, repeats + 1)
                                                                                .Select(index => index % 2 == 0 ? arcAngle : -arcAngle)):
                    return new SticksSlider
                    {
                        StartTime = source.StartTime,
                        Duration = sliderDuration,
                        Side = side,
                        Angle = SticksHitObject.NormaliseAngle(angle),
                        ArcAngle = arcAngle,
                        RepeatCount = repeats,
                        Samples = samples,
                    };
            }

            return null;
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

        private static bool hasValidDuration(double startTime, double duration) =>
            duration > 0 && double.IsFinite(startTime + duration);

        private static bool hasValidSliderPath(float startAngle, IEnumerable<float> segments)
        {
            float totalDistance = 0;
            float currentAngle = SticksHitObject.NormaliseAngle(startAngle);

            foreach (float segment in segments)
            {
                totalDistance += Math.Abs(segment);
                currentAngle += segment;

                if (!float.IsFinite(totalDistance) || !float.IsFinite(currentAngle))
                    return false;
            }

            return totalDistance > 0;
        }

        private static MarkerVersionParseStatus parseMarkerVersion(string filename, out int? version)
        {
            version = null;
            string name = getFileName(filename);
            int tokenStart = marker_namespace_prefix.Length;
            int tokenEnd = name.IndexOf('~', tokenStart);

            if (tokenEnd <= tokenStart)
                return MarkerVersionParseStatus.Malformed;

            ReadOnlySpan<char> token = name.AsSpan(tokenStart, tokenEnd - tokenStart);
            foreach (char character in token)
            {
                if (!char.IsAsciiDigit(character))
                    return MarkerVersionParseStatus.Malformed;
            }

            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedVersion))
                return MarkerVersionParseStatus.Overflow;

            version = parsedVersion;
            return MarkerVersionParseStatus.Parsed;
        }

        private static bool isReservedMarkerName(string fileName)
        {
            if (!fileName.StartsWith(marker_namespace_prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            // Reserve only delimiter-shaped carrier names. Ordinary samples such as
            // "sticks-video.wav" must remain ordinary custom hitsounds, while empty,
            // non-numeric, overflowing, and future version tokens followed by '~' must fail
            // closed rather than silently triggering procedural conversion.
            return fileName.IndexOf('~', marker_namespace_prefix.Length) >= marker_namespace_prefix.Length;
        }

        private static string getFileName(string path)
        {
            // System.IO.Path only treats the current platform's directory separator specially.
            // Beatmap archives may contain either convention regardless of the host OS.
            int separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return path[(separator + 1)..];
        }

        private enum MarkerVersionParseStatus
        {
            Malformed,
            Parsed,
            Overflow,
        }

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
