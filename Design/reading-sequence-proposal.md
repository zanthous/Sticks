# Reading sequence proposal

2026-09-15 · Implemented in difficulty version `202609150`, compared with `202609142`.

The ordinary reading component now measures work from each stick's
target sequence, accumulated from nearby notes. The implemented model gives
**Spider Dance 7.03★ and Masterpiece 7.00★**, with Spider higher in reading.
The production conversion and difficulty pipeline matches every component of the
original proposal exactly on all 53 corpus maps.

## Implemented logic

1. **Measure each hand's next target from its own previous endpoint.** A large
   distance between alternating blue/red targets contributes a small bounded
   visual-separation term. It no longer multiplies the whole reading demand.
2. **Increase reading work when the size of a hand's angular step changes.**
   Acquiring a large jump following a small step costs more than continuing with
   the same step size. This uses the actual angles of three successive heads on
   that hand. Its influence fades with elapsed time.
3. **Add the work of nearby notes with time decay.** A fast triple raises the
   strain that is still present at the following jump. The calculation does not
   replace recent strain with an average that can conceal a short burst.
4. **Keep the existing ranked aggregation and final star combination.** Length
   and sustained activity still contribute. Mechanical demand continues to price
   fast, large jumps even when their spacing is uniform.

The change term measures angular step-size variation. It does not attempt to
infer randomness or assign difficulty from named pattern types. It is not a
complete model of every kind of reading complexity.

## Concrete calculation

For each directional head, let `a` be the shortest signed angular step from that
stick's preceding endpoint to this head, in degrees. Let `previousA` be that
stick's preceding step and `handGap` its head-to-head interval in milliseconds.

```text
d = (abs(a) / 180)^0.7
v = sin(abs(abs(a) - abs(previousA)) * pi / 360)
    * 0.3^(handGap / 1000)

headWork = 0.3 + d * (1 + 3*v)
```

`d` and `v` are bounded between zero and one. A first head has `d = v = 0`;
the next head can have distance demand but has no preceding step for variation.
Clicks contribute their baseline head work without an invented angle.
Slider endpoints retain their existing role as the starting position for a
subsequent target acquisition; slider control scoring is unchanged.

For a simultaneous group:

```text
work = 7.5 * (sum(headWork)
              + 0.25 * previousGroupDistance
              + existingTypeContext + existingChordContext)

strain = previousStrain * 0.3^(elapsedMilliseconds / 1000)
         + work * (1 - 0.3^0.125)

reading = 0.85 * sqrt(existingRankedSum(strainSamples))
```

`previousGroupDistance` is the existing normalized angular distance from the
preceding chronological group, bounded between zero and one. The existing 0.85
modifier when following an opposite-hand slider arc is also retained.

The decay has a **576 ms half-life**, reusing the current mechanical decay.
It replaces reading's approximately 3.1-second averaging. The normalization
preserves a steady work level at the existing 125 ms reference interval. More
frequent events naturally build more strain, without an additional rate exponent.
Explicit breaks clear the reading history and strain. Playback rate affects the
elapsed times in both the sequence memory and strain accumulation.

The coefficients `7.5` and `3` are proposed balance values, not experimentally
established perceptual constants. The same values apply to every map. Spider and
Masterpiece informed calibration; the other maps below check the broader impact.

## Results through actual default conversion

All 53 corpus maps were converted through the default gameplay pipeline. These
are complete calculated star ratings, not estimates from a multiplier applied
to the old stars.

| Map | Before | Applied | Change |
| --- | ---: | ---: | ---: |
| Spider Dance — Spider IV. | 6.34★ | **7.03★** | +0.69★ |
| Masterpiece BPM180 — AR10 | 7.66★ | **7.00★** | −0.66★ |
| Want You Gone — Collab Potato | 4.12★ | 4.67★ | +0.55★ |
| Story of my Wife — Warota | 5.77★ | 5.50★ | −0.27★ |
| Exit This Earth's Atomosphere — GiRLC's Intangible | 7.61★ | 8.23★ | +0.62★ |
| Blue Zenith — Easy | 3.82★ | 4.19★ | +0.37★ |
| Blue Zenith — Hard | 4.98★ | 5.46★ | +0.48★ |
| Blue Zenith — FOUR DIMENSIONS | 10.29★ | 11.05★ | +0.76★ |
| FREEDOM DiVE — FOUR DIMENSIONS | 10.10★ | 10.94★ | +0.85★ |

Spider's reading component changes from **7.786 to 9.316**; Masterpiece's changes
from **10.213 to 9.031**. These are internal component units, not standalone star
ratings. Masterpiece retains the contribution from its longer, more sustained
play; Spider's faster and more variable target sequences now overcome that
advantage by a small amount.

The median change across the 53 maps is **+0.48★**. The largest increase is
**+0.94★**, Blue Zenith — ktgster's Extreme, from 9.04★ to 9.97★. Masterpiece has
the largest decrease. This is a broader reading rebalance, and the rises on Exit
and the faster stream references need playtesting as well as the two focus maps.

## Checking the mechanism

The earlier analysis identified Spider's **20.937–21.328-second** passage: two
65 ms intervals lead into a large jump. The candidate keeps demand from those
closely spaced notes while charging the next target acquisition and change in
step size.

Two diagnostic removals show which changes matter. All other candidate settings
and components stay fixed:

| Calculation | Spider | Masterpiece |
| --- | ---: | ---: |
| Full candidate | 7.03★ | 7.00★ |
| Remove the step-size-change term | 6.12★ | 6.63★ |
| Use the old decay time with the new additive accumulation | 6.56★ | 6.75★ |

The variation term and shorter accumulation therefore both help Spider more.
These effects overlap and should not be added together as independent bonuses.

The original prototype weakened variation according to the ratio between the
previous and current intervals. A synthetic triple-to-jump check exposed that
this could wrongly lower demand when a triple returned to normal spacing. The
final candidate uses elapsed-time decay instead. It passes that check with the
same angles and same final 130 ms gap: the preceding 65 ms triple produces higher
strain at the following jump than evenly spaced preceding notes.

## Scope and verification

Mechanical, control and coordination components, coordinated workload, playable
time, timing precision and angular precision remain **exactly unchanged on all
53 maps**. The current final norm, calibration and ranked length contribution
are retained.

Coordination continues to consume its existing calibrated local workload. Feeding
the new reading-work units directly into that calculation produced a large
unintended increase on Want You Gone, so that experiment is not part of this
proposal. Updating those workload units would require separate calibration.

The isolated C# implementation includes simultaneous-group rollback and passes
checks for rotations, reflections, hand swapping, time shifts, playback speed,
directionless clicks, explicit breaks, length growth, triple carry and all 160
prefixes of a sequence containing 80 two-hand chords. A separate reconstruction
from the exported objects matches every proposed reading-strain sample within
`1.8e-6`, consistent with serialized floating-point angles. Baseline components
match production within `1e-9`.

This version is ready for playtesting. The reported map ratings are
calculated outcomes; the corpus does not provide independent human difficulty
labels that would establish the new ratings as correct.

## Review artifacts

- [Production reading component](../osu.Game.Rulesets.Sticks/Difficulty/SticksReadingDifficulty.cs)
- [Production integration](../osu.Game.Rulesets.Sticks/SticksDifficultyModel.cs)
- [Regression checks](../osu.Game.Rulesets.Sticks.Tests/SticksReadingDifficultyTest.cs)
- [Production comparison against the proposal](../mapreference/reading-sequence-proposal/implementation-verification.json)
- [Core C# proposal](../mapreference/reading-sequence-proposal/reproduce/ReadingSequenceWork.cs)
- [Integration into the diagnostic calculator](../mapreference/reading-sequence-proposal/reproduce/CandidateModel.cs)
- [Reproduction commands](../mapreference/reading-sequence-proposal/reproduce/README.md)
- [All 53 before/after results and diagnostic removals](../mapreference/reading-sequence-proposal/results.json)
- [Behaviour checks](../mapreference/reading-sequence-proposal/checks.json)
- [Independent reconstruction checks](../mapreference/reading-sequence-proposal/verification.json)
- [Earlier comparison](spider-masterpiece-analysis.md)

The prototype and raw data are local, gitignored analysis artifacts. The original
single-model proposal was applied at its tested strength, without blending it with
the previous reading skill or reducing the step-change coefficient.
