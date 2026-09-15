#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Sticks.Mods;
using osu.Game.Screens.Select;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Sticks.Tests
{
    [HeadlessTest]
    [NonParallelizable]
    public partial class SticksSongSelectResourceTest : OsuTestScene
    {
        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        private readonly CountingDifficultyCache cache = new CountingDifficultyCache();
        private readonly CalculationProbe probe = new CalculationProbe();
        private readonly Dictionary<(int Song, int Mods), (double Stars, int MaxCombo)> expectedRatings = new();
        private WorkingBeatmap[] songs = Array.Empty<WorkingBeatmap>();
        private BeatmapTitleWedge.DifficultyDisplay? display;
        private (double Stars, int MaxCombo) expected;
        private CalculationGate? gate;

        // Only the test ruleset uses this observer. Its conversion and difficulty logic are
        // inherited unchanged; the hook observes lifetimes and pauses an actual calculation.
        private static CalculationProbe? activeProbe;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs<BeatmapDifficultyCache>(cache);
            dependencies.Cache(new OverlayColourProvider(OverlayColourScheme.Pink));
            return dependencies;
        }

        private StarRatingDisplay stars => display!.ChildrenOfType<StarRatingDisplay>().Single();

        [Test]
        public void TestSongAndModSwitchingCancelsCalculationsAndReleasesResources()
        {
            int warmCalculations = 0;
            SticksModDifficultyAdjust? difficultyAdjust = null;
            double previousStars = 0;

            AddStep("import three standard songs and load the song-select cache", () =>
            {
                activeProbe = probe;
                // Standalone external-ruleset test hosts do not necessarily register osu!standard.
                Realm.Write(realm => realm.Add(new OsuRuleset().RulesetInfo.Clone(), update: true));
                songs = Enumerable.Range(0, 3).Select(index =>
                {
                    using var package = createSongPackage(index);
                    return beatmaps.Import(new ImportTask(package, $"song-select-{index}.osz")).GetResultSafely()!
                                   .PerformRead(set => beatmaps.GetWorkingBeatmap(set.Beatmaps.Single()));
                }).ToArray();
                // RulesetInfo equality only compares ShortName. Move off Sticks first so
                // bindables accept the instrumented instance rather than keeping the old one.
                Ruleset.Value = new OsuRuleset().RulesetInfo;
                Ruleset.Value = new ObservedSticksRuleset().RulesetInfo;
                Add(cache);
            });
            AddUntilStep("cache loaded", () => cache.IsLoaded);
            AddStep("load real song-select difficulty display", () => Add(display = new BeatmapTitleWedge.DifficultyDisplay()));
            AddUntilStep("difficulty display loaded", () => display!.IsLoaded);

            // Interrupt in-flight work by changing the song, the mods, and then both.
            // A gate makes these real cancellations deterministic even on fast machines.
            for (int i = 0; i < 3; i++)
            {
                int cycle = i;
                AddStep($"start calculation to interrupt {i + 1}", () =>
                {
                    cache.Clear();
                    gate = new CalculationGate();
                    probe.NextGate = gate;
                    selectSong(cycle, 1);
                });
                AddUntilStep("calculation reached Sticks difficulty model", () => gate!.Started.Task.IsCompleted);
                AddStep("switch selection while calculation is active", () =>
                    selectSong(cycle == 1 ? cycle : (cycle + 1) % songs.Length, cycle == 0 ? 1 : 2));
                AddWaitStep("allow selection and mod cancellation callbacks", 3);
                AddStep("release interrupted calculation", () => gate!.Release.TrySetResult(true));
                AddUntilStep("old calculation observed cancellation", () => probe.CancelledCalculations == cycle + 1);
                AddUntilStep("stars and combo match the new selection", currentRatingIsCorrect);
            }

            // Changing a setting on the existing mod object must update the displayed result,
            // not reuse the previous settings' cache entry.
            AddStep("select difficulty adjust", () =>
            {
                selectSong(0, 5);
                difficultyAdjust = (SticksModDifficultyAdjust)SelectedMods.Value.Single();
            });
            AddUntilStep("initial DA difficulty displayed", currentRatingIsCorrect);
            AddStep("change DA speed without replacing the mod", () =>
            {
                previousStars = stars.Current.Value.Stars;
                difficultyAdjust!.SpeedChange.Value = 1.4;
                expected = calculateExpected();
                Assert.That(expected.Stars, Is.Not.EqualTo(previousStars));
            });
            AddUntilStep("DA setting change recalculated", currentRatingIsCorrect);
            AddStep("restore DA speed on the same mod", () =>
            {
                difficultyAdjust!.SpeedChange.Value = 1;
                expected = calculateExpected();
            });
            AddUntilStep("original DA rating restored", currentRatingIsCorrect);
            AddStep("release test's DA reference", () => difficultyAdjust = null);

            // Warm each actual song/mod combination once, then revisit it twice using new
            // mod instances. Cached attributes may stay alive, converted object graphs may not.
            for (int pass = 0; pass < 3; pass++)
            {
                for (int mode = 0; mode < 6; mode++)
                {
                    for (int song = 0; song < 3; song++)
                    {
                        int selectedSong = song;
                        int selectedMode = mode;
                        AddStep($"select song {song + 1}, mod {mode}, pass {pass + 1}", () => selectSong(selectedSong, selectedMode));
                        AddUntilStep("selected song/mod rating displayed", currentRatingIsCorrect);
                    }
                }

                if (pass == 0)
                    AddStep("record warm cache calculations", () => warmCalculations = probe.CalculatorsCreated);
                else
                    AddAssert("revisits use existing difficulty entries", () => probe.CalculatorsCreated == warmCalculations);
            }

            AddStep("leave difficulty display while keeping song cache alive", () =>
            {
                trackSelection();
                probe.Track(display!, "difficulty display");
                Remove(display!, true);
                display = null;
                Beatmap.SetDefault();
                SelectedMods.SetDefault();
            });
            AddUntilStep("difficulty tasks have settled", () => cache.PendingComputations == 0);
            AddWaitStep("drain selection and disposal callbacks", 20);
            AddStep("retired calculation and selection resources are collectible", () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                string[] survivors = probe.References.Where(item => item.Reference.IsAlive).Select(item => item.Name).ToArray();
                TestContext.Progress.WriteLine($"Song select: 54 selections, 3 interrupted calculations, {probe.CalculatorsCreated} calculators created; " +
                                               $"{survivors.Length}/{probe.References.Count} tracked references still alive.");
                Assert.That(survivors, Is.Empty,
                    string.Join(", ", survivors.GroupBy(name => name).Select(group => $"{group.Key}: {group.Count()}")));
                Assert.That(cache.IsAlive, Is.True, "The shared difficulty cache must remain alive during collection checks.");
                Assert.That(songs.All(song => song.Beatmap.HitObjects.Count > 0), Is.True, "Keep cached source maps alive too.");
            });
        }

        private void selectSong(int song, int mode)
        {
            trackSelection();
            Beatmap.Value = songs[song];
            SelectedMods.Value = createMods(mode);
            if (!expectedRatings.TryGetValue((song, mode), out var rating))
                expectedRatings[(song, mode)] = rating = calculateExpected();
            expected = rating;
        }

        private void trackSelection()
        {
            if (display?.IsLoaded == true)
                probe.Track(stars.Current, "retired rating bindable");
            foreach (var mod in SelectedMods.Value)
                probe.Track(mod, "selected mod");
        }

        private (double Stars, int MaxCombo) calculateExpected()
        {
            var attributes = new SticksRuleset().CreateDifficultyCalculator(Beatmap.Value).Calculate(SelectedMods.Value.ToArray());
            // Retaining attributes here would also retain the selected mod instances under test.
            return (attributes.StarRating, attributes.MaxCombo);
        }

        private bool currentRatingIsCorrect() => stars.Current.Value.DifficultyAttributes is SticksDifficultyAttributes attributes
            && cache.MatchesSelection(Beatmap.Value.BeatmapInfo, Ruleset.Value, SelectedMods.Value, attributes)
            && Math.Abs(stars.Current.Value.Stars - expected.Stars) < 0.0000001
            && stars.Current.Value.MaxCombo == expected.MaxCombo;

        private static Mod[] createMods(int mode) => mode switch
        {
            0 => Array.Empty<Mod>(),
            1 => new Mod[] { new SticksModParity() },
            2 => new Mod[] { new SticksModSolo() },
            3 => new Mod[] { new SticksModEncore() },
            4 => new Mod[] { new SticksModParity(), new SticksModEncore() },
            5 => new Mod[] { new SticksModDifficultyAdjust() },
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        [TearDownSteps]
        public void TearDownSongSelect() => AddStep("release calculation observer", releasePendingCalculation);

        private void releasePendingCalculation()
        {
            gate?.Release.TrySetResult(true);
            if (ReferenceEquals(activeProbe, probe))
                activeProbe = null;
        }

        protected override void Dispose(bool isDisposing)
        {
            // Also release the gate when a step fails before normal teardown is reached.
            releasePendingCalculation();
            base.Dispose(isDisposing);
        }

        private static MemoryStream createSongPackage(int index)
        {
            var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(archive.CreateEntry("song.osu").Open(), new UTF8Encoding(false)))
            {
                writer.WriteLine($"""
                                  osu file format v14

                                  [General]
                                  Mode:0

                                  [Metadata]
                                  Title:Song select resource test {index}
                                  Artist:Test
                                  Creator:Test
                                  Version:Conversion
                                  BeatmapID:0
                                  BeatmapSetID:-1

                                  [Difficulty]
                                  HPDrainRate:5
                                  CircleSize:4
                                  OverallDifficulty:6
                                  ApproachRate:8
                                  SliderMultiplier:1.4
                                  SliderTickRate:1

                                  [TimingPoints]
                                  0,{360 - index * 40},4,2,0,100,1,0
                                  18000,-50,4,2,0,100,0,0

                                  [HitObjects]
                                  """);
                for (int i = 0; i < 192; i++)
                {
                    int x = 48 + i * 113 % 416;
                    int y = 48 + i * 79 % 288;
                    int time = 1000 + i * (180 - index * 20);
                    writer.WriteLine(i % 12 == 0
                        ? $"{x},{y},{time},2,0,L|{512 - x}:{384 - y},2,140"
                        : $"{x},{y},{time},1,0,0:0:0:0:");
                }
            }
            stream.Position = 0;
            return stream;
        }

        private partial class CountingDifficultyCache : BeatmapDifficultyCache
        {
            public int PendingComputations;

            // Equal star values alone do not prove the display has switched songs/mods.
            public bool MatchesSelection(BeatmapInfo beatmap, RulesetInfo ruleset, IEnumerable<Mod> mods, DifficultyAttributes displayed) =>
                CheckExists(new DifficultyCacheLookup(beatmap, ruleset, mods), out var cached)
                && ReferenceEquals(cached?.DifficultyAttributes, displayed);

            protected override async Task<StarDifficulty?> ComputeValueAsync(DifficultyCacheLookup lookup, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref PendingComputations);
                try
                {
                    return await base.ComputeValueAsync(lookup, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref PendingComputations);
                }
            }
        }

        public class ObservedSticksRuleset : SticksRuleset
        {
            public ObservedSticksRuleset()
            {
                RulesetInfo.InstantiationInfo = typeof(ObservedSticksRuleset).AssemblyQualifiedName!;
            }

            public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
            {
                var converter = base.CreateBeatmapConverter(beatmap);
                activeProbe?.Track(converter, "converter");
                return converter;
            }

            public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => activeProbe is { } observer
                ? new ObservedDifficultyCalculator(RulesetInfo, beatmap, observer)
                : base.CreateDifficultyCalculator(beatmap);
        }

        private sealed class ObservedDifficultyCalculator : SticksDifficultyCalculator
        {
            private readonly CalculationProbe probe;

            public ObservedDifficultyCalculator(RulesetInfo ruleset, IWorkingBeatmap beatmap, CalculationProbe probe)
                : base(ruleset, beatmap)
            {
                this.probe = probe;
                Interlocked.Increment(ref probe.CalculatorsCreated);
                probe.Track(this, "difficulty calculator");
                probe.Track(beatmap, "calculation working beatmap");
            }

            protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
            {
                probe.Track(beatmap, "converted beatmap");
                var interrupted = beatmap.HitObjects.Count > 0 ? Interlocked.Exchange(ref probe.NextGate, null) : null;
                if (interrupted != null)
                {
                    interrupted.Started.TrySetResult(true);
                    if (!interrupted.Release.Task.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("Test did not release the interrupted difficulty calculation.");
                }
                try
                {
                    return base.CreateDifficultyAttributes(beatmap, mods, skills);
                }
                catch (OperationCanceledException)
                {
                    if (interrupted != null)
                        Interlocked.Increment(ref probe.CancelledCalculations);
                    throw;
                }
            }
        }

        private sealed class CalculationGate
        {
            public readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class CalculationProbe
        {
            public readonly ConcurrentQueue<(string Name, WeakReference Reference)> References = new();
            public CalculationGate? NextGate;
            public int CancelledCalculations;
            public int CalculatorsCreated;

            public void Track(object target, string name) => References.Enqueue((name, new WeakReference(target)));
        }
    }
}
