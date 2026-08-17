#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring.Legacy;
using osu.Game.Rulesets.Sticks.Beatmaps;
using osu.Game.Rulesets.Sticks.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Edit;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Sticks.Tests
{
    /// <summary>
    /// Covers persistence boundaries which codec-only tests cannot: the same database save and
    /// working-beatmap reload path used by the editor, followed by legacy package export/import.
    /// </summary>
    [HeadlessTest]
    public partial class SticksPersistenceIntegrationTest : OsuTestScene
    {
        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private RulesetStore originalRulesets { get; set; } = null!;

        private PersistenceRulesetStore? persistenceRulesets;
        private WorkingBeatmap reference = null!;
        private WorkingBeatmap authored = null!;
        private IBeatmap reopened = null!;
        private MemoryStream exportedPackage = null!;
        private string[] exportedEntries = Array.Empty<string>();
        private string exportedCarrierText = string.Empty;
        private string storedCarrierBeforeExport = string.Empty;
        private string storedCarrierAfterExport = string.Empty;
        private Guid originalSetId;
        private Guid importedSetId;
        private int importedOnlineSetId;
        private int importedOnlineBeatmapId;
        private string[] unmanagedSourceFilesBeforeExport = Array.Empty<string>();
        private string[] unmanagedSourceFilesAfterExport = Array.Empty<string>();
        private Guid[] unmanagedSourceBeatmapsBeforeExport = Array.Empty<Guid>();
        private Guid[] unmanagedSourceBeatmapsAfterExport = Array.Empty<Guid>();

        private void addRulesetSetupSteps()
        {
            AddStep("register package rulesets", () =>
            {
                persistenceRulesets = new PersistenceRulesetStore(originalRulesets);
                osu.Game.Beatmaps.Formats.Decoder.RegisterDependencies(persistenceRulesets);

                Realm.Write(realm =>
                {
                    foreach (RulesetInfo ruleset in persistenceRulesets.AvailableRulesets)
                    {
                        if (realm.Find<RulesetInfo>(ruleset.ShortName) == null)
                            realm.Add(ruleset.Clone());
                    }
                });
            });
        }

        private void addAuthoredDifficultySetupSteps()
        {
            AddStep("import reference package", () =>
            {
                reference = beatmaps.Import(new ImportTask(createReferencePackage(), "sticks-persistence-reference.osz"))
                                    .GetResultSafely().AsNonNull()
                                    .PerformRead(set => beatmaps.GetWorkingBeatmap(set.Beatmaps.Single(beatmap => beatmap.DifficultyName == "Reference")));
            });

            AddStep("create authored difficulty", () =>
            {
                authored = SticksEditorBootstrap.CreateDifficulty(
                    beatmaps,
                    Realm,
                    reference,
                    reference.BeatmapInfo.Ruleset,
                    new SticksRuleset().RulesetInfo,
                    retainConvertedObjects: false);
            });
        }

        private void addAuthorAndSaveSteps(bool seedPositiveOnlineBeatmapId = false)
        {
            AddStep("author objects in editor", () =>
            {
                IBeatmap playable = authored.GetPlayableBeatmap(new SticksRuleset().RulesetInfo);
                var editor = new EditorBeatmap(playable, beatmapInfo: authored.BeatmapInfo);
                foreach (SticksHitObject hitObject in createAuthoredObjects())
                    editor.Add(hitObject);

                if (seedPositiveOnlineBeatmapId)
                    editor.BeatmapInfo.OnlineID = 97531;

                // This is the exact save pairing used by Editor.Save().
                beatmaps.Save(editor.BeatmapInfo, editor.PlayableBeatmap);
            });
        }

        [TearDownSteps]
        public void RestoreDecoderRulesets()
        {
            AddStep("restore decoder rulesets", () =>
            {
                osu.Game.Beatmaps.Formats.Decoder.RegisterDependencies(originalRulesets);
                persistenceRulesets?.Dispose();
                persistenceRulesets = null;
            });
        }

        [Test]
        public void TestEditorSaveCloseAndReopenThroughBeatmapManager()
        {
            addRulesetSetupSteps();
            addAuthoredDifficultySetupSteps();
            addAuthorAndSaveSteps();

            AddStep("close and reopen working beatmap", () =>
            {
                BeatmapInfo persisted = beatmaps.QueryBeatmap(info => info.ID == authored.BeatmapInfo.ID).AsNonNull();
                WorkingBeatmap fresh = beatmaps.GetWorkingBeatmap(persisted, refetch: true);
                reopened = fresh.GetPlayableBeatmap(new SticksRuleset().RulesetInfo);
            });

            AddAssert("objects survive editor reopen", objectsAreIntact);
        }

        [Test]
        public void TestRestoreAuthoredDifficultyAfterExternalImportResetsRuleset()
        {
            addRulesetSetupSteps();
            addAuthoredDifficultySetupSteps();
            addAuthorAndSaveSteps();

            AddStep("simulate external import resetting mode", () =>
            {
                Realm.Write(realm =>
                {
                    BeatmapInfo liveBeatmap = realm.Find<BeatmapInfo>(authored.BeatmapInfo.ID).AsNonNull();
                    liveBeatmap.Ruleset = realm.Find<RulesetInfo>("osu").AsNonNull();
                });

                BeatmapInfo resetInfo = beatmaps.QueryBeatmap(info => info.ID == authored.BeatmapInfo.ID).AsNonNull();
                WorkingBeatmap reset = beatmaps.GetWorkingBeatmap(resetInfo, refetch: true);
                Assert.That(reset.BeatmapInfo.Ruleset.ShortName, Is.EqualTo("osu"));

                WorkingBeatmap restored = SticksEditorBootstrap.RestoreAuthoredDifficulty(
                    beatmaps,
                    Realm,
                    reset,
                    new SticksRuleset().RulesetInfo);

                Assert.That(restored.BeatmapInfo.Ruleset.ShortName, Is.EqualTo("sticks"));
                reopened = restored.GetPlayableBeatmap(new SticksRuleset().RulesetInfo);
            });

            AddAssert("objects survive identity restoration", objectsAreIntact);
        }

        [Test]
        public void TestAuthoredCarrierOszExportAndImportRoundTrip()
        {
            addRulesetSetupSteps();
            addAuthoredDifficultySetupSteps();
            addAuthorAndSaveSteps(seedPositiveOnlineBeatmapId: true);

            AddStep("export selected carrier package", () =>
            {
                BeatmapInfo persisted = beatmaps.QueryBeatmap(info => info.ID == authored.BeatmapInfo.ID).AsNonNull();
                BeatmapSetInfo unmanagedSource = persisted.BeatmapSet.AsNonNull();
                originalSetId = unmanagedSource.ID;
                unmanagedSourceFilesBeforeExport = unmanagedSource.Files.Select(file => file.Filename).Order(StringComparer.Ordinal).ToArray();
                unmanagedSourceBeatmapsBeforeExport = unmanagedSource.Beatmaps.Select(beatmap => beatmap.ID).Order().ToArray();
                storedCarrierBeforeExport = readStoredCarrier(persisted);
                exportedPackage = new MemoryStream();
                new SticksBeatmapPackageExporter(LocalStorage, persisted.ID)
                    .ExportToStream(unmanagedSource, exportedPackage, null);

                unmanagedSourceFilesAfterExport = unmanagedSource.Files.Select(file => file.Filename).Order(StringComparer.Ordinal).ToArray();
                unmanagedSourceBeatmapsAfterExport = unmanagedSource.Beatmaps.Select(beatmap => beatmap.ID).Order().ToArray();

                exportedPackage.Position = 0;
                using (var archive = new ZipArchive(exportedPackage, ZipArchiveMode.Read, leaveOpen: true))
                {
                    exportedEntries = archive.Entries.Select(entry => entry.FullName).ToArray();
                    ZipArchiveEntry carrier = archive.Entries.Single(entry => entry.FullName.EndsWith(".osu", StringComparison.OrdinalIgnoreCase));
                    using var reader = new StreamReader(carrier.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    exportedCarrierText = reader.ReadToEnd();
                }

                storedCarrierAfterExport = readStoredCarrier(beatmaps.QueryBeatmap(info => info.ID == authored.BeatmapInfo.ID).AsNonNull());
                exportedPackage.Position = 0;
            });

            AddAssert("only selected difficulty exported", () => exportedEntries.Count(entry => entry.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) == 1);
            AddAssert("source contains nested orphan fixture", () => unmanagedSourceFilesBeforeExport.Contains("orphan/nested-official.osu"));
            AddAssert("nested orphan difficulty omitted", () => !exportedEntries.Contains("orphan/nested-official.osu"));
            AddAssert("audio resource exported", () => exportedEntries.Contains("audio.wav"));
            AddAssert("background resource exported", () => exportedEntries.Contains("background.jpg"));
            AddAssert("custom sample exported", () => exportedEntries.Contains("custom.wav"));
            AddAssert("source carrier retained online beatmap id", () => storedCarrierBeforeExport.Contains("BeatmapID: 97531", StringComparison.Ordinal));
            AddAssert("source carrier retained online set id", () => storedCarrierBeforeExport.Contains("BeatmapSetID: 24680", StringComparison.Ordinal));
            AddAssert("export strips beatmap online id", () => !exportedCarrierText.Contains("BeatmapID:", StringComparison.Ordinal));
            AddAssert("export strips set online id", () => !exportedCarrierText.Contains("BeatmapSetID:", StringComparison.Ordinal));
            AddAssert("export does not mutate stored carrier", () => storedCarrierAfterExport == storedCarrierBeforeExport);
            AddAssert("export does not mutate unmanaged source files", () => unmanagedSourceFilesAfterExport.SequenceEqual(unmanagedSourceFilesBeforeExport));
            AddAssert("export does not mutate unmanaged source difficulties", () => unmanagedSourceBeatmapsAfterExport.SequenceEqual(unmanagedSourceBeatmapsBeforeExport));

            AddStep("import exported carrier package", () =>
            {
                var imported = beatmaps.Import(new ImportTask(exportedPackage, "sticks-authored-carrier.osz"))
                                       .GetResultSafely().AsNonNull();

                reopened = imported.PerformRead(set =>
                {
                    importedSetId = set.ID;
                    importedOnlineSetId = set.OnlineID;
                    BeatmapInfo sticksCarrier = set.Beatmaps.Single();
                    importedOnlineBeatmapId = sticksCarrier.OnlineID;
                    return beatmaps.GetWorkingBeatmap(sticksCarrier).GetPlayableBeatmap(new SticksRuleset().RulesetInfo);
                });
            });

            AddAssert("objects survive legacy export and import", objectsAreIntact);
            AddAssert("import creates a detached set", () => importedSetId != originalSetId);
            AddAssert("imported set has no official online id", () => importedOnlineSetId <= 0);
            AddAssert("imported beatmap has no official online id", () => importedOnlineBeatmapId <= 0);
            AddAssert("source online set survives collision import", () => Realm.Run(realm =>
                realm.Find<BeatmapSetInfo>(originalSetId) is BeatmapSetInfo set && !set.DeletePending && set.OnlineID == 24680));
        }

        private string readStoredCarrier(BeatmapInfo beatmap)
        {
            BeatmapSetInfo set = beatmap.BeatmapSet.AsNonNull();
            string path = beatmap.Path.AsNonNull();
            var usage = set.Files.Single(file => string.Equals(file.Filename, path, StringComparison.OrdinalIgnoreCase));
            using Stream stream = LocalStorage.GetStorageForDirectory("files").GetStream(usage.File.GetStoragePath()).AsNonNull();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private bool objectsAreIntact()
        {
            SticksHitObject[] objects = reopened.HitObjects.Cast<SticksHitObject>().ToArray();
            if (objects.Length != 3)
                return false;

            if (objects[0] is not SticksFlick flick
                || flick.Side != StickSide.Left
                || Math.Abs(flick.Angle - 45) > 0.001)
                return false;

            if (objects[1] is not SticksHold hold
                || hold.Side != StickSide.Right
                || Math.Abs(hold.Angle - 225) > 0.001
                || Math.Abs(hold.Duration - 1250) > 0.001)
                return false;

            return objects[2] is SticksSlider slider
                   && slider.Side == StickSide.Left
                   && Math.Abs(slider.Angle - 15) <= 0.001
                   && Math.Abs(slider.Duration - 2500) <= 0.001
                   && slider.HasCustomSegments
                   && slider.SegmentArcAngles.SequenceEqual(new[] { 90f, -135f, 180f });
        }

        private static SticksHitObject[] createAuthoredObjects()
        {
            var slider = new SticksSlider
            {
                StartTime = 3500,
                Duration = 2500,
                Side = StickSide.Left,
                Angle = 15,
            };
            slider.SetCustomSegments(new[] { 90f, -135f, 180f });

            return
            [
                new SticksFlick
                {
                    StartTime = 1000,
                    Side = StickSide.Left,
                    Angle = 45,
                },
                new SticksHold
                {
                    StartTime = 1750,
                    Duration = 1250,
                    Side = StickSide.Right,
                    Angle = 225,
                },
                slider,
            ];
        }

        private static MemoryStream createReferencePackage()
        {
            const string beatmap = """
                                   osu file format v14

                                   [General]
                                   AudioFilename: audio.wav
                                   Mode: 0

                                   [Editor]
                                   DistanceSpacing: 1
                                   BeatDivisor: 4
                                   GridSize: 4
                                   TimelineZoom: 1

                                   [Metadata]
                                   Title:Sticks persistence integration
                                   Artist:Test
                                   Creator:Zankai LLC
                                   Version:Reference
                                   BeatmapID:13579
                                   BeatmapSetID:24680

                                   [Difficulty]
                                   HPDrainRate:5
                                   CircleSize:5
                                   OverallDifficulty:5
                                   ApproachRate:5
                                   SliderMultiplier:1.4
                                   SliderTickRate:1

                                   [TimingPoints]
                                   0,500,4,2,0,100,1,0
                                   2500,375,4,2,0,100,1,0

                                   [Events]
                                   0,0,"background.jpg",0,0

                                   [HitObjects]
                                   256,192,1000,1,0,0:0:0:100:sticks-v1~f~x~0.wav
                                   """;

            var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("reference.osu").Open(), new UTF8Encoding(false)))
                    writer.Write(beatmap);

                // lazer stores nested .osu entries as files but deliberately does not model them
                // as difficulties. The exporter must still exclude this collision-bearing orphan.
                using (var writer = new StreamWriter(archive.CreateEntry("orphan/nested-official.osu").Open(), new UTF8Encoding(false)))
                    writer.Write(beatmap.Replace("Version:Reference", "Version:Nested orphan", StringComparison.Ordinal)
                                                .Replace("BeatmapID:13579", "BeatmapID:86420", StringComparison.Ordinal));

                using (Stream audio = archive.CreateEntry("audio.wav").Open())
                    audio.Write(createSilentWave());

                using (Stream background = archive.CreateEntry("background.jpg").Open())
                    background.Write(new byte[] { 0xff, 0xd8, 0xff, 0xd9 });

                using (Stream customSample = archive.CreateEntry("custom.wav").Open())
                    customSample.Write(createSilentWave());

                // This deliberately occupies Sticks' reserved carrier namespace while remaining
                // ordinary source-map data. The explicit editor conversion must ignore it.
                using (Stream markerLikeSample = archive.CreateEntry("sticks-v1~f~x~0.wav").Open())
                    markerLikeSample.Write(createSilentWave());
            }

            stream.Position = 0;
            return stream;
        }

        private static byte[] createSilentWave()
        {
            // PCM, mono, 8 kHz, 16-bit, one silent sample.
            return
            [
                0x52, 0x49, 0x46, 0x46, 0x26, 0x00, 0x00, 0x00,
                0x57, 0x41, 0x56, 0x45, 0x66, 0x6d, 0x74, 0x20,
                0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
                0x40, 0x1f, 0x00, 0x00, 0x80, 0x3e, 0x00, 0x00,
                0x02, 0x00, 0x10, 0x00, 0x64, 0x61, 0x74, 0x61,
                0x02, 0x00, 0x00, 0x00, 0x00, 0x00,
            ];
        }

        private sealed class PersistenceRulesetStore : RulesetStore
        {
            private readonly RulesetInfo[] availableRulesets;

            public override IEnumerable<RulesetInfo> AvailableRulesets => availableRulesets;

            public PersistenceRulesetStore(RulesetStore original)
            {
                RulesetInfo standard = new PersistenceStandardRuleset().RulesetInfo;
                RulesetInfo sticks = new SticksRuleset().RulesetInfo;

                availableRulesets = original.AvailableRulesets
                                            .Where(ruleset => ruleset.ShortName != standard.ShortName && ruleset.ShortName != sticks.ShortName)
                                            .Append(standard)
                                            .Append(sticks)
                                            .ToArray();
            }
        }
    }

    /// <summary>
    /// Test-only mode-0 implementation used because the standalone external-ruleset test package
    /// intentionally does not take a dependency on osu!standard's ruleset assembly.
    /// </summary>
    public sealed class PersistenceStandardRuleset : Ruleset, ILegacyRuleset
    {
        public override string Description => "osu!";
        public override string ShortName => "osu";

        int ILegacyRuleset.LegacyID => 0;

        public override IEnumerable<Mod> GetModsFor(ModType type) => Array.Empty<Mod>();

        public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null) => null!;

        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new PassThroughBeatmapConverter(beatmap);

        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new EmptyDifficultyCalculator(RulesetInfo, beatmap);

        ILegacyScoreSimulator ILegacyRuleset.CreateLegacyScoreSimulator() => new EmptyLegacyScoreSimulator();

        private sealed class PassThroughBeatmapConverter : IBeatmapConverter
        {
            public event Action<HitObject, IEnumerable<HitObject>>? ObjectConverted
            {
                add { }
                remove { }
            }

            public IBeatmap Beatmap { get; }

            public PassThroughBeatmapConverter(IBeatmap beatmap)
            {
                Beatmap = beatmap;
            }

            public bool CanConvert() => true;

            public IBeatmap Convert(CancellationToken cancellationToken = default) => Beatmap;
        }

        private sealed class EmptyLegacyScoreSimulator : ILegacyScoreSimulator
        {
            public LegacyScoreAttributes Simulate(IWorkingBeatmap workingBeatmap, IBeatmap playableBeatmap) => new LegacyScoreAttributes
            {
                MaxCombo = playableBeatmap.HitObjects.Count,
            };

            public double GetLegacyScoreMultiplier(IReadOnlyList<Mod> mods, LegacyBeatmapConversionDifficultyInfo difficulty) => 1;
        }

        private sealed class EmptyDifficultyCalculator : DifficultyCalculator
        {
            public EmptyDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
                : base(ruleset, beatmap)
            {
            }

            protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills) => new DifficultyAttributes(mods, 0)
            {
                MaxCombo = beatmap.HitObjects.Count,
            };

            protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods) => Array.Empty<DifficultyHitObject>();

            protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods) => Array.Empty<Skill>();
        }
    }
}
