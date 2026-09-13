using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using osu.Framework.Localisation;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Legacy;
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
        bool legacyBase = false;

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

                // Kept as an alias for older command lines. Public Parity now already includes
                // the former combined strategy, so both flags request the same comparison.
                case "--include-parity":
                case "--include-combined":
                    parity = true;
                    break;

                case "--include-legacy-base":
                    legacyBase = true;
                    break;

                case "--legacy-duet-base":
                case "--include-counterpoint":
                    legacyBase = true;
                    Console.WriteLine($"{args[i]} now aliases --include-legacy-base: Default uses Counterpoint; historical outputs are labelled LegacyBase.");
                    break;

                default:
                    Console.Error.WriteLine($"Invalid comparison argument '{args[i]}'. Use --compare-converters <directory|file.osu|file.osz> [--output report.json] [--include-parity] [--include-legacy-base].");
                    return 2;
            }
        }

        if (inputs.Count == 0)
            return 2;

        var report = new ComparisonReport { IncludesLegacyBase = legacyBase };
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
                OverallDifficulty = source.Difficulty.OverallDifficulty,
                CircleSize = source.Difficulty.CircleSize,
                SourceCircleDiameter = 128 * LegacyRulesetExtensions.CalculateScaleFromCircleSize(
                    float.IsFinite(source.Difficulty.CircleSize) ? source.Difficulty.CircleSize : 4, true),
                SourceObjects = source.HitObjects.Count,
                SourceDurationObjects = source.HitObjects.Count(note => note is IHasDuration { Duration: > 0 }),
                SourceDurationMs = source.HitObjects.OfType<IHasDuration>().Sum(note => note.Duration),
                SourceCircleStartTimes = source.HitObjects.Where(note => note is not IHasDuration).Select(note => note.StartTime).Distinct().Order().ToArray(),
                SourceTimeline = source.HitObjects.Select(note => describeSource(note, source)).ToArray(),
            };

            var modes = new List<(string Name, bool Parity, bool Encore, bool LegacyBase)>
            {
                ("Default", false, false, false),
                ("Encore", false, true, false),
            };
            if (parity)
            {
                modes.Add(("Parity", true, false, false));
                modes.Add(("ParityEncore", true, true, false));
            }
            if (legacyBase)
            {
                modes.Add(("LegacyBase", false, false, true));
                modes.Add(("LegacyBaseEncore", false, true, true));
                if (parity)
                {
                    modes.Add(("LegacyBaseParity", true, false, true));
                    modes.Add(("LegacyBaseParityEncore", true, true, true));
                }
            }
            var ruleset = new SticksRuleset();
            var working = new FlatWorkingBeatmap(source);
            var preflight = new SticksBeatmapConverter(source, ruleset);
            if (!preflight.CanConvert())
                throw new InvalidDataException(preflight.AuthoredCarrierError ?? "Cannot convert source.");
            result.Conversion = preflight.IsAuthoredCarrier ? "authored-bypass" : "procedural";

            foreach (var mode in modes)
            {
                var selectedMods = new List<Mod>();
                var arrangements = new List<ArrangementDescription>();
                if (mode.LegacyBase)
                {
                    // Historical strategies explicitly opt out of the current arrangement pass.
                    selectedMods.Add(ReferenceConversionMod.Create(mode.Parity ? SticksConversionMode.ParityDuet : SticksConversionMode.Duet));
                }
                else if (mode.Parity)
                    selectedMods.Add(new SticksModParity());

                if (mode.Encore)
                    selectedMods.Add(new SticksModEncore());
                if (!mode.LegacyBase)
                    selectedMods.Add(new ArrangementObserverMod
                    {
                        ArrangementObserved = (family, start, end, changedHands, addedHeads) =>
                            arrangements.Add(new ArrangementDescription(family, start, end, changedHands, addedHeads)),
                    });
                Mod[] mods = selectedMods.ToArray();
                // WorkingBeatmap executes the same mod -> converter -> processor -> defaults
                // pipeline as gameplay, including applying IApplicableToBeatmapConverter mods.
                IBeatmap converted = working.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
                SticksHitObject[] notes = converted.HitObjects.Cast<SticksHitObject>().ToArray();
                // Difficulty calculation may request conversion again. Capture only the
                // arrangements that produced these gameplay objects, before calculating stars.
                ArrangementDescription[] convertedArrangements = arrangements.ToArray();
                double stars = new SticksDifficultyCalculator(ruleset.RulesetInfo, working).Calculate(mods).StarRating;
                result.Modes.Add(new ModeComparison(mode.Name, stars, measure(notes), validate(notes, preflight.IsAuthoredCarrier), notes.Select(describe).ToArray())
                {
                    SourceIdentity = preflight.IsAuthoredCarrier ? null : measureSourceIdentity(result.SourceCircleStartTimes, result.SourceTimeline, notes),
                    Patterns = measurePatterns(notes),
                    Arrangements = convertedArrangements,
                    ConversionStrategy = mode.LegacyBase ? "LegacyDuet" : "Counterpoint",
                });
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
                string clearance = counts.MinimumClickClearanceMs is double minimum ? $"{minimum:0.#}ms" : "n/a";
                Console.WriteLine($"  {mode.Mode,-12} {mode.Stars:0.000} stars heads={counts.Heads} F/H/S/C={counts.Flicks}/{counts.Holds}/{counts.Sliders}/{counts.Clicks} click-clearance-min={clearance} chords={counts.Chords} dual-sustain={counts.DualSustainOverlapMs / 1000:0.###}s opposite-flicks={counts.SustainWithOppositeFlicks}{change}");
                if (mode.SourceIdentity is SourceIdentityMetrics identity)
                    Console.WriteLine($"    source-circle onsets retained={identity.SourceCircleOnsetsWithManualHeads}/{identity.SourceCircleOnsets}; generated sustains within one primary angle window={identity.GeneratedSustainsWithinPrimaryWindow}/{identity.GeneratedSustains}");
                PatternMetrics patterns = mode.Patterns;
                Console.WriteLine($"    tail handoffs={patterns.HeadsAtOppositeSustainTail}; sustain entries={patterns.SustainsStartingDuringOppositeSustain}; directional chord pairs={patterns.ChordAngles.Pairs} coincident/near/small/medium/wide={patterns.ChordAngles.Coincident}/{patterns.ChordAngles.Near}/{patterns.ChordAngles.Small}/{patterns.ChordAngles.Medium}/{patterns.ChordAngles.Wide}");
                foreach (ExampleWindow example in mode.Examples)
                    Console.WriteLine($"    compare {formatTime(example.StartTime)}-{formatTime(example.EndTime)} ({example.ChangedHeads} differing heads)");
                foreach (string issue in mode.Validation.Issues.Take(5))
                    Console.WriteLine($"    [INVALID] {issue}");
            }
        }
    }

    /// <summary>
    /// Observes the ordinary gameplay converter without selecting or enabling a strategy.
    /// </summary>
    private sealed class ArrangementObserverMod : Mod, IApplicableToBeatmapConverter
    {
        public ArrangementObserverMod() { }

        public override string Name => "Arrangement observer";
        public override string Acronym => "REF-OBS";
        public override LocalisableString Description => "Records converter arrangements for local comparisons.";
        public override ModType Type => ModType.System;

        public Action<string, double, double, int, int>? ArrangementObserved { get; init; }

        public void ApplyToBeatmapConverter(IBeatmapConverter converter)
        {
            if (converter is SticksBeatmapConverter sticks)
                sticks.CounterpointArrangementObserved = ArrangementObserved;
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
        SticksClick[] clicks = ordered.OfType<SticksClick>().ToArray();
        // Measure the distance to the entire occupied interval of every other note, on either
        // hand. A click during a slider or alongside a head has zero clearance even if that
        // other gesture's head was far away. Other clicks count as nearby notes too.
        double[] clearances = clicks.Select(click => ordered.Where(note => note != click)
                                                           .Select(note => Math.Max(0, Math.Max(note.StartTime - click.StartTime, click.StartTime - endTime(note))))
                                                           .DefaultIfEmpty(double.PositiveInfinity).Min())
                                    .Where(double.IsFinite).Order().ToArray();
        int overlappingClicks = clicks.Count(click => ordered.Any(note => note is not SticksClick
            && click.StartTime >= note.StartTime - epsilon && click.StartTime <= endTime(note) + epsilon));
        double durationMs = ordered.Length > 1 ? ordered.Max(endTime) - ordered[0].StartTime : 0;
        double? medianClearance = clearances.Length == 0 ? null
            : (clearances[(clearances.Length - 1) / 2] + clearances[clearances.Length / 2]) / 2;

        return new Counts(notes.Length, groups, notes.OfType<SticksFlick>().Count(), notes.OfType<SticksHold>().Count(), notes.OfType<SticksSlider>().Count(),
            clicks.Length, notes.Length == 0 ? 0 : clicks.Length / (double)notes.Length, durationMs > 0 ? clicks.Length * 60000 / durationMs : 0,
            clearances.Length == 0 ? null : clearances[0], medianClearance, overlappingClicks,
            chords, dualTime, oppositeFlicks);
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
            // Button presses do not occupy the stick's directional input. Their overlap with
            // an existing sustain is supported gameplay, measured separately as click clearance.
            if (note is not SticksClick)
            {
                double occupiedUntil = previousEnd.GetValueOrDefault(note.Side, double.NegativeInfinity);
                if (note.StartTime < occupiedUntil - epsilon)
                {
                    result.SameSideDurationOverlaps++;
                    if (!authored)
                        result.Issues.Add($"{note.Side} head at {note.StartTime}ms overlaps sustain ending at {occupiedUntil}ms.");
                }
                previousEnd[note.Side] = Math.Max(occupiedUntil, endTime(note));
            }

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
            bool matches = decoded && roundtrip != null
                           && (note.GetType() == roundtrip.GetType() || note is SticksHold && roundtrip is SticksSlider { IsStationary: true })
                           && note.Side == roundtrip.Side
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

    private static PatternMetrics measurePatterns(SticksHitObject[] notes)
    {
        // Clicks have no direction. Including their arbitrary stored angle would make the
        // chord histogram misleading, so these diagnostics describe directional gestures.
        SticksHitObject[] directional = notes.Where(note => note is not SticksClick).OrderBy(note => note.StartTime).ToArray();
        SticksHitObject[] sustains = directional.Where(note => endTime(note) > note.StartTime).ToArray();
        var chords = new List<ChordDescription>();
        for (int start = 0; start < directional.Length;)
        {
            int end = start + 1;
            while (end < directional.Length && directional[end].StartTime - directional[start].StartTime <= epsilon)
                end++;
            for (int first = start; first < end; first++)
            {
                for (int second = first + 1; second < end; second++)
                {
                    if (directional[first].Side == directional[second].Side)
                        continue;
                    chords.Add(new ChordDescription(directional[start].StartTime,
                        Math.Abs(SticksHitObject.DeltaAngle(directional[first].Angle, directional[second].Angle))));
                }
            }
            start = end;
        }

        bool duringOppositeSustain(SticksHitObject note) => sustains.Any(sustain => sustain.Side != note.Side
            && note.StartTime > sustain.StartTime + epsilon && note.StartTime < endTime(sustain) - epsilon);

        double[] tailHeads = directional.Where(note => sustains.Any(sustain => sustain.Side != note.Side
            && Math.Abs(note.StartTime - endTime(sustain)) <= epsilon)).Select(note => note.StartTime).ToArray();
        double[] interiorHeads = directional.Where(duringOppositeSustain).Select(note => note.StartTime).ToArray();
        double[] interiorFlicks = directional.OfType<SticksFlick>().Where(duringOppositeSustain).Select(note => note.StartTime).ToArray();
        double[] interiorSustains = sustains.Where(duringOppositeSustain).Select(note => note.StartTime).ToArray();
        var angles = new ChordAngleHistogram(chords.Count,
            chords.Count(chord => chord.SeparationDegrees <= epsilon),
            chords.Count(chord => chord.SeparationDegrees > epsilon && chord.SeparationDegrees <= 5),
            chords.Count(chord => chord.SeparationDegrees > 5 && chord.SeparationDegrees <= 45),
            chords.Count(chord => chord.SeparationDegrees > 45 && chord.SeparationDegrees <= 90),
            chords.Count(chord => chord.SeparationDegrees > 90));
        return new PatternMetrics(angles, tailHeads.Length, interiorHeads.Length, interiorFlicks.Length, interiorSustains.Length,
            chords.ToArray(), tailHeads, interiorHeads, interiorFlicks, interiorSustains);
    }

    private static SourceIdentityMetrics measureSourceIdentity(double[] sourceCircleStartTimes, SourceDescription[] sourceTimeline, SticksHitObject[] notes)
    {
        // Only top-level objects require a fresh input at their start. Slider ticks, path nodes
        // and tails must not make absorbed source circles appear to remain manual hits.
        double[] manualHeadTimes = notes.Select(note => note.StartTime).Order().ToArray();
        int retainedOnsets = sourceCircleStartTimes.Count(time => containsTime(manualHeadTimes, time));
        SticksHitObject[] generatedSustains = notes.Where(note => note is SticksHold or SticksSlider
            && endTime(note) > note.StartTime && containsTime(sourceCircleStartTimes, note.StartTime)).ToArray();
        int withinPrimaryWindow = generatedSustains.Count(note => angularExcursion(note) <= note.PrimaryHitAngle + 0.001);

        double[] sourceHeadTimes = sourceTimeline.Select(note => note.StartTime).Distinct().Order().ToArray();
        SourceDescription[] sourceDurations = sourceTimeline.Where(note => note.EndTime > note.StartTime).ToArray();
        double[] sourceDurationHeadTimes = sourceDurations.Select(note => note.StartTime).Distinct().Order().ToArray();
        int retainedDurationOnsets = sourceDurationHeadTimes.Count(time => containsTime(manualHeadTimes, time));
        (double Start, double End)[] sourceIntervals = mergeIntervals(sourceDurations.Select(note => (note.StartTime, note.EndTime)));
        (double Start, double End)[] convertedIntervals = mergeIntervals(notes.Where(note => endTime(note) > note.StartTime)
                                                                            .Select(note => (note.StartTime, endTime(note))));
        double sourceDurationUnion = sourceIntervals.Sum(interval => interval.End - interval.Start);
        double convertedDurationUnion = convertedIntervals.Sum(interval => interval.End - interval.Start);
        double coveredDuration = 0;
        int convertedIndex = 0;
        foreach (var sourceInterval in sourceIntervals)
        {
            while (convertedIndex < convertedIntervals.Length && convertedIntervals[convertedIndex].End <= sourceInterval.Start)
                convertedIndex++;
            for (int index = convertedIndex; index < convertedIntervals.Length && convertedIntervals[index].Start < sourceInterval.End; index++)
                coveredDuration += Math.Max(0, Math.Min(sourceInterval.End, convertedIntervals[index].End)
                                              - Math.Max(sourceInterval.Start, convertedIntervals[index].Start));
        }

        return new SourceIdentityMetrics(sourceCircleStartTimes.Length, retainedOnsets,
            sourceCircleStartTimes.Length == 0 ? null : retainedOnsets / (double)sourceCircleStartTimes.Length,
            generatedSustains.Length, withinPrimaryWindow, sourceDurationHeadTimes.Length, retainedDurationOnsets,
            sourceDurationUnion, coveredDuration, sourceDurationUnion > 0 ? coveredDuration / sourceDurationUnion : null,
            Math.Max(0, convertedDurationUnion - coveredDuration), notes.Count(note => !containsTime(sourceHeadTimes, note.StartTime)));
    }

    private static (double Start, double End)[] mergeIntervals(IEnumerable<(double Start, double End)> intervals)
    {
        var result = new List<(double Start, double End)>();
        foreach (var interval in intervals.OrderBy(interval => interval.Start))
        {
            if (result.Count == 0 || interval.Start > result[^1].End)
                result.Add(interval);
            else
                result[^1] = (result[^1].Start, Math.Max(result[^1].End, interval.End));
        }
        return result.ToArray();
    }

    private static SourceDescription describeSource(HitObject note, IBeatmap source)
    {
        double end = note is IHasDuration duration ? duration.EndTime : note.StartTime;
        int repeats = note is IHasRepeats repeated && end > note.StartTime ? Math.Max(0, repeated.RepeatCount) : 0;
        var timing = source.ControlPointInfo.TimingPointAt(note.StartTime);
        return new SourceDescription(note.GetType().Name, note.StartTime, end, timing.Time, timing.BeatLength,
            note is IHasPosition positioned ? positioned.Position.X : null,
            note is IHasPosition positionedY ? positionedY.Position.Y : null,
            repeats, repeats <= 4096 ? Enumerable.Range(1, repeats).Select(index => note.StartTime + (end - note.StartTime) * index / (repeats + 1)).ToArray() : Array.Empty<double>(),
            note is IHasRepeats ? SticksCounterpointCheckpoints.Generate(note, source)
                .Where(point => point.Type == SliderEventType.Tick).Select(point => point.Time).ToArray() : Array.Empty<double>(),
            note.Samples.Any(sample => sample.Name is HitSampleInfo.HIT_CLAP or HitSampleInfo.HIT_FINISH));
    }

    private static bool containsTime(double[] orderedTimes, double time)
    {
        int index = Array.BinarySearch(orderedTimes, time);
        if (index >= 0)
            return true;

        index = ~index;
        return index < orderedTimes.Length && orderedTimes[index] - time <= epsilon
               || index > 0 && time - orderedTimes[index - 1] <= epsilon;
    }

    private static double angularExcursion(SticksHitObject note)
    {
        if (note is not SticksSlider slider)
            return 0;

        // Segment arcs are signed and unwrapped. Comparing only the head and tail would miss
        // reversals, while summing absolute travel would overstate repeated small oscillations.
        double position = 0;
        double minimum = 0;
        double maximum = 0;
        foreach (float arc in slider.SegmentArcAngles)
        {
            position += arc;
            minimum = Math.Min(minimum, position);
            maximum = Math.Max(maximum, position);
        }

        return maximum - minimum;
    }

    private static ObjectDescription describe(SticksHitObject note) => new(
        note.GetType().Name.Replace("Sticks", string.Empty), note.StartTime, endTime(note), note.Side.ToString(),
        SticksHitObject.NormaliseAngle(note.Angle), note is SticksSlider slider ? slider.SegmentArcAngles.ToArray() : Array.Empty<float>(),
        note is SticksSlider timed && timed.HasTimedSegments ? timed.SegmentDurationWeights.ToArray() : Array.Empty<double>(), note.PrimaryHitAngle);

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
        public int SchemaVersion { get; } = 5;
        public double RapidJumpMaximumIntervalMs { get; } = SticksBeatmapConverter.RAPID_ALTERNATION_THRESHOLD;
        public int RapidJumpMinimumHeads { get; } = 4;
        public string ConverterAssemblySha256 { get; } = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SticksBeatmapConverter).Assembly.Location))).ToLowerInvariant();
        public bool IncludesLegacyBase { get; init; }
        public string DefaultConversionStrategy { get; } = "Counterpoint";
        public string ChangedHeadFractionDefinition { get; } = "1 - matching / multiset-union of Default and compared objects; equality includes kind, start/end time, side, angle and slider arcs rounded to 0.001, plus exact timed segment weights.";
        public string ClickClearanceDefinition { get; } = "Minimum distance in milliseconds from each click to any other note's occupied start-to-end interval, on either hand, including other clicks. Simultaneous or sustaining gestures give zero; absent finite measurements give null.";
        public string SourceIdentityDefinition { get; } = "Unique start times of source objects without IHasDuration are circle onsets. An onset is retained if any converted top-level manual head starts within 0.01 ms; nested ticks, reversals and tails do not count. Generated sustains are positive-duration holds or sliders starting at a source circle onset. Within-primary-window means their full unwrapped angular excursion (maximum minus minimum cumulative signed segment angle; zero for holds) is at most the object's actual full PrimaryHitAngle plus 0.001 degrees. One stationary aim could therefore cover the entire path within the primary window; this is a geometric observation, not a quality judgement. Authored bypass metrics are null.";
        public string DurationCoverageDefinition { get; } = "Source duration onsets count unique starts of positive-duration source objects. Their retained onsets use the same manual-head test. SourceDurationUnionMs is the time covered by at least one source duration; SourceDurationCoveredMs intersects that union with the converted duration union, regardless of hand. ConvertedDurationOutsideSourceMs is the converted union outside source duration intervals. ManualHeadsAwayFromSourceOnsets counts heads more than 0.01 ms from every source head; musical slider repeats or tails may justify these, so it is not an error count.";
        public string PatternDefinition { get; } = "Directional gestures exclude clicks. Chord angles count each opposite-hand pair within a head group of 0.01 ms: Coincident <= 0.01 degrees; Near > 0.01 to 5; Small > 5 to 45; Medium > 45 to 90; Wide > 90 to 180. Tail heads coincide within 0.01 ms with an opposite-hand sustain end. Interior heads start strictly after an opposite-hand sustain start and before its end, excluding a 0.01 ms boundary margin. Each head counts once per family; times are exported per head. Families can overlap and greater counts do not imply better conversion.";
        public string SourceTimelineDefinition { get; } = "Decoded source kind, start/end time, timing-section start and local beat length in milliseconds, repeat count/times, source slider tick times and clap-or-finish head accent. Tick times use osu's shared SliderEventGenerator, including mirrored repeat positions, GenerateTicks and pre-v8 SV density; these are source checkpoints, not Sticks-generated scoring ticks. Tick exports share Counterpoint's bounds: at most 16 spans and 32 ticks, with ticks omitted for excessive density or invalid intervals. Repeat times are omitted only for pathological objects with more than 4096 repeats; count and duration remain available. Times include the same format-version and legacy offsets as gameplay.";
        public string RapidJumpDefinition { get; } = "SourceTimeline positionX/positionY and map sourceCircleDiameter use osu!standard source geometry. A protected run has at least RapidJumpMinimumHeads consecutive positioned source objects without positive duration, each separated by more than one circle diameter, more than 0.01ms but at most RapidJumpMaximumIntervalMs apart, inside one timing section. This diagnostic exports the inputs so preservation checks can reconstruct runs independently.";
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
        public float OverallDifficulty { get; init; }
        public float CircleSize { get; init; }
        public double SourceCircleDiameter { get; init; }
        public string Conversion { get; set; } = string.Empty;
        public int SourceObjects { get; init; }
        public int SourceDurationObjects { get; init; }
        public double SourceDurationMs { get; init; }
        public double[] SourceCircleStartTimes { get; init; } = Array.Empty<double>();
        public SourceDescription[] SourceTimeline { get; init; } = Array.Empty<SourceDescription>();
        public List<ModeComparison> Modes { get; } = new();
    }

    private sealed record ModeComparison(string Mode, double Stars, Counts Counts, ValidationResult Validation, ObjectDescription[] Objects)
    {
        public string ConversionStrategy { get; init; } = string.Empty;
        public SourceIdentityMetrics? SourceIdentity { get; init; }
        public PatternMetrics Patterns { get; init; } = null!;
        public ArrangementDescription[] Arrangements { get; init; } = Array.Empty<ArrangementDescription>();
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
    private sealed record ArrangementDescription(string Family, double StartTime, double EndTime, int ChangedHandCount, int AddedHeadCount);
    private sealed record Counts(int Heads, int TimingGroups, int Flicks, int Holds, int Sliders,
                                int Clicks, double ClickFraction, double ClicksPerMinute,
                                double? MinimumClickClearanceMs, double? MedianClickClearanceMs, int ClicksOverlappingDirectionalGestures,
                                int Chords, double DualSustainOverlapMs, int SustainWithOppositeFlicks);
    private sealed record Difference(int MatchingHeads, int RemovedOrChangedHeads, int AddedOrChangedHeads, double ChangedHeadFraction);
    private sealed record SourceIdentityMetrics(int SourceCircleOnsets, int SourceCircleOnsetsWithManualHeads, double? SourceCircleOnsetRetentionFraction,
                                                int GeneratedSustains, int GeneratedSustainsWithinPrimaryWindow,
                                                int SourceDurationOnsets, int SourceDurationOnsetsWithManualHeads,
                                                double SourceDurationUnionMs, double SourceDurationCoveredMs, double? SourceDurationCoverageFraction,
                                                double ConvertedDurationOutsideSourceMs, int ManualHeadsAwayFromSourceOnsets);
    private sealed record PatternMetrics(ChordAngleHistogram ChordAngles, int HeadsAtOppositeSustainTail, int HeadsDuringOppositeSustain,
                                          int FlicksDuringOppositeSustain, int SustainsStartingDuringOppositeSustain,
                                          ChordDescription[] DirectionalChords, double[] OppositeSustainTailHeadTimes, double[] HeadsDuringOppositeSustainTimes,
                                          double[] FlicksDuringOppositeSustainTimes, double[] SustainsStartingDuringOppositeSustainTimes);
    private sealed record ChordAngleHistogram(int Pairs, int Coincident, int Near, int Small, int Medium, int Wide);
    private sealed record ChordDescription(double StartTime, double SeparationDegrees);
    private sealed record SourceDescription(string Kind, double StartTime, double EndTime, double TimingSectionStart, double BeatLength, float? PositionX, float? PositionY,
                                           int RepeatCount, double[] RepeatTimes, double[] TickTimes, bool ClapOrFinishAccent);
    private sealed record ObjectDescription(string Kind, double StartTime, double EndTime, string Side, float Angle, float[] SegmentArcs, double[] SegmentDurationWeights,
                                            float PrimaryHitAngle);
    private sealed record ExampleWindow(double StartTime, double EndTime, int ChangedHeads, ObjectDescription[] Standard, ObjectDescription[] Experimental);
}
