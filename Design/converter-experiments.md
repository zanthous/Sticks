# Converter experiments

The Parity (`PA`) and Duet (`DU`) modifiers let us compare two separate procedural
conversion ideas with the existing converter. They are experiments, not claims
that either approach is a better default. Authored Sticks carrier maps retain their
authored objects; the reference maps below are examples to study and play directly,
not inputs from which to judge a new procedural conversion.

Parity + Duet (`PD`) combines both experiments: it builds the complete Duet output,
including added slider partners and accompaniment, then applies Parity to every
gesture. Timings, stick assignments, chords, durations and relative slider arcs
stay as Duet generated them. A final cleanup aligns nearly coincident chord heads,
then synced-note links use those final angles.

## Authored reference observations

The five local `.osz` archives in the ignored `mapreference/` directory contain
2,326 authored Sticks objects. Counts below come from decoding their `sticks-v1`
and `sticks-v2` sample markers, including the stored side, angle, and duration.
An opposite-stick flick counts as accompaniment only when its start is strictly
inside a sustain, excluding its head and tail. Duration overlaps require positive
overlap; simultaneous heads use the converter's 0.01 ms tolerance.

| Map | Flicks | Holds | Sliders | Two-stick chords | Cross-stick sustain overlaps | Flicks inside an opposite sustain |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DJ OKAWARI — Flower Dance | 483 | 259 | 122 | 45 | 0 | 0 |
| EMIRI — Self-Destructive Girl feat. Hatsune Miku (Cut Ver.) | 188 | 65 | 67 | 6 | 6 | 14 |
| Nanahira × Camellia — Mofucchatte\*Summer! Summer!! Summer!!! (Short Ver.) | 74 | 23 | 14 | 1 | 1 | 7 |
| Sharpnel High Speed Music Team — Exciting Hyper Highspeed Star | 433 | 107 | 26 | 5 | 0 | 8 |
| luvlxckdown — tbh i dont like being social | 379 | 19 | 67 | 17 | 14 | 37 |
| Total | 1,557 | 473 | 296 | 74 | 21 | 66 |

All five maps have zero same-stick duration overlaps. All 296 sliders have a
single arc, including those encoded with the segmented v2 format. These maps
therefore provide examples of two-stick coordination without requiring reversals.
The 66 accompaniment flicks occur within 54 different sustains.

Representative passages, with times measured from the beatmap timeline:

- **Flower Dance, 46.397 s:** left hold at 210° for 150 ms with a simultaneous
  right flick at 0°. A flick chord follows at 47.597 s. Across the map, 25 chords
  combine a flick and a short hold, and 20 combine two flicks. Several sections
  place chords every two beats. Chord gesture type provides variety even without
  sustained two-stick overlap.
- **Self-Destructive Girl, 85.482 s:** both sticks start sliders at 30° and follow
  identical −75° arcs for 750 ms. Five similar paired gestures occur at 86.7945,
  88.482, 91.482, 92.7945, and 94.482 s. The second pair lasts 937.5 ms. This is
  synchronized movement in the same direction.
- **Summer, 35.4386 s:** both sticks begin at 270°, then follow opposite −180°
  and +180° arcs for 5,625 ms. This is synchronized movement in opposite directions.
- **tbh, 12.5918 s:** a left slider lasts 524.659 ms; a right slider begins
  349.773 ms later and lasts 349.773 ms. Their staggered heads create an interval
  of simultaneous tracking without a simultaneous head chord.
- **Hyper Highspeed, 69.4032 s:** a right slider moves +114.77° over 1,498.776 ms
  while left flicks occur at offsets 166.531, 499.592, 832.653, and 1,165.715 ms.
  Their angles are approximately 175.7°, 248.0°, 307.0°, and 271.6°. The tapping
  hand has its own geometry rather than following the slider's instantaneous angle.
- **Summer, 25.2433 s:** a right hold at approximately 340.7° lasts 1,054.688 ms;
  left flicks at offsets 351.5625, 585.9375, and 820.3125 ms move through roughly
  257.4°, 224.7°, and 190.5°. One stick provides a stable gesture while the other
  follows a changing pattern.
- **tbh, 15.7398 s and 82.8961 s:** the same slider and accompaniment choreography
  returns after exactly 192 beats. Repeating musical phrases can have recognizable
  repeated movement, rather than receiving unrelated variation on each occurrence.

The archives are local design material, not checked-in fixtures or runtime
dependencies. Their authored angles do not recover the geometry of an original
osu!standard map. Motif recognition in source maps is a design inference from
these examples, not a measurement of the mapper's conversion process.

## Parity experiment

The Discord suggestion describes alternating motion by approximately 135° between
successive gestures on the same stick. Parity now uses that angle as a preference
whose strength follows the section's rhythm and geometry. It measures each turn
from the previous **unmodified** gesture endpoint to the next unmodified head,
expands its magnitude, then applies that signed turn at the converted endpoint.
Moving source patterns therefore keep their clockwise or counter-clockwise flow;
they no longer chase the nearest boundary relative to an already-corrected angle.

Each stick tracks exponentially smoothed source-turn magnitude and free time
between gestures, with a two-local-beat time constant. Averages begin with the
phrase's first observation, rather than fixed seed values. Short, compact source
patterns receive the strongest parity bias. Slower sections and already-broad
source turns receive less. The strength varies continuously; there are no angle
buckets, randomly selected section styles, or random per-note offsets.

For source-turn magnitude `d`, mean source magnitude `m`, and mean free gap in
beats `g`, the expanded turn is `d + strength * (180 - d)`, where
`strength = (135 / 180) / ((1 + (m / 180)^2) * (1 + (g / 2)^2))`.
This preserves the ordering of source-turn magnitudes at a fixed context without
imposing a minimum output angle. Exactly repeated source directions can still
alternate deliberately. Zero and straight-reversal source turns use a mirrored
tie-break between sticks; other turns use their original sign.

Slider history includes its actual repeat/custom-path endpoint. Rotating the head
preserves the entire relative path. A rest of two beats still starts a new phrase,
anchored to its source heading. This approach reinterprets the source's spatial
contour: accumulated headings can drift farther from the original angles even
though their turning direction is retained. That is a playtesting tradeoff.

This is an angle experiment, separate from Duet's two-stick pattern selection.
The Beat Saber forehand/backhand illustration motivates the trial; it does not
establish an equivalent ergonomic rule for a joystick that rearms through its
neutral region. Compare the resulting feel, readability, and preservation of
source patterns before drawing conclusions.

### Original hard-correction audit — 2026-09-11

The following measurements describe the previous implementation, which snapped
smaller turns to exactly +135° or −135° from the last converted endpoint.

The test used all 43 downloaded procedural maps: the 42-map conversion corpus and
the supplied CS 9 map, *A New Summer Adventure! [Insane]* (3550412). Standard,
Parity, Duet and Parity + Duet were converted through the actual gameplay mod
pipeline. The analysis reproduced all 63,978 PA/PD heads and slider endpoints
with **zero angular error** before testing alternative reset policies. No gameplay
conversion was changed for this audit.

The strongest effect is repetition of relative directions. For PD, the matched
sample contains 33,044 heads, 30,610 same-stick transitions and 10,764 three-flick
sequences. Transitions exclude first notes, overlapping gestures and gaps of two
local beats or more after the previous gesture ends. Those same comparison events
are used for every reset variant.

| Measurement | Duet | PD, current | PD, reset disabled | PD, preserve turn preference across resets |
| --- | ---: | ---: | ---: | ---: |
| Turns exactly 135° (±0.01°) | 0.08% | 84.24% | 83.77% | 84.24% |
| Third flick returns within 3° of the first | 2.59% | 63.33% | 63.15% | 63.34% |
| Flicks in runs of 4+ on one 45°-spaced set of directions | 0.60% | 50.32% | 50.19% | 50.25% |

Every one of the 43 maps increased its exact-135° and three-flick return rates.
The median map's return rate rose from 2.61% to 63.04%, so this is not just a few
long maps dominating the pooled counts. Parity alone behaves similarly: 83.83%
exact-135° turns and 62.69% returns. Exact 180° turns are only 0.07% with PD;
the pile-up is specifically at the lower boundary.

The absolute compass directions are much less concentrated. The busiest 15° bin
falls from 5.12% of Duet heads to 4.53% with PD, and normalized angular entropy
increases in 35/43 maps. There is still local concentration onto rotated sets of
eight directions: the phase-independent eightfold concentration increases in
30/43 maps, although its pooled value falls. Repeated ±135° steps preserve the
starting angle modulo 45°, explaining how short patterns can repeat while the
combined histogram looks nearly uniform.

Disabling the two-beat reset changes the three-flick return rate by only −0.19
percentage points. Resetting after one beat reduces exact-135° turns to 50.06%
and returns to 44.34%, but it also exempts far more notes from correction: 15,708
rest resets versus 2,348 currently. Preserving only the turn preference barely
matters; exact-cost ties account for 301 of 25,696 corrected PD heads (1.17%).

The seven approximately 5-star Featured Artist references show the same effect:
83.61% exact-135° turns and 55.73% returns with PD, versus 0.08% and 2.60% with
Duet. On the supplied CS 9 map, PD gives 81/92 turns at exactly 135° and 39/52
three-flick returns (75%, versus 5.77% with Duet).

Synthetic controls further separate the mechanisms. Source directions
`13, 15, 17, 19` become `13, 148, 13, 148`. After a rest, a new source phrase
`32, 34, 36, 38` becomes `32, 167, 32, 167`; removing resets keeps using
`13, 148, 13, 148`. Rotating the inputs by 17° rotates the outputs by 17°.
The reset therefore does not impose a fixed compass direction; the hard correction
creates the repeated movement, anchored to whichever direction starts the phrase.

These measurements support softening the exact-135° correction before changing
the rest policy. They quantify lost movement variety; perceived sameness still
requires playtesting. The corpus is the available local sample, not a random
sample of all osu! maps.

Reproduction commands are in the [testbed README](../osu.Game.Rulesets.Sticks.DifficultyTestbed/README.md#parity-angle-audit).
Local artifacts are gitignored: [plots](../mapreference/parity-audit/parity-angle-audit.png),
[per-map and reset statistics](../mapreference/parity-audit/analysis.json), and
[actual converted objects](../mapreference/parity-audit/objects.json).

### Flexible parity comparison — 2026-09-11

The same 43 maps were exported again through the actual gameplay pipeline after
the change. Standard and Duet objects match the original export exactly. All
63,978 PA/PD heads retain their types, timings, stick assignments, durations and
relative slider geometry. These measurements compare actual exports; they do
not replay the old hard-correction algorithm against the new output.

| Measurement | Original PD | Flexible PD |
| --- | ---: | ---: |
| Turns exactly 135° (±0.01°) | 84.24% | 0.03% |
| Third flick returns within 3° of the first | 63.33% | 3.86% |
| Flicks in runs of 4+ on one 45°-spaced set | 50.32% | 0.00% |
| Median turn | 135.00° | 123.38° |
| 10th–90th percentile turn | 135.00–149.82° | 102.83–154.47° |
| Turns below 90° | 0.00% | 1.92% |

Every map reduces PD's exact-135° and three-flick-return rates. PA alone has
0.03% exact-135° turns, 3.65% returns and a 124.61° median turn. Eight-local-beat
windows also show increased variation between sections: among the 30 maps with
at least two stick/window samples containing eight eligible transitions, the
median map's standard deviation of section mean turns rises from 2.61° to 5.14°.
Repeated source phrases can still repeat; variation is not added for its own sake.

The supplied CS 9 map now rates 5.55★ with PD because its converted angles changed;
the approved CS scaling formula is unchanged. The altered spatial contour and
section feel still need playtesting.

The [comparison command](../osu.Game.Rulesets.Sticks.DifficultyTestbed/README.md#compare-actual-parity-exports)
produces gitignored [per-map and section results](../mapreference/parity-flexible/comparison.json)
from the [new converted objects](../mapreference/parity-flexible/objects.json).

### Nearly coincident doubles — 2026-09-12

Duet accompaniment aims an added flick from nearby source notes independently of
the simultaneous slider head. This can produce a double whose angles differ by
only a few degrees. Parity can also bring independently converted stick directions
close together. The renderer distinguishes these from exact overlaps, leaving
separate edges and ticks.

Every procedural converter now finishes by aligning opposite-stick doubles whose
angles differ by at most 5°. Both heads move to the circular midpoint, so each
moves at most 2.5°, including across 0°/360°. The pass runs after parity and before
synced-link assignment. It uses the existing simultaneous-group tolerance
(less than 0.01 ms), applies only to groups of exactly two opposite-stick heads,
and leaves authored carrier angles intact. Slider paths rotate with their heads;
their timing, arcs and repeat motion are preserved.

The 43-map before/after check found 86 such doubles in Duet, 13 in PD and 5 in PA.
All 104 now have exactly equal head angles; the largest adjustment was 2.48857°.
All other heads match exactly, and object/chord counts are unchanged. The local
[verification script](../mapreference/chord-alignment/verify_alignment.py) and
[results](../mapreference/chord-alignment/verification.json) are gitignored.

## Duet design rationale

Duet explores how source rhythm and radial geometry can suggest complete
two-stick phrases. Existing conversion already has occasional four-anchor
staggered sliders and five-anchor slider accompaniment. A meaningful experiment
should offer more than increased frequency of those exact templates.

Duet first builds the complete standard conversion, preserving its existing chords,
holds and generated slider phrases. It then evaluates unclaimed circle windows and
commits a complete phrase only after validating both sticks. Both stick assignments
are tried before rejecting a candidate. The available candidate types are:

- **Nested gestures, four anchors:** an outer sustain joins the first and last
  anchors, while an inner opposite-stick sweep joins the middle two. An
  approximately A–B–C–A angular shape becomes an outer hold with an inner moving
  slider; moving outer endpoints can instead produce two nested sweeps.
- **Interleaved voices, four, six, or eight anchors:** even and odd anchors form
  separate stick trajectories, allowing overlapping motion with staggered heads.
  Each moving voice retains every source direction at its original timestamp.
  Speed changes, stationary intervals and reversals remain in the path; no smooth
  endpoint fit or intermediate-angle tolerance is applied. An entirely stationary
  voice becomes a hold only when its existing tick rhythm covers the interior notes.
- **Sustain and independent flicks, five to nine anchors:** one stick connects
  phrase endpoints while the other keeps the interior source angles. A compact
  angular pattern can become a hold; a moving pattern can become a slider. The
  tapping rhythm must leave more than 260 ms between consecutive source events.
- **Chord phrases, four or five anchors:** repeated angular jumps or multiple
  clap/finish accents become coordinated two-stick heads. The phrase uses a
  reflected relationship for jumps or a parallel relationship for compact accents.
- **Paired isolated source sliders:** a source slider can acquire a simultaneous
  partner when both lanes are available. Source placement selects reflected or
  parallel movement, and both sliders keep the source duration and repeat behavior.
- **Native slider accompaniment:** an ordinary source slider can receive up to two
  opposite-stick flicks at its head, repeat/tick checkpoints, or half-beat pulses
  supported by nearby source rhythms. Head accents form additional chords. Phrases
  start at least four beats apart; additions respect existing objects and 260 ms
  recovery margins. Opposite-stick source directions guide the new flicks, with the
  final converted slider path used when no neighbouring direction is available.

Candidate windows contain at most nine circles, stop at timing changes, and follow
straight or triplet rhythmic grids. Phrase starts are on beats or after rests,
with at least eight beats between accepted phrase starts. A geometry score chooses
among valid alternatives; a clear outer hold with an inner sweep has priority.
Interleaved paths use the shortest signed turn between consecutive anchors of
that voice. Their source times determine each segment's duration, so the 120°/s
limit is checked on every segment. Reversing candidates are skipped when reversals
are disabled. The existing initial separation/contrary-motion selection rule and
phrase cooldown remain in place. These choices are deterministic for the inspected
local context, but the converter does not yet identify matching phrases across an
entire song or chain sustained handoffs between phrases.

Source angles remain the main geometric input. Absorbed circles become duration
heads, tails, or tick checkpoints. Interleaved moving paths preserve the source
samples at every anchor: actual turns back produce reversal checkpoints, while
speed changes and pauses use tick checkpoints. Their timing survives the timed
slider carrier format (v3); existing v1/v2 carriers remain supported. Other generated
slider heads and tails retain their source samples, while a generated hold uses
its head samples for its tail. This conversion does not preserve every individual
source hitsound accent. A movement template is useful
when its geometry fits the source and its rhythm leaves enough room for both sticks.

Every accepted phrase must reserve its final stick assignments and entire sustain
intervals, including generated partners. A stick needs time to release and rearm
before a new head. An opposite-stick head at a sustain tail is a different case
from asking the sustaining stick to flick again at that instant. New overlapping
patterns should preserve the existing generated slider speed limit of 120°/s;
faster authored reference sliders are not a reason to raise it automatically.

### Interleaved paths without smoothing — 2026-09-12

The 20° endpoint-fit rejection is removed along with the fit itself. Across the
43-map corpus, Duet now produces 34 timed voices, including 18 that reverse.
All 124 retained anchors match the baseline source directions and timestamps;
maximum measured angular error is below 0.00002°. Every segment stays within
120°/s. Chords remain at 1,195, while dual-sustain overlap increases from 100.35
to 108.63 seconds. PA/PD retain their own rigid path rotation after conversion.

All 503 automated tests pass, including exact intermediate timing and samples,
unequal speeds, returns, dwells, reversal settings, preview geometry and carrier
roundtrips. The final 43-map four-mode comparison has no validation issues.
Local captures are under `mapreference/duet-unsmoothed/` and remain gitignored.

## Manual comparison

1. Choose an ordinary osu!standard source difficulty and compare the baseline,
   `PA`, and `DU` separately with the same rate and gameplay settings. Keep a few
   short timestamp ranges so differences can be replayed directly.
2. Include sparse slider phrases, steady quarter/half-beat circles, dense streams,
   repeated geometric patterns, phrase boundaries, and timing changes. Watch for
   absent or distorted source accents as well as newly interesting gestures.
3. For `PA`, compare consecutive gestures on each stick, slider-to-flick
   transitions, and the first note after a rest. Record whether forced separation
   improves physical flow or makes recognizable source patterns harder to read.
4. For `DU`, check whether two-stick phrases have clear starts and endings, whether
   one hand's work remains readable while the other tracks a sustain, and whether
   repeated phrases feel consistent. Distinguish added simultaneous heads from
   actual intervals in which both sticks remain active.
5. Inspect release transitions at normal speed. Autoplay can check generated
   structure, but physical controller play is needed to assess neutral rearming,
   attention, and coordination. Record source map, modifier, timestamp, and the
   specific uncomfortable or successful gesture for follow-up tuning.
6. Play the authored reference passages directly to compare the intended types of
   coordination. Enabling a converter experiment on a carrier map should preserve
   its authored object data.
