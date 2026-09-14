# Coordination difficulty

The September 2026 calculation combines an ordinary-skill baseline with a bounded
addition for work performed by both hands together. It uses the same mechanical,
reading and control demands as the ordinary difficulty model: note density,
angular spacing, aim precision, slider speed and reversals all matter.

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
Directionless clicks use neutral angular precision.

Explicit mapped breaks are merged and excluded from both work and playable time.
Event work removed by a break is not redistributed onto the remaining time.
Playable time runs from the first head to the last head or sustain end.

## Approved normalization

```text
demandRate = coordinatedWork / playableSeconds
p = demandRate / (demandRate + 7.449578513114503)
response = 6*p*p / (6*p*p + 1 - p)
addition = 1.9804203856707803 * response
stars = min(30, baseline + addition)
```

Zero playable time or zero work produces no addition. The response starts gently,
reaches 75% at `p = 0.5`, and approaches its fixed maximum smoothly. `p` is a demand
index, not a percentage of map time. The constants were frozen from the approved
Want You Gone comparison to preserve its preceding proposed rating. They are not
recomputed per map or corpus. Harder unrelated solo passages cannot increase or
dilute the coordination addition through the map's ordinary-skill rating.

The baseline combines mechanical, reading and control ratings with the existing
3.3-norm, multiplied by `0.89` before the existing timing and star calibration and
angular adjustment. The former coordination strain is excluded from that norm.

For performance calculation, the additive stars are expressed as an equivalent
fourth skill in the existing norm. This preserves the ordinary components' share
and gives coordination the additional performance. The existing coordination
event strain count remains solely for the performance miss penalty; its strain
magnitudes no longer set star difficulty or the coordination skill rating.

## Verification

The production conversion/difficulty pipeline was compared with all 52 saved
normalization-proposal results, with a maximum star difference below `1e-9`.

| Reference difficulty | Previous stars | Applied stars | Coordination addition |
| --- | ---: | ---: | ---: |
| Want You Gone — Collab Potato | 3.90 | 3.95 | +0.650 |
| Masterpiece BPM180 — AR10 | 7.98 | 6.79 | +0.000 |
| Story of my Wife — Warota | 6.34 | 5.43 | +0.037 |
| Exit This Earth's Atomosphere — GiRLC's Intangible | 7.29 | 6.32 | +0.125 |
| Blue Zenith — Easy | 3.56 | 4.07 | +1.052 |
| Blue Zenith — Akaphyxia's Normal | 3.97 | 4.23 | +0.868 |
| Blue Zenith — Hard | 5.08 | 4.93 | +0.621 |

Local comparison artifacts remain under `mapreference/coordination-normalization-proposal/`.
Regression checks cover break exclusions, density and movement sensitivity,
directionless clicks, bounded normalization, and exact timed-prefix results after
simultaneous groups and overlapping sliders. Coordination intervals are updated
incrementally so querying each prefix does not replay the whole map.

Validation passed 908 regression tests on the stable package and 121 targeted
checks against the cached Tachyon checkout. A local 4,000-note timed benchmark
went from 2.58 s to 2.74 s (about 6%); the existing timed path still allocates
heavily. The new coordination interval updates scale with local changes, as
checked separately from the ordinary-skill and framework work. Full-map
calculation for that synthetic benchmark took 49 ms.
