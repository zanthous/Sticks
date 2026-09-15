# Gameplay and song-select resource checks

`SticksGameplayResourceTest` runs in the normal test suite. It covers:

- 24 gameplay attempts, mixing early exits and completion, with real replay recorders. A loaded flick-activation settings slider verifies that recording locks it and background disposal safely unlocks it. The cached source, shared settings, skin provider and score history stay alive while weak references verify that retired gameplay objects are collected.
- 60,000 hit effects and 120,000 cursor-trail positions after warming the pools. Effect drawables must be reused, retained managed growth must stay below 8 MiB, and gameplay must be collectible after exit.
- 32 skin replacements during gameplay. Obsolete skins and textures must be collectible. Hidden sprite draw nodes may cache the final texture wrapper until teardown; the graphics run additionally requires all replaced native textures to be released.

Run the focused headless checks:

```sh
dotnet test osu.Game.Rulesets.Sticks.Tests -c Release --filter FullyQualifiedName~SticksGameplayResourceTest
```

The separate graphics test is explicit because it requires a working desktop renderer:

```sh
dotnet test osu.Game.Rulesets.Sticks.Tests -c Release --filter FullyQualifiedName~SticksGameplayGraphicsResourceTest
```

It runs the same gameplay scenarios with rendering enabled, verifies that custom textures were actually uploaded and bound, and checks their native allocation lifetimes. Configuration and databases use a temporary test directory; the renderer cache stays beside the runner. On Linux, `SDL_VIDEODRIVER=offscreen` can use Mesa software rendering when available; this exercises OpenGL resource cleanup but does not measure a hardware driver's VRAM.

Headless passes alone do not verify native graphics memory. These checks cover ruleset gameplay and recording lifetimes; they do not automate the full song-selection and Player-screen UI.

Verified on 2026-09-16: all three scenarios passed headlessly against lazer 2026.804.2 and Tachyon 2026.911.0, and with offscreen OpenGL using Mesa software rendering. The graphics run collected all 24 retired gameplay instances and their recorders, retained 2.22 MiB during the warmed effects stress phase (below the 8 MiB bound), and released all 32 replaced native skin textures. All tracked gameplay resources were collectible after teardown.

## Song-select difficulty checks

`SticksSongSelectResourceTest` uses the actual song-select difficulty display and `BeatmapDifficultyCache`, with imported standard maps and real Sticks conversion, difficulty and performance calculations. One test covers:

- Interrupting active difficulty calculations by changing the selected song, mods, or both.
- Changing Difficulty Adjust's speed setting on the existing mod instance, then restoring it.
- 54 selections across three songs and six mod states: no mods, Parity, Solo, Encore, Parity + Encore and Difficulty Adjust. Stars and maximum combo must match independent calculations; revisiting warmed combinations must use the cache.
- Collection of retired calculators, converted beatmaps, converters, rating bindings, mod instances and the difficulty display. The source songs and populated shared cache stay alive during this check.

```sh
dotnet test osu.Game.Rulesets.Sticks.Tests -c Release --filter FullyQualifiedName~SticksSongSelectResourceTest
```

Selection changes use song select's beatmap/mod bindables. This exercises the difficulty display and calculation lifecycle, not the full carousel, preview audio or background artwork.

Verified on 2026-09-16 against lazer 2026.804.2 and Tachyon 2026.911.0: both passed, including all three cancellations, correct selection-specific results, warm-cache reuse and zero retained tracked resources after closing the display. Both compatibility builds completed without warnings or errors.
