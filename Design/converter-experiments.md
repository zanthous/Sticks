# Converter experiments

Counterpoint is now the default conversion, building on the earlier Duet base.
Parity (`PA`) applies its angle changes to the complete arrangement, including
slider partners, accompaniment and deliberate stacked doubles. The separate
Duet (`DU`), Parity + Duet (`PD`) and Counterpoint (`CP`) selections are retired;
CP remains hidden for compatibility with older scores. The Conversion category
contains Difficulty Adjust, Parity and Encore. Encore remains opt-in for
additional note types. Authored Sticks maps retain their authored objects.

The observations and dated comparisons below retain their original mode names
and measurements. In the original parity audits, “Standard” means the converter
before Duet became the default, and “Parity” means Parity on that older base.
In the Counterpoint comparisons, “Default” is the previous Duet-based converter
and “CP” is the candidate arrangement tested alongside it. Those results predate
Counterpoint's promotion; they are not new measurements of today's mod selection.

## Encore click placement — 2026-09-13

Clicks remain opt-in through Encore. They replace isolated flicks at source
accent hitsounds or bar starts, retaining the source time and samples. They do
not split double heads or add a button press inside a sustain. Both the source
rhythm and the complete converted gestures must provide space before and after
the click; earlier holds and sliders count through their ends.

At source OD 3 or below, surrounding clearance is at least 500 ms and one local
beat, and clicks are separated by at least eight seconds and 16 local beats.
These requirements decrease continuously up to OD 7, where they become 150 ms
and 0.45 beats of surrounding clearance, and four seconds and eight beats between
clicks. The larger of each time/beat pair applies. The 0.45-beat value leaves room
for rounded half-beat timestamps on harder maps. Hitsounds cannot bypass spacing.
OD expresses intended difficulty here; actual source and converted spacing still
decide whether any individual accent is suitable. A map can receive no clicks.

Verification of the earlier Duet promotion used the same 43-map local corpus and
a saved ruleset DLL. Default and Parity each match all 32,985 heads and star
ratings of the former Duet and Parity + Duet outputs respectively. Encore clicks
fall from 2,431 to 411 across the corpus; in the five OD ≤4 maps they fall from
107 to 15, with at least
600 ms of clearance to any other occupied gesture in that sample. There are no
remaining click overlaps with directional gestures, and Parity leaves click times
and hand assignments identical. These are corpus observations, not a guarantee of
the same counts on every map. Local exports and summary are in
`mapreference/default-promotion/`; the testbed README documents reproduction.

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

This is an angle experiment, applied after the base converter's two-stick
pattern selection.
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

The base first builds the existing conversion, preserving its chords,
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
  endpoint fit or intermediate-angle tolerance is applied. A voice whose full
  angular excursion fits within the CS-derived primary aim window keeps its individual flicks
  beside the other voice's slider. If neither voice needs tracking, the paired
  slider candidate is declined. This tests whether movement is required; it does
  not reshape source directions or limit their variety.
  DA's explicit angle-window override is applied later and does not reselect these
  patterns; the diagnostic export uses the actual final window.
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

### Jump and accompaniment preservation — 2026-09-13

Rapid jumps are identified before any circle-to-sustain planning. A run of at least
four circles is protected when each interval is within the existing 260 ms
alternation threshold and consecutive source circles do not overlap spatially.
Spacing uses osu!standard's CS-dependent circle diameter, following the
[upstream circle size calculation](https://github.com/ppy/osu/blob/master/osu.Game.Rulesets.Osu/Objects/OsuHitObject.cs).
Original 2D positions distinguish jumps even when two targets share a radial
bearing. Rests, duration objects and timing changes break a run.

Protected runs retain their manual heads and source directions instead of becoming
synthetic holds, paired sliders or a compressed small-angle stream. The two sticks
still alternate at rapid intervals. Short bursts and compact streams also keep their
individual directions instead of being rewritten into the historical 30-degree
stream template. Existing source sliders retain their conversion.
Independent accompaniment flicks also retain their source directions throughout the
current base; they are no longer moved onto the sustaining stick's path. Historical
Standard/Parity strategies remain available to comparison tools with their previous
behavior. Public Parity applies after the new base pattern selection.

The supplied *Masterpiece* (292577) and *Warota* (129847) examples showed why keeping
timestamps as slider ticks is insufficient: eight widely spaced jump attacks could
become two slow tracks. Testbed reports now measure source circle onsets retained as
manual heads separately from generated sustain counts and angular excursion. Use
those measurements alongside chords and simultaneous patterns when evaluating
future changes; an increase in sustained overlap alone is not an improvement.

The 45-map gameplay-pipeline comparison restores all 1,128 circle onsets in
*Masterpiece* (previously 1,062) and all 585 in *Warota* (previously 539). Across
the corpus, retained circle onsets rise from 20,236 to 20,437 of 20,484. Chords
remain at 1,197; dual-sustain overlap falls from 125.50 to 97.23 seconds as jump
substitutions are removed. All original duration-object geometry remains identical
in Default, and all four modes pass conversion validation.

Among 2,137 matched flick/slider interactions, 75 previously exactly aligned flicks
now retain independent directions across nine maps. Their median separation from
the other stick's slider is 49.07 degrees. The new mixed timed-voice branch is
covered by synthetic tests; this corpus did not select new instances of it.
Before/after exports and repeatable analysis scripts are gitignored under
`mapreference/converter-jump-revision/`. The complete test suite passes 650 tests.

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

## Counterpoint experiment

Counterpoint began as the optional `CP` experiment on the Duet-based converter.
The arrangement logic described here is now enabled by default and is the base
for Parity. Encore can add its optional note types afterward. Authored Sticks
maps retain their authored objects. The iteration records below preserve the
comparisons made while Counterpoint was a separate mod.

The human references suggest distinct roles for the two hands: *Flower Dance*
uses a lead hand with sparse opposite-hand accents and recognizable repeated
phrases; *tbh i dont like being social* introduces a second sustain partway through
the first and hands off at releases; *Hyperspeed* places a separate pulse beneath
longer tracking gestures and reserves exact stacked doubles for selected accents.
These are arrangement ideas, not templates to copy onto every passage.

The arrangement planner considers seven candidate families:

- **Rhythm answer:** opposite-hand flicks at existing source slider repeats,
  ticks or tails, supported by a recurring nearby source-head pulse or a specific
  clap/finish on that slider node. Repeat/tail status alone is not an accent.
  The planner compares recovery-spaced responses instead of taking the earliest
  available checkpoints. Directions come from the surrounding source phrase.
- **Staggered voice:** a second slider enters at a source checkpoint and ends at
  another checkpoint. Its timed movement retains the selected portion of the
  original slider, including reversals; the source contour can suggest parallel
  or contrary motion.
- **Phrase lead:** a suitable slower circle phrase stays on one hand around an
  existing opposite-hand accent, or receives a sparse accent where there is room.
- **Phrase support:** that articulated lead phrase receives an additional timed
  slider on the other hand. Every circle remains a manual flick; the supporting
  path keeps its source directions and timings without smoothing them.
- **Punctuation:** an exact stacked double marks a source phrase arrival,
  resolution or newly emphasized hitsound, where both hands have recovery room.
- **Rhythmic voices:** four recurring attacks establish a steady part for one
  hand while the other plays changing fills. Short sliders and holds remain
  articulated. The pulse can start off the timing grid or after a short pickup;
  its phase and period come from existing attacks. Uniform alternation and rapid
  jump runs are preserved. This is a bounded phrase detector, not instrument
  separation or a rule that every offbeat belongs to one hand.
- **Staggered phrase:** a second slider enters during the first, then an available
  hand plays a short answer using existing attacks and short sustains. Both hands
  are checked, existing doubles keep both heads, and a single closing attack can
  serve as the answer when the source phrase ends there.
  The added voice can recall a nearby source slider's full signed contour and
  relative segment timing, fitted to its entrance/release interval, rather than
  always copying or mirroring the lead. This interpretation cannot cross a rest
  of more than two beats or a timing change. It adds no source-head replacements.

Existing head types, timings, slider durations and signed relative segments remain
intact. Arrangements may change hand assignments, but protected rapid jump runs
keep their original alternation. Directional doubles whose visible primary arcs
intersect by at least half of the narrower arc become an exact purple stack;
shallower overlaps and separated doubles retain their directions. CP uses the
actual source-CS/EZ/HR width or an explicit Difficulty Adjust angle override.
This approved chord rotation is the exception to preserving absolute directions.
Parity also preserves intentional exact stacks, resolving both hands together
before recording their actual final endpoints for subsequent gestures.
Added sliders must require movement beyond the aim window and obey
the existing speed limit. New paths retain at least the existing 250 ms between
actual reversals, including the initial and final spans; same-direction control
points do not impose extra reversal restrictions. Reversal and tail accents retain their authored node
sounds; tick entries do not replay a head-only clap or finish accent.

Candidates reserve the complete occupied interval on each hand, including release
and rearming time. Recovery and phrase spacing depend on source difficulty and
local tempo. Changing hand roles without adding notes needs recovery room but
avoids the longer cooldown used for additional attacks. Low difficulties skip
lead, support, rhythmic-voice and staggered families; slider answers are limited
to supported, spaced releases. New doubles have a separate sparse
budget and avoid nearby doubles already supplied by the base converter.

Repeated source phrases are recognized from rhythm, object types and geometry.
Overlapping windows within one passage do not count as independent repetitions.
Source combo boundaries can establish a phrase, including an offbeat pickup;
three-head groups require that explicit boundary. A combo start alone does not
justify a standalone double. The planner remembers the selected family, relative
rhythm and lead hand for
matching phrases, while allowing different arrangements when occupancy prevents
that recipe. Timing changes end rhythmic context. Candidate choice remains
deterministic; variety is tied to source structure rather than random placement.

Reference analysis and iteration exports are kept locally under the gitignored
`mapreference/counterpoint/` directory. Evaluate retained manual attacks and source
directions alongside simultaneous play; more overlapping duration alone does not
establish an improvement.

### Counterpoint iterations — 2026-09-13

Four gameplay-pipeline exports cover the same 45 procedural maps and five authored
references, with CP alone and with Parity/Encore. The first candidate version added
only contrary-motion sustains and almost never changed a phrase's hand roles.
Subsequent passes used the source contour for motion direction, reserved easy-map
answers for releases, remembered complete phrase recipes, and recognized source
combo boundaries and existing double accents. A separate offbeat-combo regression
caught an inappropriate whole-beat accent gate on supporting sliders.

| CP additions or changes, 45 procedural maps | First candidate | Final candidate |
| --- | ---: | ---: |
| Added heads | 964 | 923 |
| Extra sustained overlap | 76.58 s | 76.06 s |
| Parallel / contrary added overlap | 0 / 76.58 s | 63.92 / 12.15 s |
| New double onsets | 41 | 26 |
| Existing heads reassigned between hands | 1 | 11 |
| Existing heads removed or reangled; paths reshaped | 0 | 0 |

All 34,927 baseline heads retain their type, timing, angle and duration/path with
plain CP; 11 change hand. Added heads comprise 271 repeats, 543 true releases,
83 ticks and 26 source head accents. Checkpoints use osu!'s shared
[slider event generator](https://github.com/ppy/osu/blob/master/osu.Game/Rulesets/Objects/SliderEventGenerator.cs),
including mirrored tick placement on reverse spans and disabled ticks. The first
candidate's approximate repeat-phase ticks were replaced rather than accepted as
source evidence.

Default, Parity, Encore and Parity+Encore outputs and stars exactly match the saved
pre-CP baseline on all 45 maps. All five authored maps bypass CP unchanged. These
exports report no invalid geometry, procedural same-hand sustain overlaps or lost
source onsets. CP+Parity can reangle earlier patterns because added gestures enter
its history; CP+Encore can change click selection to retain clearance.

The new lead arrangements occur in Flower Dance and two Blue Zenith difficulties.
Staggered entries occur in nine maps, rather than every suitable-looking phrase being
forced into two sliders. Phrase support has a positive gameplay-converter
regression but is absent from this development corpus; its speed and meaningful
movement checks remain in place. Masterpiece stays unchanged, while Warota receives
four checkpoint/release responses and retains every jump attack.

The measurements establish preservation and the kinds of coordination generated.
They do not establish that a passage feels better on a controller. In particular,
release answers are still the most common addition and supporting phrases remain
rare. These measurements preceded the follow-up changes and eventual promotion
to the default converter.

A separate, preselected holdout used six Featured Artist Insane difficulties from
Creo, Frums, cYsmix, Kurokotei, Silentroom and Rameses B, selected before checking
conversion outputs or ratings. They converted to 4.64–5.58 stars on the default.
CP retained all 3,316 baseline heads and their exact shapes, reassigning one hand;
it added 142 heads (71 tails, 45 repeats, 16 ticks, 10 source head doubles), two
staggered entries and no same-hand overlaps. Phrase support was also absent from
this holdout. No gameplay changes were made in response to those results.

The Release solution builds without warnings, and all 713 tests pass. Regression
coverage includes source retention, protected jumps, offbeat supporting phrases,
existing-double reuse, signed arcs beyond 180 degrees, node hitsounds, authentic
reverse-span ticks, repeated motifs, cancellation, authored bypass, converter
reuse and conversion-mod order. Saved converter-only benchmarks measured CP at
roughly 2–2.5 times default conversion time on six maps (about 6–70 ms locally),
excluding decode and difficulty calculation. These are local CPU measurements,
not gameplay frame timings. Lazy motif analysis and indexed occupancy reduced
work on passages where the experiment makes no change.

### Counterpoint follow-up — hand roles and deliberate doubles

The approved follow-up adds pulse/fill hand assignments for mixed flick/short-slider
phrases, source-rhythm selection for additional attacks, and staggered phrases
that can recall a nearby slider contour. It also merges directional doubles when
at least half the narrower visible arc overlaps and preserves exact stacks through
Parity. These are playable conversion changes, not audio stem separation.

The final iteration-07 gameplay exports cover the 45-map development sample, six
Featured Artist holdout maps, and five authored references. Plain CP preserves all
38,243 baseline heads' kinds, times, durations and signed slider paths. There are
43 hand-only changes and 236 head rotations, all attributable to the approved
midpoint stack cleanup. All 4,912 protected jump heads are unchanged. Default and
Encore outputs and stars are unchanged; Parity's preservation of existing stacks
intentionally changes some PA outputs. Authored references remain identical in
all eight tested mod combinations.

Actual selection diagnostics show 12 rhythmic-voice arrangements across three
maps, including Manic's 400 ms pulse with independently timed fills and short
sliders. Five staggered phrases occur across three maps: Why do you hate me?,
flying in the flow of deep-sea, and Exit This Earth's Atomosphere. The latter two
change two existing hand assignments apiece as well as adding a second slider.
The borrowed paths in the first map provide a different secondary contour while
retaining its existing answering notes. These counts establish exercised behavior,
not a numerical measure of musical quality.

The cited Red Haze short tail additions are removed, while Flower Dance Normal's
supported 600 ms clap-tail accents remain. Frums' 65.076351s release remains
because it continues the preceding mapped pulse; its short gap after the pickup
head is not by itself grounds for rejection. The unsupported 72.48034s release
is removed. Existing note timing is never moved to make these responses fit.

Release build: zero warnings/errors; 795 tests pass. The tests include pickup
phase, supported/unsupported short tails, interval recovery, signed contours,
reversal spacing, half-overlap boundaries, DA/EZ/HR visual widths, paired Parity
history, and mod-order/reuse behavior. Local exports and analyses are
`mapreference/counterpoint/iteration-07*` and `holdout-revision-07*`. The warmed
six-map converter benchmark measured CP medians of 9–111 ms (2.2–3.5 times the
base converter); this measures conversion work, not gameplay rendering. Pattern
feel still needs playtesting, and the local sample does not represent every map.
