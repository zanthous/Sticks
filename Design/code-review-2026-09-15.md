# Code review — 2026-09-15

The sweep covered conversion and mod composition, input and judgements, difficulty
and performance, replay persistence, editor changes and authored-map storage,
skinning and drawable lifetimes, and the test suite. This was source inspection,
headless testing and targeted reproductions; it was not a controller playtest or
a visual check on every graphics backend.

## Replay storage and export limitations

**Correction: the legacy bridge's missing buttons are not an active high-priority
export bug.** Stock lazer and Tachyon only create `.osr` data for rulesets with
online IDs 0–3. [SticksRuleset](../osu.Game.Rulesets.Sticks/SticksRuleset.cs)
uses ID −1. Its editor-compatible implementation does not change that identity.
`Player.ImportScore()` therefore saves the score without an `.osr` file, and
`LegacyScoreExporter.ExportToStream()` can only copy an existing score file.
The prior finding checked frame encoding without tracing this save/export gate.

[SticksReplayFrame](../osu.Game.Rulesets.Sticks/Replays/SticksReplayFrame.cs)
still omits all six buttons from `ToLegacy()` and restores them as false in
`FromLegacy()`: that field currently stores the right stick's axes. This is a
limitation of the legacy bridge, not the local recording. Changing the packing
alone would not make Sticks replays exportable. Portable replay support needs an
actual export/import path in addition to a suitable data format.

Local `.stkr` files are Sticks' replay storage. They preserve all four raw axes
and all six buttons. Version 5 also stores one activation threshold in the header
(four bytes per replay); frame sizes are unchanged. Recording locks the setting
until its recorder is disposed, including overlapping retry lifetimes. Playback
uses the saved threshold for interpolation, flick detection and derived recharge.
Older recordings without settings use default 95% activation and 65% recharge,
independent of the viewer's preference. Original settings that were never saved
cannot be recovered.

## Cleanup applied

- **Fixed repeated work in timed difficulty calculation.** Every emitted prefix
  previously rescanned all earlier objects six times for tracking/tail counts,
  plus separate head and ordering scans. Counts now accumulate when objects are
  processed, and ordering checks inspect only appended objects during a reusable
  timed traversal. Full calculations still reset after editor mutations; an
  out-of-order append invalidates reuse. Cancellation remains checked.
- **Removed retired display machinery.** Deleted bracket-style growth, rehearsal
  and upcoming-path helpers, and unused path aliases. Removed chord-link metadata,
  its sorting pass in conversion/editor updates, and its verifier checks. The
  actual chord geometry and modern purple-stack rendering remain covered.
- **Removed 10 obsolete or redundant tests**, plus dead link assertions inside
  otherwise useful tests. Retargeted the old fixed-angle grading test to the
  actual per-object gameplay grading method. Extended the existing timed-prefix
  test to compare combo and scoring-object counts with fresh calculations.
- **Corrected the README** to remove Brackets and describe Solo and the beginner
  coordination allowance.

Old hold/carrier decoding, hidden historical mod acronyms, replay-version readers,
and their compatibility tests remain necessary. They protect existing user data.

## Measurements and verification

Release benchmark, same synthetic map generator and machine, separate warm runs:

| Objects | Before | After | Before allocation | After allocation |
| ---: | ---: | ---: | ---: | ---: |
| 500 | 235 ms | 20 ms | 99.7 MiB | 7.8 MiB |
| 1,000 | 643 ms | 52 ms | 382.7 MiB | 15.7 MiB |
| 2,000 | 811 ms | 198 ms | 1,497.9 MiB | 31.6 MiB |
| 4,000 | 3,248 ms | 418 ms | 5,925.8 MiB | 63.4 MiB |

All four final ratings were unchanged. These timings are individual measurements,
not latency guarantees. Allocations measure cumulative managed allocations, not
peak resident memory.

The cleanup left 898 normal tests, with one additional explicit
benchmark excluded from normal runs. The benchmark is
`SticksDifficultyModelTest.BenchmarkDenseTimedDifficulty`.

Release builds completed without warnings or errors, and all 898 normal tests
passed on the package baseline (2026.730.0), stable lazer (2026.804.2-lazer), and
Tachyon (2026.911.0-tachyon). Existing editor seek, undo, large-selection, disposal,
scoring, conversion and persistence regressions are included. Local tests used an
in-process NUnit runner because the sandbox blocks VSTest's socket transport.

The replay-threshold follow-up passes 908 normal tests on the package baseline,
stable lazer and Tachyon, with clean Release builds. After the final replay-loader
API cleanup, all 13 persistence tests were also rerun on the package baseline.
Checks cover the four older file versions, header-only settings storage, invalid
threshold data, original/replayed scoring, and unlocking across recorder retries.
