// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;

namespace osu.Game.Rulesets.Sticks.DifficultyTestbed;

internal static class Program
{
    private const string files_environment_variable = "STICKS_OSU_FILES";
    private const double simultaneous_epsilon = 0.01;

    public static int Main(string[] args)
    {
        if (!tryParseArguments(args, out List<string> filesRoots, out bool showHelp))
            return 2;

        if (showHelp)
        {
            printUsage();
            return 0;
        }

        string manifestPath = Path.Combine(AppContext.BaseDirectory, "cases.json");
        TestbedManifest manifest;

        try
        {
            manifest = loadManifest(manifestPath);
            validateManifest(manifest);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Unable to load calibration cases from '{manifestPath}': {exception.Message}");
            return 2;
        }

        if (filesRoots.Count == 0)
        {
            string? configuredRoots = Environment.GetEnvironmentVariable(files_environment_variable);

            if (!string.IsNullOrWhiteSpace(configuredRoots))
            {
                filesRoots.AddRange(configuredRoots.Split(Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            filesRoots.AddRange(findDefaultFileStores());
        }

        filesRoots = filesRoots.Select(Path.GetFullPath)
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .ToList();

        Console.WriteLine($"Sticks star-rating testbed ({manifest.Cases.Count} case{(manifest.Cases.Count == 1 ? string.Empty : "s")})");

        if (filesRoots.Count == 0)
        {
            Console.WriteLine($"No lazer files root configured. Pass --files-root <path> or set {files_environment_variable}.");

            foreach (CalibrationCase calibrationCase in manifest.Cases)
                Console.WriteLine($"[SKIP] {calibrationCase.Id}: no files root configured.");

            return 0;
        }

        Console.WriteLine("Files roots:");
        foreach (string root in filesRoots)
            Console.WriteLine($"  {root}");

        int completed = 0;
        int skipped = 0;
        int errors = 0;

        foreach (CalibrationCase calibrationCase in manifest.Cases)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {calibrationCase.Id} ===");

            string? sourcePath = findSourcePath(calibrationCase.SourceHash, filesRoots);
            if (sourcePath == null)
            {
                Console.WriteLine($"[SKIP] Map file {calibrationCase.SourceHash} was not found in any configured files root.");
                skipped++;
                continue;
            }

            try
            {
                evaluate(calibrationCase, sourcePath);
                completed++;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[ERROR] {calibrationCase.Id}: {exception.Message}");
                errors++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Summary: {completed} evaluated, {skipped} skipped, {errors} error{(errors == 1 ? string.Empty : "s")}.");
        return errors == 0 ? 0 : 1;
    }

    private static void evaluate(CalibrationCase calibrationCase, string sourcePath)
    {
        verifyHash(sourcePath, calibrationCase.SourceHash);

        Beatmap source;
        using (FileStream stream = File.OpenRead(sourcePath))
        using (var reader = new LineBufferedReader(stream))
            source = new LegacyBeatmapDecoder { ApplyOffsets = false }.Decode(reader);

        validateMetadata(calibrationCase, source);
        validateConversionKind(calibrationCase, source);

        var converter = new SticksBeatmapConverter(source, new SticksRuleset());
        if (!converter.CanConvert())
            throw new InvalidDataException(converter.AuthoredCarrierError ?? "The beatmap cannot be converted to Sticks.");

        IBeatmap convertedBeatmap = converter.Convert();
        SticksHitObject[] objects = convertedBeatmap.HitObjects.Cast<SticksHitObject>()
                                                       .OrderBy(hitObject => hitObject.StartTime)
                                                       .ToArray();
        SticksDifficultyBreakdown difficulty = SticksDifficultyCalculator.CalculateDifficulty(
            objects,
            overallDifficulty: convertedBeatmap.Difficulty.OverallDifficulty);
        StructuralCounts counts = countStructure(source, objects);
        Milestone? latestMilestone = calibrationCase.Milestones.LastOrDefault();
        double? milestoneDelta = latestMilestone == null ? null : difficulty.StarRating - latestMilestone.AfterStars;
        bool? inTargetRange = calibrationCase.TargetStars == null
            ? null
            : difficulty.StarRating >= calibrationCase.TargetStars.Minimum
              && difficulty.StarRating <= calibrationCase.TargetStars.Maximum;

        Console.WriteLine($"{source.Metadata.Artist} - {source.Metadata.Title} [{source.BeatmapInfo.DifficultyName}] by {source.Metadata.Author.Username}");
        Console.WriteLine($"Source: {calibrationCase.SourceHash} ({calibrationCase.Conversion} conversion)");
        Console.WriteLine(inTargetRange.HasValue
            ? $"Current: {difficulty.StarRating:0.000} stars | target {calibrationCase.TargetStars!.Minimum:0.00}-{calibrationCase.TargetStars.Maximum:0.00} [{(inTargetRange.Value ? "IN RANGE" : "OUT OF RANGE")}]"
            : $"Current: {difficulty.StarRating:0.000} stars | target not set");

        if (latestMilestone != null)
        {
            Console.WriteLine($"Latest milestone: {latestMilestone.AfterStars:0.000} stars | current delta {formatSigned(milestoneDelta!.Value)} stars");
            Console.WriteLine($"Recorded change: {latestMilestone.BeforeStars:0.000} -> {latestMilestone.AfterStars:0.000} stars ({formatSigned(latestMilestone.AfterStars - latestMilestone.BeforeStars)})");
        }

        Console.WriteLine($"Skills: mechanical {difficulty.Mechanical:0.000}, reading {difficulty.Reading:0.000}, control {difficulty.Control:0.000}, coordination {difficulty.Coordination:0.000}");
        Console.WriteLine($"Precision: angular {difficulty.AngularPrecision:0.000}, timing {difficulty.TimingPrecision:0.000}");
        Console.WriteLine($"Structure: {counts.SourceObjects} source -> {counts.ConvertedObjects} converted | {counts.TimingGroups} timing groups, {counts.Flicks} flicks, {counts.Sliders} sliders, {counts.Holds} hold{(counts.Holds == 1 ? string.Empty : "s")}, {counts.Chords} chords");
        Console.WriteLine($"Traits: {string.Join(", ", calibrationCase.Traits)}");

        foreach (string comment in calibrationCase.Comments)
            Console.WriteLine($"Comment: {comment}");

        if (calibrationCase.Milestones.Count > 0)
        {
            Console.WriteLine("Milestones:");

            foreach (Milestone milestone in calibrationCase.Milestones)
            {
                Console.WriteLine($"  {milestone.Date} | {milestone.Label} | {milestone.BeforeStars:0.000} -> {milestone.AfterStars:0.000} stars | calculator v{milestone.CalculatorVersion}");
                Console.WriteLine($"    {milestone.Comment}");
            }
        }
    }

    private static StructuralCounts countStructure(IBeatmap source, SticksHitObject[] objects)
    {
        int timingGroups = 0;
        int chords = 0;

        for (int i = 0; i < objects.Length;)
        {
            int end = i + 1;
            while (end < objects.Length && Math.Abs(objects[end].StartTime - objects[i].StartTime) <= simultaneous_epsilon)
                end++;

            timingGroups++;
            if (end - i > 1)
                chords++;
            i = end;
        }

        return new StructuralCounts(
            source.HitObjects.Count,
            objects.Length,
            timingGroups,
            objects.Count(hitObject => hitObject is SticksFlick),
            objects.Count(hitObject => hitObject is SticksSlider),
            objects.Count(hitObject => hitObject is SticksHold),
            chords);
    }

    private static void validateMetadata(CalibrationCase calibrationCase, Beatmap source)
    {
        var mismatches = new List<string>();
        addMismatch("artist", calibrationCase.Metadata.Artist, source.Metadata.Artist);
        addMismatch("title", calibrationCase.Metadata.Title, source.Metadata.Title);
        addMismatch("creator", calibrationCase.Metadata.Creator, source.Metadata.Author.Username);
        addMismatch("difficulty", calibrationCase.Metadata.Difficulty, source.BeatmapInfo.DifficultyName);

        if (mismatches.Count > 0)
            throw new InvalidDataException($"Metadata validation failed: {string.Join("; ", mismatches)}.");

        void addMismatch(string field, string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                mismatches.Add($"{field} expected '{expected}', got '{actual}'");
        }
    }

    private static void validateConversionKind(CalibrationCase calibrationCase, Beatmap source)
    {
        bool containsCarrierMarkers = source.HitObjects.Any(hitObject =>
            SticksAuthoredBeatmapCodec.InspectMarker(hitObject).Status != SticksAuthoredBeatmapCodec.MarkerStatus.None);
        string actual = containsCarrierMarkers ? "authored" : "procedural";

        if (!string.Equals(calibrationCase.Conversion, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Conversion validation failed: expected '{calibrationCase.Conversion}', detected '{actual}'.");
    }

    private static void verifyHash(string sourcePath, string expectedHash)
    {
        using FileStream stream = File.OpenRead(sourcePath);
        string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA-256 validation failed: expected {expectedHash}, got {actualHash}.");
    }

    private static string? findSourcePath(string hash, IEnumerable<string> filesRoots)
    {
        foreach (string root in filesRoots)
        {
            string candidate = Path.Combine(root, hash[..1], hash[..2], hash);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> findDefaultFileStores()
    {
        var candidates = new List<string>();
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (!string.IsNullOrWhiteSpace(appData))
        {
            candidates.Add(Path.Combine(appData, "osu", "files"));
            candidates.Add(Path.Combine(appData, "osu-development", "files"));
        }

        const string wslUsers = "/mnt/c/Users";
        if (Directory.Exists(wslUsers))
        {
            try
            {
                foreach (string userDirectory in Directory.EnumerateDirectories(wslUsers))
                {
                    candidates.Add(Path.Combine(userDirectory, "AppData", "Roaming", "osu", "files"));
                    candidates.Add(Path.Combine(userDirectory, "AppData", "Roaming", "osu-development", "files"));
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return candidates.Where(Directory.Exists);
    }

    private static TestbedManifest loadManifest(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<TestbedManifest>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("The manifest is empty.");
    }

    private static void validateManifest(TestbedManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported schema version {manifest.SchemaVersion}.");

        if (manifest.Cases.Count == 0)
            throw new InvalidDataException("The manifest has no calibration cases.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (CalibrationCase calibrationCase in manifest.Cases)
        {
            if (string.IsNullOrWhiteSpace(calibrationCase.Id) || !ids.Add(calibrationCase.Id))
                throw new InvalidDataException($"Case ID '{calibrationCase.Id}' is empty or duplicated.");

            if (calibrationCase.SourceHash.Length != 64 || calibrationCase.SourceHash.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException($"Case '{calibrationCase.Id}' has an invalid SHA-256 sourceHash.");

            if (calibrationCase.TargetStars != null
                && (calibrationCase.TargetStars.Minimum < 0 || calibrationCase.TargetStars.Maximum < calibrationCase.TargetStars.Minimum))
                throw new InvalidDataException($"Case '{calibrationCase.Id}' has an invalid targetStars range.");

            if (calibrationCase.Conversion is not ("procedural" or "authored"))
                throw new InvalidDataException($"Case '{calibrationCase.Id}' conversion must be 'procedural' or 'authored'.");
        }
    }

    private static bool tryParseArguments(string[] args, out List<string> filesRoots, out bool showHelp)
    {
        filesRoots = new List<string>();
        showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--files-root":
                    if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    {
                        Console.Error.WriteLine("--files-root requires a path.");
                        printUsage();
                        return false;
                    }

                    filesRoots.Add(args[i]);
                    break;

                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                default:
                    Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
                    printUsage();
                    return false;
            }
        }

        return true;
    }

    private static void printUsage()
    {
        Console.WriteLine("Usage: dotnet run --project osu.Game.Rulesets.Sticks.DifficultyTestbed -- [--files-root <lazer-files-directory>]");
        Console.WriteLine($"If --files-root is omitted, the usual lazer stores and {files_environment_variable} are checked.");
        Console.WriteLine("The files root is the lazer content-store directory named 'files', not the parent osu data directory.");
    }

    private static string formatSigned(double value) => value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);

    private readonly record struct StructuralCounts(
        int SourceObjects,
        int ConvertedObjects,
        int TimingGroups,
        int Flicks,
        int Sliders,
        int Holds,
        int Chords);
}

internal sealed class TestbedManifest
{
    public int SchemaVersion { get; init; }

    public List<CalibrationCase> Cases { get; init; } = new();
}

internal sealed class CalibrationCase
{
    public string Id { get; init; } = string.Empty;

    public string SourceHash { get; init; } = string.Empty;

    public ExpectedMetadata Metadata { get; init; } = new();

    public string Conversion { get; init; } = string.Empty;

    public StarRange? TargetStars { get; init; }

    public List<string> Comments { get; init; } = new();

    public List<string> Traits { get; init; } = new();

    public List<Milestone> Milestones { get; init; } = new();
}

internal sealed class ExpectedMetadata
{
    public string Artist { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Creator { get; init; } = string.Empty;

    public string Difficulty { get; init; } = string.Empty;
}

internal sealed class StarRange
{
    public double Minimum { get; init; }

    public double Maximum { get; init; }
}

internal sealed class Milestone
{
    public string Date { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public int CalculatorVersion { get; init; }

    public double BeforeStars { get; init; }

    public double AfterStars { get; init; }

    public string Comment { get; init; } = string.Empty;
}
