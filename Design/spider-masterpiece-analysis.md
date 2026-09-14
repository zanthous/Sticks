# Spider Dance and Masterpiece: converted difficulty comparison

Analysis date: 2026-09-15. Difficulty version: `202609142`.

The largest discrepancy is in the reading calculation. Spider Dance has faster
main passages and substantially more variation in each hand's flick sequence.
The model gives it a small mechanical advantage, but Masterpiece receives a much
larger reading rating, heavily influenced by the distance between alternating
left/right targets. This comparison focuses on flick patterns; slider changes
are not the proposed explanation or remedy.

## Maps and method

- [Spider Dance — Spider IV.](https://osu.ppy.sh/beatmapsets/396259#sticks/862154), beatmap `862154`.
- [Square Jump Practice Maps — Masterpiece BPM180, AR10](https://osu.ppy.sh/beatmapsets/112651#sticks/292577), beatmap `292577`.

Both were converted through the actual default gameplay pipeline without mods.
Both have CS 4. Spider has OD 6; Masterpiece has OD 7. Their source AR values do
not describe a different player-selected Sticks approach rate.

An instrumented copy of the current calculator reproduced both final ratings
and all four skill components within `1e-9`. It recorded each converted head,
each hand's preceding endpoint, local difficulty inputs, and accumulated strain.
Counterfactual calculations reused those converted objects rather than changing
their conversion. Production conversion and difficulty code were not edited.

## What each map asks the player to do

| Measurement | Spider Dance | Masterpiece |
| --- | ---: | ---: |
| Current rating | 6.336★ | 7.657★ |
| Main spacing between successive heads | 130–131 ms | 166–167 ms |
| Median interval between one hand's consecutive flicks | 261 ms | 333 ms |
| Mean angular step between one hand's consecutive flicks | 87.6° | 99.8° |
| Those flick steps measuring at least 120° | 28.1% | 45.6% |
| Median change in step size across three consecutive flicks on one hand | 53.1° | 8.0° |
| Consecutive chronological heads assigned to the same hand | 36 of 510 | 0 of 1,230 |
| Simultaneous two-hand head groups | 8 | 0 |
| Playable span, excluding explicit breaks | 98.060 s | 197.984 s |
| Total converted heads | 511 | 1,231 |
| Heads per second over that playable span | 5.21 | 6.22 |

The per-hand flick measurements use consecutive objects on that hand only when
both are flicks and no more than one second apart: 360 transitions in Spider,
1,115 in Masterpiece. The step-size-change measurement requires three flicks
under the same condition: 328 triples in Spider, 1,103 in Masterpiece. It measures
how much the size of the next angular step changes, not simply whether the
movement reverses sign. That avoids ambiguity around exact 180° steps.

Spider's main pulse is about **28% faster**. Its individual hands also receive
much less uniform sequences of target angles, and hand assignment occasionally
departs from strict alternation. Those are concrete differences supporting the
reported playing experience. The angular variation statistic describes geometry;
it has not been calibrated into an extra difficulty penalty.

Masterpiece has substantial demands of its own: more large per-hand angular
steps, roughly twice the playable length, and much more continuous activity.
It also has a roughly nine-second 83–84 ms passage around 2:49–2:59. Spider is
faster in its main patterns, but has enough slower passages and pauses that
Masterpiece has the higher whole-map average note density.

Spider's timing metadata is 115 BPM with main notes a quarter beat apart.
Masterpiece is 180 BPM with main notes half a beat apart. Comparing the actual
130 ms and 167 ms intervals is more useful here than comparing the stored BPM.

## Why the calculation ranks Masterpiece higher

The final difference is **1.321★**. Decomposing that difference by swapping the
component values between the maps, averaged over every swap order, gives:

| Contribution to Masterpiece's lead | Difference |
| --- | ---: |
| Reading | +1.211★ |
| Timing precision, OD 7 versus OD 6 | +0.202★ |
| Mechanical difficulty | −0.082★ |
| Control | +0.007★ |
| Coordination | −0.016★ |

These values explain the current formula's output; they are not independent
estimates of how difficult the maps should feel. In particular, the result does
not mainly come from a control difference. Mechanical difficulty already favours
Spider, but only slightly after accumulation and aggregation.

### Reading follows the chronological sequence across both hands

The reading calculation measures each head against the previous chronological
head group. With alternating notes, that usually means comparing a right-hand
target with the previous left-hand target. The angular distance contributes both
to a local reading term and to a multiplier that can reach 6.8.

Masterpiece's **ten highest reading-strain samples at eligible flick heads** all
occur between **3:30.444 and 3:31.943**. In this passage:

- Each hand's own target advances about **30.0°** on average.
- The distance from the preceding opposite-hand target averages **164.9°**.
- The model consequently treats this as an especially demanding reading passage.

For Spider's ten highest eligible flick samples, each hand's own step averages
**113.7°**, while the chronological cross-hand distance averages **91.0°**. These
samples are around **0:37.502** and **0:46.111–0:48.068**, mostly at the faster
130–131 ms pulse. Eligibility requires the current head and that hand's previous
object to be flicks; the strain itself is the normal full-map calculation.

Visual separation across the board can matter. However, this heuristic does not
distinguish large separation between two independently progressing hands from a
large change in what one hand must target next. The peak comparison shows why
the distinction matters here. It does not establish that all cross-hand visual
distance should be ignored.

### Accumulation favours sustained high reading estimates

Reading strain uses an exponentially weighted average with a half-life of about
**3.1 seconds**. Difficult individual inputs are averaged with the surrounding
pattern before the resulting samples are ranked and combined.

Across all head groups, Spider's unsmoothed reading input has a slightly higher
90th percentile and peak than Masterpiece's. After averaging, Masterpiece's peak
reading strain is **16.27**, compared with Spider's **9.96**. Sustaining the high
cross-hand distance estimate therefore has a large effect on the result.

The ranked sum also grows with the number of positive samples. Masterpiece's
length and sustained activity partly erase Spider's mechanical advantage:
Spider has higher peak mechanical strain and higher strain after normalizing the
rank weights, yet the final mechanical components are only **8.74 versus 8.56**.
These are internal component units, not standalone star ratings.

This is evidence to review how short demanding passages and sustained passages
are balanced. Concentration in a few sections is not inherently a bug: both maps
have much of their rating concentrated around particular passages.

## Checks against overly simple explanations

These are diagnostic experiments, not proposed replacement ratings.

| Isolated experiment | Spider | Masterpiece |
| --- | ---: | ---: |
| Current calculation | 6.336★ | 7.657★ |
| Both at OD 6 | 6.336★ | 7.439★ |
| Remove only the spatial-search multiplier from reading | 5.412★ | 5.527★ |
| Use per-hand distance only in that reading term, retaining its other factors | 6.860★ | 8.073★ |
| Repeat each map twice, separated by an explicit two-bar break | 6.945★ | 8.394★ |

Removing the search multiplier almost eliminates the gap, demonstrating its
importance. But replacing only its distance with a per-hand distance does **not**
solve the comparison: Masterpiece has larger per-hand steps in many other
sections, and the remaining multipliers and aggregation still favour it. A
distance substitution on its own would also push Masterpiece above the user's
stated approximate upper expectation.

Tempo is already influential. Slowing Spider's entire converted timeline until
its main pulse matches Masterpiece's, at OD 6, lowers it from **6.336★ to 4.674★**.
The failure is therefore not a complete absence of speed weighting. Simply
raising a global speed multiplier would not address the reading discrepancy.

Removing the existing return/similar-step discounts also fails to solve it:
Spider becomes **6.435★**, Masterpiece **7.830★**. The analysis does not support
making this a new predictability adjustment.

## Recommended next investigation

Prioritize the **reading model for two independent flick sequences**, including
how it accumulates at the actual note pace. Keep global visual separation as a
distinct consideration and examine how strongly it should amplify the demands
already represented by each hand's targets and timing.

Use the passages above to check whether a candidate moves in the right direction,
then validate it across other maps. Also examine whether the current averaging
and length contribution suppress Spider's faster, less uniform demands too much.
Changing the distance term alone has already failed this diagnostic comparison.

The measurements support taking the perceived difficulty ordering seriously.
They do not yet establish a replacement formula or an exact target star rating.

## Artifacts

Local, gitignored diagnostic data:

- [Converted Spider objects and exact calculator trace](../mapreference/spider-masterpiece-analysis/spider.json)
- [Converted Masterpiece objects and exact calculator trace](../mapreference/spider-masterpiece-analysis/masterpiece.json)
- [Flick-only comparison and peak samples](../mapreference/spider-masterpiece-analysis/flick-comparison.json)
- [Whole-map metrics and 16-beat diagnostic sections](../mapreference/spider-masterpiece-analysis/analysis.json)
- [Rating-gap attribution](../mapreference/spider-masterpiece-analysis/gap-attribution.json)
- [Additional reading experiments](../mapreference/spider-masterpiece-analysis/diagnostics.json)
- [Reproduction notes and source snapshots](../mapreference/spider-masterpiece-analysis/reproduce/README.md)

Source SHA-256 hashes:

```text
862154  2E1FB6BEADE337DEACF5EED643CCBD2FF94DB624FA2F79CF752BB9562230B99D
292577  36A3A3DEBC64F42E62797012D8A2F8744A13F9ABB9DC6E99436B801094DB7A85
```
