using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// Exports actual gameplay conversions and local timing for reproducible angle-distribution analysis.
/// Archives are read in place, and authored carriers are excluded from procedural parity analysis.
/// </summary>
internal static class ParityAudit
{
    private const int maximum_map_bytes = 32 * 1024 * 1024;

    public static int Run(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var inputs = new List<string>();
        string? output = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--audit-parity" when i + 1 < args.Length:
                    inputs.Add(args[++i]);
                    break;

                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;

                default:
                    return usage();
            }
        }

        if (inputs.Count == 0 || string.IsNullOrWhiteSpace(output))
            return usage();

        var report = new AuditReport();
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
                        foreach (ZipArchiveEntry entry in archive.Entries
                                                                .Where(entry => Path.GetExtension(entry.FullName).Equals(".osu", StringComparison.OrdinalIgnoreCase))
                                                                .OrderBy(entry => entry.FullName, StringComparer.Ordinal))
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

        Console.WriteLine($"Audited {report.Maps.Count} maps across four conversion modes; {report.Skipped.Count} skipped; {report.Errors.Count} errors.");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
            Console.WriteLine($"Audit data: {displayPath(output)}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unable to write audit: {exception.Message}");
            return 1;
        }

        return report.Errors.Count == 0 && report.Maps.Count > 0 ? 0 : 1;

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

            string[] lines = Encoding.UTF8.GetString(data).Split('\n');
            string header = lines.FirstOrDefault()?.Trim().TrimStart('\uFEFF') ?? string.Empty;
            const string prefix = "osu file format v";
            if (!header.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(header[prefix.Length..], out int version))
                throw new InvalidDataException("Missing osu file format version header.");

            string? mode = sectionValue(lines, "General", "Mode");
            if (mode != null && mode != "0")
            {
                report.Skipped.Add(new InputMessage(identifier, $"Source mode {mode}; only osu!standard is supported."));
                return;
            }

            // The file's actual version preserves legacy timing offsets, SV and slider defaults,
            // matching the source map used by the gameplay converter.
            Beatmap source;
            using (var reader = new LineBufferedReader(new MemoryStream(data)))
                source = new LegacyBeatmapDecoder(version).Decode(reader);

            var ruleset = new SticksRuleset();
            var preflight = new SticksBeatmapConverter(source, ruleset);
            if (!preflight.CanConvert())
                throw new InvalidDataException(preflight.AuthoredCarrierError ?? "Cannot convert source.");
            if (preflight.IsAuthoredCarrier)
            {
                report.Skipped.Add(new InputMessage(identifier, "Authored carrier; experimental conversion is bypassed."));
                return;
            }

            var result = new MapAudit
            {
                Source = identifier,
                Sha256 = hash,
                BeatmapId = sectionValue(lines, "Metadata", "BeatmapID"),
                BeatmapSetId = sectionValue(lines, "Metadata", "BeatmapSetID"),
                Artist = source.Metadata.Artist,
                Title = source.Metadata.Title,
                Difficulty = source.BeatmapInfo.DifficultyName,
                Creator = source.Metadata.Author.Username,
                CircleSize = source.Difficulty.CircleSize,
                SourceObjects = source.HitObjects.Count,
            };

            var working = new FlatWorkingBeatmap(source);
            foreach (SticksConversionMode conversionMode in new[]
                     {
                         SticksConversionMode.Standard, SticksConversionMode.Parity,
                         SticksConversionMode.Duet, SticksConversionMode.ParityDuet,
                     })
            {
                Mod[] mods = conversionMode switch
                {
                    SticksConversionMode.Parity => new Mod[] { new SticksModParity() },
                    SticksConversionMode.Duet => new Mod[] { new SticksModDuet() },
                    SticksConversionMode.ParityDuet => new Mod[] { new SticksModParityDuet() },
                    _ => Array.Empty<Mod>(),
                };
                IBeatmap converted = working.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
                ObjectDescription[] objects = converted.HitObjects.Cast<SticksHitObject>().Select(note => describe(note, source)).ToArray();
                result.Modes.Add(new ModeAudit(conversionMode.ToString(), objects));
            }

            report.Maps.Add(result);
        }
    }

    private static int usage()
    {
        Console.Error.WriteLine("Use --audit-parity <directory|file.osu|file.osz> [--audit-parity <additional-input>] --output audit.json.");
        return 2;
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

    private static string? sectionValue(string[] lines, string section, string key)
    {
        bool inSection = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
                inSection = line == $"[{section}]";
            if (inSection && line.StartsWith(key + ":", StringComparison.Ordinal))
                return line[(key.Length + 1)..].Trim();
        }
        return null;
    }

    private static ObjectDescription describe(SticksHitObject note, IBeatmap source)
    {
        double beatLength = source.ControlPointInfo.TimingPointAt(note.StartTime).BeatLength;
        if (!double.IsFinite(beatLength) || beatLength <= 0)
            beatLength = 500;

        return new ObjectDescription(
            note.GetType().Name.Replace("Sticks", string.Empty),
            note.StartTime,
            note is IHasDuration duration ? duration.EndTime : note.StartTime,
            note.Side.ToString(),
            SticksHitObject.NormaliseAngle(note.Angle),
            note is SticksSlider slider ? SticksHitObject.NormaliseAngle(slider.AngleAt(slider.EndTime)) : SticksHitObject.NormaliseAngle(note.Angle),
            note is SticksSlider path ? path.SegmentArcAngles.ToArray() : Array.Empty<float>(),
            note is SticksSlider motion ? motion.SegmentProgressAt(motion.EndTime) : 1,
            beatLength,
            note is SticksSlider timed && timed.HasTimedSegments ? timed.SegmentDurationWeights.ToArray() : Array.Empty<double>());
    }

    private sealed class AuditReport
    {
        public int SchemaVersion { get; } = 1;
        public string ConverterAssemblySha256 { get; } = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SticksBeatmapConverter).Assembly.Location))).ToLowerInvariant();
        public List<MapAudit> Maps { get; } = new();
        public List<InputMessage> Skipped { get; } = new();
        public List<InputMessage> Errors { get; } = new();
    }

    private sealed class MapAudit
    {
        public string Source { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
        public string? BeatmapId { get; init; }
        public string? BeatmapSetId { get; init; }
        public string Artist { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Difficulty { get; init; } = string.Empty;
        public string Creator { get; init; } = string.Empty;
        public float CircleSize { get; init; }
        public int SourceObjects { get; init; }
        public List<ModeAudit> Modes { get; } = new();
    }

    private sealed record InputMessage(string Source, string Reason);
    private sealed record ModeAudit(string Mode, ObjectDescription[] Objects);
    private sealed record ObjectDescription(string Kind, double StartTime, double EndTime, string Side, float Angle, float EndAngle, float[] SegmentArcs, double EndProgress, double BeatLength, double[] SegmentDurationWeights);
}
