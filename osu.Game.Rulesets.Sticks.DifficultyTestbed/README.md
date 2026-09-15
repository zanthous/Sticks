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

## Compare conversion on real maps

```text
dotnet run --project osu.Game.Rulesets.Sticks.DifficultyTestbed -c Release -- --compare-converters mapreference/standard --output mapreference/standard/comparison.json
```

The input can be a directory (searched recursively), an `.osu`, or an `.osz`; repeat `--compare-converters` for multiple inputs. Only mode-0 maps are converted. Identical SHA-256 sources are evaluated once, and archives are read without extracting media. Keep downloaded maps and generated reports under the gitignored `mapreference/` directory.

The runner compares **Default** and **Encore** (click accents and Slice follow-ups); add `--include-parity` to also compare **Parity** and **ParityEncore**. Default now uses the Counterpoint arrangement pass, and Parity applies its angle processing on that base. Add `--include-legacy-base` for **LegacyBase**, **LegacyBaseEncore**, and, when Parity is requested, **LegacyBaseParity** and **LegacyBaseParityEncore**. These explicitly disable Counterpoint and select the former Duet strategies through testbed-only reference mods. Current modes use an observer-only mod for arrangement diagnostics; it does not enable or select a strategy.

`--include-combined` remains an alias for `--include-parity`. The retired `--include-counterpoint` and `--legacy-duet-base` flags now print a notice and alias `--include-legacy-base`: they never rename historical output to Default or export duplicate Counterpoint/default modes. Schema 5 records `defaultConversionStrategy`, `includesLegacyBase` and each mode's `conversionStrategy` to make this distinction explicit.

Conversion uses the source format version, legacy gameplay offsets, `FlatWorkingBeatmap.GetPlayableBeatmap` with the actual mods, and `SticksDifficultyCalculator.Calculate(mods)` for in-game Sticks stars. Authored Sticks maps are labelled `authored-bypass` and checked for identical output across modes.

Source-speed mapping is part of every default conversion, including Parity. `sliderBursts` records each changed source slider's time range, original source speed, and converted speed before and after the change. These sliders may exceed the generated-slider limit of 120°/s, up to 720°/s for both continuous movement and reversals. Generated accompaniment retains its normal limit. See the [slider speed notes](../Design/surge.md).

`sourceSliderSpeeds` records the source velocity and converted velocity of **every retained source slider**, including unchanged ones, separately from added accompaniment. Compare these complete distributions when assessing speed variation. Looking only at `sliderBursts` can exclude an entire slower source group and badly understate the map's converted speed range.

Schema 7 also exports `sourceTimeline[].sliderPixelsPerSecond` for **every original source slider** and the map's `sourceSliderMultiplier`. This includes source sliders even if an arrangement replaces their original converted object. Speed is decoded path distance divided by one span's duration, including BPM and SV; each slider contributes one observation irrespective of repeat count or duration.

To reproduce the historical Surge study using archived reports (including the retired Relative emphasis proposal):

```sh
python3 osu.Game.Rulesets.Sticks.DifficultyTestbed/analyse_slider_speeds.py \
  mapreference/surge-speed-study/new-maps.json \
  mapreference/surge-speed-study/existing-maps.json \
  mapreference/surge-speed-study/railgun.json \
  --output mapreference/surge-speed-study/summary.json
```

This historical analysis writes JSON and a Markdown table with source minimum/median/maximum, speed-group counts, observed current-mod output, uncapped proposal requests, and a separate projection with the existing 240°/s continuous / 160°/s reversal ceilings. These projections and ceilings describe the study build, not current gameplay. Stars belong to that archived build. See the [tech-map study](../Design/surge.md#tech-map-speed-study--2026-09-15).

Add `--exclude-buzz` and use a different output name, such as `mapreference/surge-speed-study/without-buzz.json`, for the buzz/rapid-repeat exclusion table. This deliberately broad research filter removes source sliders with at least one reversal and a span duration at most one quarter of the local beat (plus 0.01ms for rounding). It also excludes short rapid reversals; ordinary slower reversals and fast single-pass sliders remain. Source and proposal medians are recalculated from the remaining sliders. Current-build observations are filtered without reconverting; their stars still belong to the full map. The JSON includes every excluded source object so the classification is reviewable.

The console reports head/flick/hold/slider/click/Slice counts, minimum click clearance, two-stick chords, milliseconds with both sticks sustaining, and interior flicks played opposite an active sustain. It prints three eight-second windows with the most differing objects. JSON includes source OD/CS, metadata, source hash/IDs, stars, all converted objects, and both versions of those windows. Click metrics include the fraction of heads, clicks per minute, minimum/median clearance and the number overlapping directional gestures. Clearance is the distance to any other note's complete occupied interval, on either hand: simultaneous heads and active sustains give zero. Maps with no measurable click clearance report `null`.

Source identity metrics, introduced in schema 3, measure source circle onsets retained as manual heads, counting each source timestamp once and matching top-level converted heads within 0.01 ms. Slider ticks, reversals and tails do not count as retained heads. Generated sustains are classified as holds or sliders starting at source circle onsets. Their `generatedSustainsWithinPrimaryWindow` count measures whether one fixed aim angle could cover the entire path within its primary window: the maximum minus minimum cumulative signed segment angle must fit inside the object's actual full `primaryHitAngle`. Holds have zero excursion; repeated reversals are included. This describes movement demand, not whether a pattern is good or bad. Authored bypasses have `null` source identity metrics. Source circle timestamps and each converted object's primary angle window are included in JSON for reproduction.

Schema 4 adds `patterns` diagnostics for comparing where different two-stick gestures occur. `headsAtOppositeSustainTail` counts directional heads matching an opposite-hand sustain end within 0.01 ms. `headsDuringOppositeSustain` counts directional heads strictly inside an opposite-hand sustain, with separate flick and sustain-entry counts. Matching start/end boundaries are excluded from interior counts. Each head counts once per family; families may overlap. The corresponding time arrays make these moments easy to locate, alongside the existing changed windows.

`patterns.chordAngles` counts opposite-hand directional head pairs at the same timestamp; clicks are excluded because their stored angle has no gameplay meaning. Its mutually exclusive bins are **coincident** (0–0.01°), **near** (>0.01–5°), **small** (>5–45°), **medium** (>45–90°), and **wide** (>90–180°). `directionalChords` also exports every pair's timestamp and separation. Higher totals or a flatter histogram are not targets: intentional repeated patterns and rare emphasis can both be useful.

The source identity metrics now also report retained source duration onsets and duration coverage. `sourceDurationUnionMs` measures time occupied by at least one source duration. `sourceDurationCoveredMs` intersects that interval union with the converted duration union, without counting simultaneous sticks twice. `convertedDurationOutsideSourceMs` measures sustains outside source duration intervals. `manualHeadsAwayFromSourceOnsets` counts heads more than 0.01 ms from all source head times; source repeats or tails may justify them. `sourceTimeline` exports decoded head/tail/repeat times, source slider tick times, timing-section start, local beat length and clap/finish accents, so added events can be checked against the source rhythm. Tick times use osu!'s shared `SliderEventGenerator`: repeat spans mirror tick positions, `GenerateTicks` is respected, and pre-v8 SV adjusts tick density. They describe source checkpoints rather than Sticks scoring ticks. The export shares Counterpoint's bounds of 16 spans and 32 ticks; excessive tick density or invalid tick intervals omit tick candidates without inventing a coarser grid. Repeat counts above 4096 retain their count and duration but omit the expanded repeat timestamp array.

`changedHeadFraction` is the fraction of the multiset union that does not match the Default output, comparing kind, timing, side, angle and slider arcs to 0.001 precision, plus exact timed segment weights. Added chord partners count as changes even when the existing head is preserved.

The process exits unsuccessfully for unreadable inputs, no evaluated maps, or validation failures. Validation checks chronological order, finite geometry, positive durations, carrier encode/decode (including segment timing), procedural same-side directional sustain overlaps and the 120 degrees/second generated-slider speed limit on every segment. Button clicks can overlap directional sustains, so these are measured by clearance rather than rejected as invalid. Authored overlaps and speeds are measured but do not fail those procedural constraints.

For historical builds, use the testbed from the same revision rather than replacing only its ruleset DLL. Reports record source hashes and the ruleset assembly hash. Schema 4's Default/Encore/Parity outputs correspond to schema 5's LegacyBase variants; schema 4's Counterpoint variants correspond to schema 5's current Default/Encore/Parity outputs. The analyzer handles these mappings when comparing saved reports.

### Iterate on the default arrangement converter

Run the entire local collection, including the two jump references and authored reference archives, through the current default and its historical base in one process:

```text
dotnet run --project osu.Game.Rulesets.Sticks.DifficultyTestbed -c Release -- --compare-converters mapreference --include-legacy-base --include-parity --output mapreference/counterpoint/iteration-01.json
```

Use `python3 osu.Game.Rulesets.Sticks.DifficultyTestbed/analyse_counterpoint.py mapreference/counterpoint/iteration-01.json` to summarize retained heads, hand changes, source checkpoint placement, chord spacing, motion relationships and per-map differences. The optional `--baseline` compares the historical base against an earlier export; `--previous-experiment` separately compares arrangement revisions, including Counterpoint before it became the default. Removing an earlier generated addition can be intentional. Both flags match equivalent modes across schema 4 and 5 rather than relying on identical labels.

```text
python3 osu.Game.Rulesets.Sticks.DifficultyTestbed/analyse_counterpoint.py mapreference/counterpoint/iteration-05.json --baseline mapreference/counterpoint/iteration-04.json --previous-experiment mapreference/counterpoint/iteration-04.json
```

The source geometry fields (`sourceCircleDiameter`, `sourceTimeline.positionX` and `positionY`) and exported rapid-jump thresholds let the analyzer independently reconstruct protected jump runs. It checks their original hands, timing, kind and signed paths, allowing a bounded rotation into an intentional exact chord stack. The lead-hand diagnostics identify runs of at least four retained flicks reassigned to one stick and describe the opposite stick's accompaniment. Added-head cadence ratios compare the nearest new onset spacing with surrounding baseline spacing; use them to locate abrupt subdivisions for review, rather than automatically rejecting valid syncopation.

`pulseHandSections` includes regular pulses shared between flicks and short sliders, with the other hand playing fills. The `arrangements` export records the planner families actually selected in current default modes, including timing extents, changed-hand counts and added-head counts. Historical base modes have no arrangement pass to observe. The analyzer keeps these observations separate from object-derived checks: selecting a family demonstrates coverage, while preserved source rhythm and the resulting hand relationships determine whether it helped.

Chord readability checks use final gameplay angle windows: a simultaneous opposite-stick pair with at least half the narrower window overlapping should share one angle. With equal full widths `W`, this means separation of at most `W / 2`, including the boundary. Authored maps bypass this procedural check. Final exact stacks may intentionally rotate existing heads slightly, and Parity history can change after reassignment, so review those separately from timing or path loss. During arrangement-only revisions, the explicit LegacyBase outputs should remain unchanged. Default and Encore are now the modes under development.

For simultaneous sliders that swap hands and receive new Parity rotations, retained signed paths take precedence over final colour when matching original objects. Encore can separately reselect clicks and their button sides after Counterpoint changes nearby occupancy; the protected-jump diagnostic lists those click changes separately and uses the plain LegacyBase/Default pair to check the underlying directional pattern.

Save the next revision as `iteration-02.json` and compare matching source SHA-256 values and mode names. LegacyBase/Default, LegacyBaseEncore/Encore, and the corresponding Parity pairs isolate the arrangement pass while keeping the other mods constant. The analyzer also accepts the original schema-4 Default/Counterpoint pairs. The `difference` and `examples` fields always compare each mode against the report's **Default**, which now includes Counterpoint; the analyzer uses the exported objects to compare each historical/current pair in the intended order. Check each map's source-onset retention and duration coverage before reviewing new tail handoffs, accompaniment, sustain entries and chord spacing. Review per-map changes and listen to the affected passages; pooled pattern counts can hide a repetitive long map or rewarding local change. The authored modes should remain identical and serve as reference patterns, rather than targets for procedural object counts.

## Parity angle audit

The original reset audit requires the original hard-correction converter build. Its capture includes the historical Standard, Parity, Duet and Parity + Duet strategies, each head's local beat length and slider endpoint. The audit explicitly selects each strategy through testbed-only mods and the gameplay pipeline. These labels are retained for compatibility with the analysis scripts; they do not describe the current mod selection menu.

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
