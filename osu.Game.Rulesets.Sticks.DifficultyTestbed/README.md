# Sticks star-rating testbed

- `cases.json` records each calibration map, why it is useful, its preferred range, and the history of difficulty changes made because of it.
- Source beatmaps are not committed. Each case uses the SHA-256 filename already present in lazer's local `files` store.
- The runner converts locally available maps with the current Sticks converter and reports current stars, skill values, and drift from the last recorded milestone.
- Preferred ranges are review guidance rather than hard assertions, so experimental changes remain easy to compare.

Run from the repository root:

```text
dotnet run --project osu.Game.Rulesets.Sticks.DifficultyTestbed
```

The usual stable and development lazer stores are detected automatically. Use `--files-root "path/to/osu/files"` to supply another store; the option may be repeated.

When adding a case:

1. Add the source map's SHA-256 and identifying metadata.
2. Record the specific mechanic and why the current rating feels wrong.
3. Use a broad target range until playtesting supports something narrower.
4. After a meaningful model change, append a milestone with the before/after rating and its design reason.

## Compare experimental converters on real maps

```text
dotnet run --project osu.Game.Rulesets.Sticks.DifficultyTestbed -c Release -- --compare-converters mapreference/standard --output mapreference/standard/comparison.json
```

The input can be a directory (searched recursively), an `.osu`, or an `.osz`; repeat `--compare-converters` for multiple inputs. Only mode-0 maps are converted. Identical SHA-256 sources are evaluated once, and archives are read without extracting media. Keep downloaded maps and generated reports under the gitignored `mapreference/` directory.

The runner compares Standard and Duet; add `--include-parity` for Parity and `--include-combined` for Parity + Duet. These flags can be used independently or together. It uses the source format version, legacy gameplay offsets, `FlatWorkingBeatmap.GetPlayableBeatmap` with the actual mods, and `SticksDifficultyCalculator.Calculate(mods)` for in-game Sticks stars. Authored Sticks maps are labelled `authored-bypass` and checked for identical output across modes.

The console reports head/flick/hold/slider counts, two-stick chords, milliseconds with both sticks sustaining, and interior flicks played opposite an active sustain. It prints three eight-second windows with the most differing objects. JSON includes metadata, source hash/IDs, stars, all converted objects, and both versions of those windows. `changedHeadFraction` is the fraction of the multiset union that does not match, comparing kind, timing, side, angle and slider arcs to 0.001 precision, plus exact timed segment weights. Added chord partners count as changes even when the existing head is preserved.

The process exits unsuccessfully for unreadable inputs, no evaluated maps, or validation failures. Validation checks chronological order, finite geometry, positive durations, carrier encode/decode (including segment timing), procedural same-side sustain overlaps and the 120 degrees/second generated-slider speed limit on every segment. Authored overlaps and speeds are measured but do not fail those procedural constraints.

## Parity angle audit

The original reset audit requires the original hard-correction converter build. Its capture includes the actual Standard, Parity, Duet and Parity + Duet gameplay conversions, each head's local beat length and slider endpoint:

```sh
dotnet run --project osu.Game.Rulesets.Sticks.DifficultyTestbed -c Release -- --audit-parity mapreference/standard --audit-parity mapreference/high-cs --output mapreference/parity-audit/objects.json
python3 osu.Game.Rulesets.Sticks.DifficultyTestbed/analyse_parity.py mapreference/parity-audit/objects.json --output mapreference/parity-audit/analysis.json
```

Inputs are deduplicated by SHA-256; authored carriers are excluded. The analysis checks its parity replay against the actual mod output before comparing reset variants. It measures absolute-angle concentration, exact turn sizes and repeated two-direction flick patterns. Reset comparisons use the same eligible transitions, so changing the reset threshold does not change the denominator. Per-map summaries accompany pooled results to keep long maps from hiding differences between maps. These diagnostics do not change the gameplay converter.

### Compare actual parity exports

To compare a future parity revision with the current export, capture it to a new location and compare the two actual gameplay outputs:

Both builds must have matching Standard/Duet behavior. The current baseline includes chord alignment and Duet timed paths; older exports predate these conversion changes. Slider exports include segment duration weights so comparisons can detect timing changes.

```sh
dotnet run --project osu.Game.Rulesets.Sticks.DifficultyTestbed -c Release -- --audit-parity mapreference/standard --audit-parity mapreference/high-cs --output mapreference/parity-next/objects.json
python3 osu.Game.Rulesets.Sticks.DifficultyTestbed/compare_parity.py mapreference/duet-unsmoothed/objects.json mapreference/parity-next/objects.json --output mapreference/parity-next/comparison.json
```

`compare_parity.py` requires identical source SHA-256 sets and all four modes. It checks that Standard and Duet objects are unchanged and that parity preserves types, timing, stick assignment and slider motion. It reports exact-135° turns, three-flick returns, 45° grid runs, movement quantiles and turns below 90°, with pooled results, equal-map medians and per-map changes. Eight-local-beat windows also compare source and output turn variation on each stick. These windows are anchored to their first head and restart at observed tempo changes; signed mean/std values use the −180°/180° branch cut. This comparison does not replay the new converter; `analyse_parity.py` remains a replay of the original hard correction and should only analyse its original export.
