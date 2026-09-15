# Surge slider-speed experiment

Enable **Surge (SG)** in Conversion, then choose **Speed interpretation** in its settings. It is unranked and does not change the default converter when disabled.

| Option | What it responds to |
| --- | --- |
| **Source speed** (default) | Source path travel per second × `45 / (100 × map.SliderMultiplier)`. Follows source BPM and SV directly, including slower source sliders. |
| **Relative emphasis** | `max(80, 120 × (2 − medianSourceSpeed / sourceSpeed))`. Keeps 120°/s as the normal reference, allows slower sliders down to 80°/s, and preserves the existing diminishing boost for faster sliders. |

Both preserve source slider timing and direction. The actual source-derived primary slider is rescaled to the requested maximum segment speed, preserving segment timing and relative motion. Equal source speeds receive equal requests across unaccented runs and long sustains. The previous accent/entry requirement and four-beat duration limit have been removed.

Continuous movement stays at or below **240°/s**; paths retaining reversals stay at or below **160°/s**. These are experimental ceilings, not comfort guarantees. Added accompaniment and sliders assembled from circles retain their existing paths. Authored Sticks maps bypass Surge. Parity, Encore, Solo and Difficulty Adjust can be combined with it.

Relative emphasis counts each source slider once when finding the median, irrespective of duration or repeat count. Median-speed source sliders request 120°/s, twice-median sliders 180°/s, and four-times-median sliders 210°/s. Requests diminish towards 240°/s as the ratio increases. At 80% of the median the request is 90°/s, and at 75% or less it reaches the 80°/s floor. Scaling all source speeds equally leaves these requests unchanged. The buzz exclusion later in this document is an analysis filter only; gameplay's median uses all source sliders.

Reproduce the gameplay-pipeline comparison:

```sh
dotnet run --project osu.Game.Rulesets.Sticks.DifficultyTestbed -c Release -- --compare-converters mapreference/standard --include-surge --include-parity --output mapreference/surge/comparison.json
```

The report records changed slider timestamps and both speeds for playtesting. Numerical checks establish timing and speed bounds; they do not establish whether the faster passages feel good.

The initial 44-map comparison of the previous implementation passed with zero validation issues, including both settings with Parity. Its measurements and the proposal study below are historical; current implementation checks are recorded separately.

## Fiery's Extreme: previous implementation's complete source-speed comparison

The earlier shortlist counted only changed sliders. That omitted the slow group and incorrectly made Relative emphasis look like it only varied between 180 and 190°/s. The full gameplay-pipeline trace includes all 45 source sliders:

| Source speed (px/s) | Sliders | Default (°/s) | Source speed (°/s) | Relative emphasis (°/s) |
| ---: | ---: | ---: | ---: | ---: |
| 255 | 26 | 120 | 120 | 120 |
| 510 | 11 | 120 | 127.1 | 180 |
| 612 | 8 | 120 | 145.9 | 190 |

Relative emphasis already gave a 70°/s slow-to-fast difference. The current implementation retains that fast-side curve, with its normal reference fixed at 120°/s. `sourceSliderSpeeds` records every retained source slider, including those left unchanged. Use those complete traces to judge variation; `sliderBursts` alone only reports modifications, now including decreases as well as increases. No speed-ranking claim should be inferred from the number of changed sliders.

## Tech-map speed study — 2026-09-15

Measured **78 maps / 22,689 source sliders**, including 30 new maps selected from tech-map references and their difficulty spreads. Candidate sources included the [osu! technical-map wiki](https://osu.ppy.sh/wiki/en/Beatmap/Technical_maps), [PLANET//SHAPER's Loved nomination](https://osu.ppy.sh/community/forums/topics/880237), and a [slider-velocity practice thread](https://osu.ppy.sh/community/forums/topics/1258775). Selection was purposeful, not a representative sample of all maps. All 312 current-mode conversions (Default, Encore and both Surge settings) completed with zero validation issues. Every source slider in this sample retained a traced primary slider.

The pre-implementation study projected these two replacements:

- **Direct Source:** `source_px_per_s * 45 / (100 * map.SliderMultiplier)`.
- **Relative, 80 floor:** with `r = source_px_per_s / median_source_px_per_s`, use `max(80, 120 + 120 * (1 - 1 / r))`. This keeps the 120°/s base and existing faster-slider emphasis. The proposed slower side extends the same curve below the median, stopping at 80°/s.

These requests remove the existing boost-only behavior and accent/entry/duration gates. The full tables separately apply the existing ceilings using each converted path's actual reversal status. **80°/s is the relative floor; the base remains 120°/s.** The earlier table incorrectly changed the base to 80 and has been replaced. Stars in the report belong to the current build, not these proposals.

All numbers below are **minimum / median / maximum**; proposals are degrees/s before per-path ceilings:

| Map | Source px/s | Direct Source °/s | Relative, 80 floor °/s |
| --- | --- | --- | --- |
| [Harumachi Clover — Fiery's Extreme](https://osu.ppy.sh/beatmapsets/859783#osu/1893461) | 255 / 255 / 612 | 63.75 / 63.75 / 153 | 120 / 120 / 190 |
| [MARENOL — Insane](https://osu.ppy.sh/beatmapsets/1136149#osu/2439930) | 46.7 / 466.7 / 1166.7 | 10.5 / 105 / 262.5 | 80 / 120 / 192 |
| [MARENOL — Extra](https://osu.ppy.sh/beatmapsets/1136149#osu/2404722) | 84 / 840 / 3360 | 10.5 / 105 / 420 | 80 / 120 / 210 |
| [Nhelv — Rhonen's Hyper](https://osu.ppy.sh/beatmapsets/917915#osu/2039004) | 124.2 / 611 / 1222.1 | 39.9 / 196.4 / 392.8 | 80 / 120 / 180 |
| [flying in the flow of deep-sea — water](https://osu.ppy.sh/beatmapsets/347556#osu/766891) | 119.6 / 478.5 / 957 | 32.6 / 130.5 / 261 | 80 / 120 / 180 |
| [Railgun Roulette — Shot It](https://osu.ppy.sh/beatmapsets/567069#osu/1201131) | 192 / 768 / 1536 | 48 / 192 / 384 | 80 / 120 / 180 |
| [Exit This Earth's Atomosphere — Evolution](https://osu.ppy.sh/beatmapsets/517474#osu/1114721) | 102 / 1020 / 4233 | 12.75 / 127.5 / 529.125 | 80 / 120 / 211.1 |
| [PLANET//SHAPER — Acceleration](https://osu.ppy.sh/beatmapsets/611729#osu/1291225) | 88.2 / 882 / 8820 | 11.025 / 110.25 / 1102.5 | 80 / 120 / 228 |
| [Exit remix — Primordial Nucleosynthesis](https://osu.ppy.sh/beatmapsets/855677#osu/1787848) | 70 / 1225 / 7000 | 15 / 262.5 / 1500 | 80 / 120 / 219 |

**Useful contrasts:** MARENOL [Extra] has 49/227 sliders at least twice its median speed; Railgun Roulette [Shot It] has 29/186. Their 90th-percentile speeds are 2.41× and 2× median respectively, so their fast behavior is broader than an isolated maximum. MARENOL [Insane] has 14/169 at least twice median. PLANET//SHAPER has 53/761 at least twice median, but only two reach its extreme 8820 px/s maximum. These are short repeated sliders at 2:35.481 and 2:35.889. The exact difficulty matters: Railgun's [Hard+] has 173/187 sliders at its median and only a 1.3× maximum/median ratio.

**Ceiling effects:** Direct Source exceeds the current per-path ceiling on 616/1165 Exit-remix sliders (52.9%); its median would flatten to 240°/s. For MARENOL [Extra] and Railgun [Shot It], the counts are 29/227 and 33/186. The corrected relative proposal exceeds no per-path ceilings on these three maps. Its 80°/s floor also prevents the extremely slow targets from the mistaken 80-base proposal. These projections retain the existing faster-slider emphasis curve, rather than using a linear speed ratio for the faster side.

For playtesting within the earlier preferred difficulty range, the **current build** gives MARENOL [Insane] 5.37★ default / 5.75★ Source / 5.86★ Relative; Nhelv [Rhonen's Hyper] 4.73★ / 5.58★ / 4.92★. MARENOL [Extra] (6.18★ / 7.10★ / 7.26★) and Railgun [Shot It] (6.45★ / 7.04★ / 6.74★) are harder but stronger variation cases. These ratings are not forecasts for either replacement.

Local gitignored artifacts: [all-map tables](../mapreference/surge-speed-study/summary.md), [summary and speed groups](../mapreference/surge-speed-study/summary.json), and [new-map download URLs and SHA-256 hashes](../mapreference/surge-speed-study/download-manifest.json). Full decoded objects and current-mod observations are in `new-maps.json`, `railgun.json`, and `existing-maps.json` in that directory. Reproduction uses the [testbed analysis command](../osu.Game.Rulesets.Sticks.DifficultyTestbed/README.md).

### Buzz and rapid-repeat exclusion

For this comparison, exclude source sliders with **at least one reversal and at most one quarter of a local beat per span** (+0.01ms rounding tolerance). This is deliberately broader than just long buzzes: short rapid reversals are excluded too. Fast single-pass sliders and slower ordinary reversals remain. This is an analysis filter, not a change to the converter or a universal definition of a buzz slider.

The filter removes 2,215 of 22,689 source sliders across the 78-map sample, leaving 20,474. Source and proposal medians are recalculated after exclusion. The source medians for the nine maps below happen to stay unchanged. Triplets are **minimum / median / maximum**, before per-path ceilings; the relative proposal keeps its **120°/s base and 80°/s floor**.

| Map / difficulty | Excluded | Source px/s | Direct Source °/s | Relative °/s |
| --- | ---: | --- | --- | --- |
| Harumachi — Fiery's Extreme | 1 / 45 | 255 / 255 / 612 | 63.8 / 63.8 / 153 | 120 / 120 / 190 |
| MARENOL — Insane | 27 / 169 | 46.7 / 466.7 / 933.3 | 10.5 / 105 / 210 | 80 / 120 / 180 |
| MARENOL — Extra | 44 / 227 | 84 / 840 / 3360 | 10.5 / 105 / 420 | 80 / 120 / 210 |
| Nhelv — Rhonen's Hyper | 73 / 222 | 124.2 / 611 / 1018.4 | 39.9 / 196.4 / 327.3 | 80 / 120 / 168 |
| flying in the flow of deep-sea — water | 4 / 423 | 119.6 / 478.5 / 957 | 32.6 / 130.5 / 261 | 80 / 120 / 180 |
| Railgun Roulette — Shot It | 42 / 186 | 192 / 768 / 1536 | 48 / 192 / 384 | 80 / 120 / 180 |
| Exit This Earth's Atomosphere — Evolution | 25 / 670 | 102 / 1020 / 2845.8 | 12.8 / 127.5 / 355.7 | 80 / 120 / 197 |
| PLANET//SHAPER — Acceleration | 154 / 761 | 88.2 / 882 / 3528 | 11 / 110.3 / 441 | 80 / 120 / 210 |
| Exit remix — Primordial Nucleosynthesis | 81 / 1165 | 70 / 1225 / 3731 | 15 / 262.5 / 799.5 | 80 / 120 / 200.6 |

MARENOL [Extra] retains its maximum on two single-pass sliders, but its count at least twice median falls from 49/227 to 10/183 (5.5%). Railgun [Shot It] retains 20/144 (13.9%), PLANET//SHAPER 47/607 (7.7%), and Exit [Evolution] 39/645 (6.0%). MARENOL [Insane] falls to 4/142 (2.8%); Nhelv [Rhonen's Hyper] has none at twice median after this exclusion. These are more useful counts for selecting ordinary fast-slider passages than the unfiltered counts above.

Artifacts: [filtered all-map tables](../mapreference/surge-speed-study/without-buzz.md) and [filtered statistics plus every excluded object](../mapreference/surge-speed-study/without-buzz.json). Reproduce with `analyse_slider_speeds.py --exclude-buzz` using the same input reports. Gameplay was not changed.
