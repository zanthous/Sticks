using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.DifficultyTestbed;

/// <summary>
/// Reproducible comparisons of conversion mods against real, locally supplied beatmaps.
/// Archives are read in place; their audio, images and other files are never extracted.
/// </summary>
internal static class ConverterComparison
{
    private const double epsilon = 0.01;
    private const double window_length = 8000;
    private const int maximum_map_bytes = 32 * 1024 * 1024;

    public static int Run(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var inputs = new List<string>();
        string? output = null;
        bool parity = false;
        bool combined = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--compare-converters" when i + 1 < args.Length:
                    inputs.Add(args[++i]);
                    break;

                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;

                case "--include-parity":
                    parity = true;
                    break;

                case "--include-combined":
                    combined = true;
                    break;

                default:
                    Console.Error.WriteLine($"Invalid comparison argument '{args[i]}'. Use --compare-converters <directory|file.osu|file.osz> [--output report.json] [--include-parity] [--include-combined].");
                    return 2;
            }
        }

        if (inputs.Count == 0)
            return 2;

        var report = new ComparisonReport();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (string path in inputs.SelectMany(findFiles).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (Path.GetExtension(path).Equals(".osz", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using ZipArchive archive = ZipFile.OpenRead(path);
                        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => Path.GetExtension(entry.FullName).Equals(".osu", StringComparison.OrdinalIgnoreCase)).OrderBy(entry => entry.FullName, StringComparer.Ordinal))
                        {
                            string identifier = displayPath(path) + " :: " + entry.FullName;
                            try
                            {
                                if (entry.Length > maximum_map_bytes)
                                    throw new InvalidDataException("Beatmap exceeds the 32 MiB input limit.");
                                using Stream stream = entry.Open();
                                evaluate(stream, identifier);
                            }
                            catch (Exception exception)
                            {
                                fail(identifier, exception);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        fail(displayPath(path), exception);
                    }
                }
                else
                {
                    try
                    {
                        using FileStream stream = File.OpenRead(path);
                        evaluate(stream, displayPath(path));
                    }
                    catch (Exception exception)
                    {
                        fail(displayPath(path), exception);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            fail("inputs", exception);
        }

        int validationFailures = report.Maps.Sum(map => map.Modes.Sum(mode => mode.Validation.Issues.Count));
        Console.WriteLine($"Compared {report.Maps.Count} maps; {report.Maps.Count(map => map.Conversion == "authored-bypass")} authored bypasses; {report.Skipped.Count} skipped; {report.Errors.Count} errors; {validationFailures} validation issues.");

        if (output != null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                }));
                Console.WriteLine($"Report: {displayPath(output)}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Unable to write report: {exception.Message}");
                return 1;
            }
        }

        return report.Errors.Count == 0 && validationFailures == 0 && report.Maps.Count > 0 ? 0 : 1;

        void fail(string identifier, Exception exception)
        {
            report.Errors.Add(new InputMessage(identifier, exception.Message));
            Console.Error.WriteLine($"[ERROR] {identifier}: {exception.Message}");
        }

        void evaluate(Stream stream, string identifier)
        {
            using var bytes = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                if (bytes.Length + read > maximum_map_bytes)
                    throw new InvalidDataException("Beatmap exceeds the 32 MiB input limit.");
                bytes.Write(buffer, 0, read);
            }

            byte[] data = bytes.ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            if (!hashes.Add(hash))
            {
                report.Skipped.Add(new InputMessage(identifier, "Duplicate SHA-256."));
                return;
            }

            string text = Encoding.UTF8.GetString(data);
            string[] lines = text.Split('\n');
            string? modeLine = sectionValue(lines, "General", "Mode");
            if (modeLine != null && modeLine != "0")
            {
                report.Skipped.Add(new InputMessage(identifier, $"Source mode {modeLine}; only osu!standard is supported."));
                return;
            }

            string header = lines.FirstOrDefault()?.Trim().TrimStart('\uFEFF') ?? string.Empty;
            const string prefix = "osu file format v";
            if (!header.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(header[prefix.Length..], out int version))
                throw new InvalidDataException("Missing osu file format version header.");

            // Match gameplay: respect the file's version and legacy offsets. The decoder itself
            // installs legacy SV/tick flags, applies HitObject defaults (including slider duration)
            // and resolves samples before returning the source map. Do not decode as latest-version.
            Beatmap source;
            using (var reader = new LineBufferedReader(new MemoryStream(data)))
                source = new LegacyBeatmapDecoder(version).Decode(reader);

            var result = new MapComparison
            {
                Source = identifier,
                Sha256 = hash,
                BeatmapId = sectionValue(lines, "Metadata", "BeatmapID"),
                BeatmapSetId = sectionValue(lines, "Metadata", "BeatmapSetID"),
                Artist = source.Metadata.Artist,
                Title = source.Metadata.Title,
                Difficulty = source.BeatmapInfo.DifficultyName,
                Creator = source.Metadata.Author.Username,
                SourceObjects = source.HitObjects.Count,
                SourceDurationObjects = source.HitObjects.Count(note => note is IHasDuration { Duration: > 0 }),
                SourceDurationMs = source.HitObjects.OfType<IHasDuration>().Sum(note => note.Duration),
            };

            var modes = new List<SticksConversionMode> { SticksConversionMode.Standard, SticksConversionMode.Duet };
            if (parity)
                modes.Add(SticksConversionMode.Parity);
            if (combined)
                modes.Add(SticksConversionMode.ParityDuet);
            var ruleset = new SticksRuleset();
            var working = new FlatWorkingBeatmap(source);
            var preflight = new SticksBeatmapConverter(source, ruleset);
            if (!preflight.CanConvert())
                throw new InvalidDataException(preflight.AuthoredCarrierError ?? "Cannot convert source.");
            result.Conversion = preflight.IsAuthoredCarrier ? "authored-bypass" : "procedural";

            foreach (SticksConversionMode mode in modes)
            {
                Mod[] mods = mode switch
                {
                    SticksConversionMode.Duet => new Mod[] { new SticksModDuet() },
                    SticksConversionMode.Parity => new Mod[] { new SticksModParity() },
                    SticksConversionMode.ParityDuet => new Mod[] { new SticksModParityDuet() },
                    _ => Array.Empty<Mod>(),
                };
                // WorkingBeatmap executes the same mod -> converter -> processor -> defaults
                // pipeline as gameplay, including applying IApplicableToBeatmapConverter mods.
                IBeatmap converted = working.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
                SticksHitObject[] notes = converted.HitObjects.Cast<SticksHitObject>().ToArray();
                double stars = new SticksDifficultyCalculator(ruleset.RulesetInfo, working).Calculate(mods).StarRating;
                result.Modes.Add(new ModeComparison(mode.ToString(), stars, measure(notes), validate(notes, preflight.IsAuthoredCarrier), notes.Select(describe).ToArray()));
            }

            ModeComparison standard = result.Modes[0];
            foreach (ModeComparison mode in result.Modes.Skip(1))
            {
                mode.Difference = difference(standard.Objects, mode.Objects);
                mode.Examples = exampleWindows(standard.Objects, mode.Objects);
                if (result.Conversion == "authored-bypass" && mode.Difference.ChangedHeadFraction > 0)
                    mode.Validation.Issues.Add("Conversion mod changed an authored carrier.");
            }

            report.Maps.Add(result);
            Console.WriteLine($"{result.Artist} - {result.Title} [{result.Difficulty}] ({result.BeatmapId ?? "local"}, {result.Conversion})");
            foreach (ModeComparison mode in result.Modes)
            {
                Counts counts = mode.Counts;
                string change = mode.Difference == null ? string.Empty : $"; changed {mode.Difference.ChangedHeadFraction:P1} ({mode.Difference.RemovedOrChangedHeads} removed/changed, {mode.Difference.AddedOrChangedHeads} added/changed)";
                Console.WriteLine($"  {mode.Mode,-8} {mode.Stars:0.000} stars heads={counts.Heads} F/H/S={counts.Flicks}/{counts.Holds}/{counts.Sliders} chords={counts.Chords} dual-sustain={counts.DualSustainOverlapMs / 1000:0.###}s opposite-flicks={counts.SustainWithOppositeFlicks}{change}");
                foreach (ExampleWindow example in mode.Examples)
                    Console.WriteLine($"    compare {formatTime(example.StartTime)}-{formatTime(example.EndTime)} ({example.ChangedHeads} differing heads)");
                foreach (string issue in mode.Validation.Issues.Take(5))
                    Console.WriteLine($"    [INVALID] {issue}");
            }
        }
    }

    private static IEnumerable<string> findFiles(string input)
    {
        if (File.Exists(input))
        {
            if (!isBeatmap(input))
                throw new InvalidDataException($"Expected .osu or .osz: {displayPath(input)}");
            return new[] { Path.GetFullPath(input) };
        }

        if (!Directory.Exists(input))
            throw new DirectoryNotFoundException($"Input does not exist: {displayPath(input)}");
        return Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Where(isBeatmap).Select(Path.GetFullPath);
    }

    private static bool isBeatmap(string path) => Path.GetExtension(path).ToLowerInvariant() is ".osu" or ".osz";
    private static string displayPath(string path) => Path.GetRelativePath(Environment.CurrentDirectory, Path.GetFullPath(path));
    private static string formatTime(double time) => $"{(int)(time / 60000)}:{time / 1000 % 60:00.000}";
    private static double endTime(SticksHitObject note) => note is IHasDuration duration ? duration.EndTime : note.StartTime;

    private static string? sectionValue(string[] lines, string section, string key)
    {
        bool inSection = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
                inSection = line == $"[{section}]";
            if (!inSection || !line.StartsWith(key + ":", StringComparison.Ordinal))
                continue;
            return line[(key.Length + 1)..].Trim();
        }
        return null;
    }

    private static Counts measure(SticksHitObject[] notes)
    {
        SticksHitObject[] ordered = notes.OrderBy(note => note.StartTime).ToArray();
        int chords = 0;
        int groups = 0;
        for (int start = 0; start < ordered.Length;)
        {
            int end = start + 1;
            while (end < ordered.Length && ordered[end].StartTime - ordered[start].StartTime <= epsilon)
                end++;
            if (ordered[start..end].Select(note => note.Side).Distinct().Count() > 1)
                chords++;
            groups++;
            start = end;
        }

        SticksHitObject[] durations = ordered.Where(note => endTime(note) > note.StartTime).ToArray();
        var endpoints = durations.SelectMany(note => new[] { (Time: note.StartTime, Side: note.Side, Change: 1), (Time: endTime(note), Side: note.Side, Change: -1) }).OrderBy(point => point.Time);
        int left = 0;
        int right = 0;
        double previous = 0;
        double dualTime = 0;
        foreach (var point in endpoints)
        {
            if (left > 0 && right > 0)
                dualTime += point.Time - previous;
            if (point.Side == StickSide.Left)
                left += point.Change;
            else
                right += point.Change;
            previous = point.Time;
        }

        int oppositeFlicks = ordered.OfType<SticksFlick>().Count(flick => durations.Any(sustain =>
            sustain.Side != flick.Side && flick.StartTime > sustain.StartTime + epsilon && flick.StartTime < endTime(sustain) - epsilon));
        return new Counts(notes.Length, groups, notes.OfType<SticksFlick>().Count(), notes.OfType<SticksHold>().Count(), notes.OfType<SticksSlider>().Count(), chords, dualTime, oppositeFlicks);
    }

    private static ValidationResult validate(SticksHitObject[] notes, bool authored)
    {
        var result = new ValidationResult();
        var previousEnd = new Dictionary<StickSide, double>();
        double previousTime = double.NegativeInfinity;
        foreach (SticksHitObject note in notes)
        {
            if (note.StartTime < previousTime)
                result.Issues.Add($"Unsorted head at {note.StartTime}ms.");
            previousTime = note.StartTime;
            if (!double.IsFinite(note.StartTime) || !float.IsFinite(note.Angle))
                result.Issues.Add($"Nonfinite head at {note.StartTime}ms.");
            if (note is IHasDuration duration && (!double.IsFinite(duration.Duration) || duration.Duration <= 0))
                result.Issues.Add($"Invalid duration at {note.StartTime}ms.");
            double occupiedUntil = previousEnd.GetValueOrDefault(note.Side, double.NegativeInfinity);
            if (note.StartTime < occupiedUntil - epsilon)
            {
                result.SameSideDurationOverlaps++;
                if (!authored)
                    result.Issues.Add($"{note.Side} head at {note.StartTime}ms overlaps sustain ending at {occupiedUntil}ms.");
            }
            previousEnd[note.Side] = Math.Max(occupiedUntil, endTime(note));

            if (note is SticksSlider slider)
            {
                double speed = Enumerable.Range(0, slider.SegmentCount)
                                         .Max(index => Math.Abs(slider.SegmentArcAngleAt(index)) / slider.SegmentDurationAt(index) * 1000);
                result.MaximumSliderDegreesPerSecond = Math.Max(result.MaximumSliderDegreesPerSecond, speed);
                if (!slider.SegmentArcAngles.All(float.IsFinite) || !double.IsFinite(speed))
                    result.Issues.Add($"Nonfinite slider geometry at {note.StartTime}ms.");
                if (!authored && speed > SticksBeatmapConverter.MAX_GENERATED_SLIDER_ANGULAR_VELOCITY + 0.001)
                    result.Issues.Add($"Slider at {note.StartTime}ms exceeds speed limit: {speed}deg/s.");
            }

            bool decoded = SticksAuthoredBeatmapCodec.TryDecode(SticksAuthoredBeatmapCodec.CreateLegacyProxy(note), out SticksHitObject? roundtrip);
            bool matches = decoded && roundtrip != null && note.GetType() == roundtrip.GetType() && note.Side == roundtrip.Side
                           && Math.Abs(note.StartTime - roundtrip.StartTime) < epsilon
                           && Math.Abs(SticksHitObject.DeltaAngle(note.Angle, roundtrip.Angle)) < 0.001
                           && Math.Abs(endTime(note) - endTime(roundtrip)) < 0.001;
            if (matches && note is SticksSlider original && roundtrip is SticksSlider restored)
                matches = original.SegmentCount == restored.SegmentCount
                          && original.HasTimedSegments == restored.HasTimedSegments
                          && original.SegmentArcAngles.Zip(restored.SegmentArcAngles).All(pair => Math.Abs(pair.First - pair.Second) < 0.001)
                          && Enumerable.Range(0, original.SegmentCount).All(index => Math.Abs(original.SegmentDurationAt(index) - restored.SegmentDurationAt(index)) < 0.001);
            if (!matches)
                result.Issues.Add($"Carrier roundtrip changed or rejected object at {note.StartTime}ms.");
        }
        return result;
    }

    private static ObjectDescription describe(SticksHitObject note) => new(
        note.GetType().Name.Replace("Sticks", string.Empty), note.StartTime, endTime(note), note.Side.ToString(),
        SticksHitObject.NormaliseAngle(note.Angle), note is SticksSlider slider ? slider.SegmentArcAngles.ToArray() : Array.Empty<float>(),
        note is SticksSlider timed && timed.HasTimedSegments ? timed.SegmentDurationWeights.ToArray() : Array.Empty<double>());

    private static string signature(ObjectDescription note) => FormattableString.Invariant(
        $"{note.Kind}:{note.StartTime:0.000}:{note.EndTime:0.000}:{note.Side}:{note.Angle:0.000}:{string.Join(',', note.SegmentArcs.Select(arc => arc.ToString("0.000", CultureInfo.InvariantCulture)))}:{string.Join(',', note.SegmentDurationWeights.Select(weight => weight.ToString("R", CultureInfo.InvariantCulture)))}");

    private static Difference difference(ObjectDescription[] baseline, ObjectDescription[] other)
    {
        Dictionary<string, int> standard = baseline.GroupBy(signature).ToDictionary(group => group.Key, group => group.Count());
        int common = 0;
        foreach (IGrouping<string, ObjectDescription> group in other.GroupBy(signature))
            common += Math.Min(standard.GetValueOrDefault(group.Key), group.Count());
        int union = baseline.Length + other.Length - common;
        return new Difference(common, baseline.Length - common, other.Length - common, union == 0 ? 0 : 1 - common / (double)union);
    }

    private static ExampleWindow[] exampleWindows(ObjectDescription[] standard, ObjectDescription[] other)
    {
        return standard.Concat(other).Select(note => Math.Floor(note.StartTime / window_length) * window_length).Distinct()
                       .Select(start =>
                       {
                           ObjectDescription[] before = standard.Where(note => note.StartTime < start + window_length && note.EndTime >= start).ToArray();
                           ObjectDescription[] after = other.Where(note => note.StartTime < start + window_length && note.EndTime >= start).ToArray();
                           Difference delta = difference(before, after);
                           return new ExampleWindow(start, start + window_length, delta.RemovedOrChangedHeads + delta.AddedOrChangedHeads, before, after);
                       })
                       .Where(window => window.ChangedHeads > 0).OrderByDescending(window => window.ChangedHeads).ThenBy(window => window.StartTime).Take(3).ToArray();
    }

    private sealed class ComparisonReport
    {
        public int SchemaVersion { get; } = 1;
        public string ConverterAssemblySha256 { get; } = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SticksBeatmapConverter).Assembly.Location))).ToLowerInvariant();
        public string ChangedHeadFractionDefinition { get; } = "1 - matching / multiset-union of Standard and experimental objects; equality includes kind, start/end time, side, angle and slider arcs rounded to 0.001, plus exact timed segment weights.";
        public List<MapComparison> Maps { get; } = new();
        public List<InputMessage> Skipped { get; } = new();
        public List<InputMessage> Errors { get; } = new();
    }

    private sealed class MapComparison
    {
        public string Source { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
        public string? BeatmapId { get; init; }
        public string? BeatmapSetId { get; init; }
        public string Artist { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Difficulty { get; init; } = string.Empty;
        public string Creator { get; init; } = string.Empty;
        public string Conversion { get; set; } = string.Empty;
        public int SourceObjects { get; init; }
        public int SourceDurationObjects { get; init; }
        public double SourceDurationMs { get; init; }
        public List<ModeComparison> Modes { get; } = new();
    }

    private sealed record ModeComparison(string Mode, double Stars, Counts Counts, ValidationResult Validation, ObjectDescription[] Objects)
    {
        public Difference? Difference { get; set; }
        public ExampleWindow[] Examples { get; set; } = Array.Empty<ExampleWindow>();
    }

    private sealed class ValidationResult
    {
        public int SameSideDurationOverlaps { get; set; }
        public double MaximumSliderDegreesPerSecond { get; set; }
        public List<string> Issues { get; } = new();
    }

    private sealed record InputMessage(string Source, string Reason);
    private sealed record Counts(int Heads, int TimingGroups, int Flicks, int Holds, int Sliders, int Chords, double DualSustainOverlapMs, int SustainWithOppositeFlicks);
    private sealed record Difference(int MatchingHeads, int RemovedOrChangedHeads, int AddedOrChangedHeads, double ChangedHeadFraction);
    private sealed record ObjectDescription(string Kind, double StartTime, double EndTime, string Side, float Angle, float[] SegmentArcs, double[] SegmentDurationWeights);
    private sealed record ExampleWindow(double StartTime, double EndTime, int ChangedHeads, ObjectDescription[] Standard, ObjectDescription[] Experimental);
}
