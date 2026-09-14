# Coordination and ordinary difficulty

The September 2026 calculation combines mechanical, reading, control and
coordination skills before the shared star calibration. Dense solo patterns do
not need a coordination addition to receive difficulty.

## Rapid target changes

Mechanical difficulty retains the existing per-hand neutral-reset speed demand.
It also measures how quickly the target changes for each stick across the head
sequence. The previous direction is the preceding gesture's **endpoint**, including
slider movement. Clicks contribute their existing button demand without inventing
an angle.

```text
gap = max(50 ms, previous head-group gap, same-hand recovery gap / 2)
displacement = sin(abs(same-hand angular change) / 2)
reorientation = 11 * (125 ms / gap)^3 * displacement^2
```

Gaps use real playback time and the existing OD-aware speed adjustment. The rate
term couples squared displacement at speed with event frequency. There is no
pattern-predictability factor in this term. Using the same hand's recovery gap
prevents a tiny offset from an otherwise slow opposite-hand pattern from creating
artificially fast movement demand.

The strongest reorientation demand in a head group is accumulated with the existing
mechanical decay. Mechanical strain takes the stronger of this and the per-hand
reset strain, rather than summing two assessments of the same head sequence.
Simultaneous heads form one group. The existing ranked strain aggregation keeps
bursts and sustained density relevant, with diminishing growth from length.

## Measuring shared work

Head and reversal work occupies a local rhythmic window. Its half-width is half
the shorter gap to the neighbouring gameplay event, using heads, actual reversals
and sustain ends as boundaries. Slider ticks and non-reversing path subdivisions
do not introduce artificial windows. Tracking work follows each segment's duration
and angular movement; reversal effort is allocated around the actual turns.

Coordination is enabled around simultaneous heads, heads played during an
opposite-hand sustain, and intervals with both hands sustaining. If the work rates
are `L` and `R`, the shared rate is `4 * L * R / (L + R)`. This cannot exceed their
combined work, and a slow held task cannot inherit the other hand's full workload.
Head work includes the stronger of reset and reorientation demand. Reading,
angular precision, slider speed and reversals remain part of shared work.
Directionless clicks use neutral angular precision.

Explicit mapped breaks are merged and excluded from both work and playable time.
Event work removed by a break is not redistributed onto the remaining time.
Playable time runs from the first head to the last head or sustain end.

## Combining the skills

```text
demandRate = coordinatedWork / playableSeconds
p = demandRate / (demandRate + 7.449578513114503)
response = 6*p*p / (6*p*p + 1 - p)
coordinationSkill = 8 * response^(1 / 3.3)
combined = (mechanical^3.3 + reading^3.3 + control^3.3
            + coordinationSkill^3.3)^(1 / 3.3)
```

Zero playable time or zero work produces no coordination skill. The retained
response starts gently, reaches 75% at `p = 0.5`, and approaches its fixed maximum
smoothly. `p` is a demand index, not a percentage of map time. The constants are
fixed across maps; no title, beatmap ID or current corpus statistic affects them.

The response controls coordination's contribution to the norm's sum. Its raw skill
is bounded at 8; that is **not eight stars**. The combined result receives the
existing `0.89` scale, timing multiplier, star calibration and angular adjustment,
with the overall 30-star ceiling. The old fixed addition of up to 1.98 stars has
been removed. The diagnostic `CoordinationStarAddition` now reports the difference
between calibrating all four skills and calibrating the three ordinary skills.
It is not an extra operation applied after calculating the rating.

Performance uses these same four skill ratings directly. The historical
coordination event strain count remains solely for the performance miss penalty;
its strain magnitudes do not set star difficulty or coordination magnitude.

## September 14 comparison

All 53 maps went through the actual default gameplay conversion pipeline. The
static difficulty result and the full calculator pipeline agreed within `1e-9`.

| Reference difficulty | Previous stars | Revised stars | Coordination share in stars |
| --- | ---: | ---: | ---: |
| Spider Dance — Spider IV. | 4.96 | 6.34 | +0.018 |
| Masterpiece BPM180 — AR10 | 6.79 | 7.66 | +0.000 |
| Want You Gone — Collab Potato | 3.95 | 4.12 | +0.741 |
| Story of my Wife — Warota | 5.43 | 5.77 | +0.024 |
| Exit This Earth's Atomosphere — GiRLC's Intangible | 6.32 | 7.61 | +0.059 |
| Blue Zenith — Easy | 4.07 | 3.82 | +0.811 |
| Blue Zenith — Akaphyxia's Normal | 4.23 | 4.08 | +0.720 |
| Blue Zenith — Hard | 4.93 | 4.98 | +0.487 |
| Blue Zenith — FOUR DIMENSIONS | 6.77 | 10.29 | +0.023 |
| FREEDOM DiVE — FOUR DIMENSIONS | 6.45 | 10.10 | +0.029 |

Spider Dance remains below the suggested approximately 7-star target. Faster stream
references rise substantially; these are calculated outcomes requiring playtesting,
not independent evidence that every revised rating is correct. No per-map target
or exception was added to obtain the table.

Local analysis and full comparison results are gitignored under
`mapreference/density-coordination-revision/`. Regression coverage includes dense
alternating target changes, shared calibration, break exclusions, rate and movement
sensitivity, directionless clicks, bounded normalization and exact timed prefixes.
The new accumulator is reversible with its simultaneous group; querying a prefix
does not replay the map.
