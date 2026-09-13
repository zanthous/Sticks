#!/usr/bin/env python3
"""Compare the current arrangement converter with its historical base, without treating density as quality."""

import argparse
from bisect import bisect_left
from collections import Counter, defaultdict, deque
import json
import math
from pathlib import Path
import statistics

ROOT = Path(__file__).resolve().parents[1]
EPS = 0.01
ANGLE_EPS = 0.001
PAIRS = (
    ("Default", "Counterpoint"),
    ("Encore", "CounterpointEncore"),
    ("Parity", "CounterpointParity"),
    ("ParityEncore", "CounterpointParityEncore"),
)
PROMOTED_PAIRS = (
    ("LegacyBase", "Default"),
    ("LegacyBaseEncore", "Encore"),
    ("LegacyBaseParity", "Parity"),
    ("LegacyBaseParityEncore", "ParityEncore"),
)


def mode_pairs(schema):
    """Schema 5 promotes Counterpoint and labels the historical base explicitly."""
    return PROMOTED_PAIRS if schema >= 5 else PAIRS


def delta(a, b):
    return (b - a + 180) % 360 - 180


def duration(note):
    return max(0.0, note["endTime"] - note["startTime"])


def shape(note, side=True):
    """The export's existing 0.001 comparison precision; weights stay exact."""
    key = (note["kind"], round(note["startTime"], 3), round(note["endTime"], 3),
           round(note["angle"] % 360, 3), tuple(round(a, 3) for a in note["segmentArcs"]),
           tuple(note.get("segmentDurationWeights", [])), round(note.get("primaryHitAngle", 0), 3))
    return key + (note["side"],) if side else key


def match_existing(before, after):
    """Match exact objects, then hand-only moves, then remaining heads at the same onset."""
    unused_before = set(range(len(before)))
    unused_after = set(range(len(after)))
    matches = []
    for include_side, category in ((True, "exact"), (False, "handOnly")):
        candidates = defaultdict(deque)
        for index in sorted(unused_after):
            candidates[shape(after[index], include_side)].append(index)
        for index in sorted(unused_before):
            available = candidates[shape(before[index], include_side)]
            if available:
                counterpart = available.popleft()
                matches.append((index, counterpart, category))
                unused_before.remove(index)
                unused_after.remove(counterpart)

    # At a chord timestamp a new partner must not be mistaken for a modified original.
    # Exact and hand-only matches above have first claim on all original objects.
    candidates = defaultdict(list)
    for index in sorted(unused_after):
        candidates[round(after[index]["startTime"], 3)].append(index)
    for index in sorted(unused_before):
        old = before[index]
        options = candidates.get(round(old["startTime"], 3), [])
        if not options:
            continue

        def cost(counterpart):
            new = after[counterpart]
            # Two simultaneous sliders may exchange hands and then receive new
            # Parity rotations. Their retained signed paths identify them more
            # reliably than the final colour or starting angle.
            return (new["kind"] != old["kind"], abs(new["endTime"] - old["endTime"]),
                    new["segmentArcs"] != old["segmentArcs"],
                    new.get("segmentDurationWeights", []) != old.get("segmentDurationWeights", []),
                    new["side"] != old["side"], abs(delta(old["angle"], new["angle"])))

        counterpart = min(options, key=cost)
        options.remove(counterpart)
        unused_before.remove(index)
        unused_after.remove(counterpart)
        matches.append((index, counterpart, "modified"))
    return matches, sorted(unused_before), sorted(unused_after)


def nearest_time(times, time):
    if not times:
        return None
    at = bisect_left(times, time)
    options = times[max(0, at - 1):at + 1]
    value = min(options, key=lambda candidate: abs(candidate - time))
    return {"time": value, "distanceMs": abs(value - time)}


def union(intervals):
    merged = []
    for start, end in sorted(intervals):
        if end <= start:
            continue
        if not merged or start > merged[-1][1]:
            merged.append([start, end])
        else:
            merged[-1][1] = max(merged[-1][1], end)
    return merged


def length(intervals):
    return sum(end - start for start, end in intervals)


def intersection_length(first, second):
    total = 0
    j = 0
    for start, end in first:
        while j < len(second) and second[j][1] <= start:
            j += 1
        k = j
        while k < len(second) and second[k][0] < end:
            total += max(0, min(end, second[k][1]) - max(start, second[k][0]))
            k += 1
    return total


def segments(note):
    arcs = note.get("segmentArcs") or [0]
    weights = note.get("segmentDurationWeights") or [1] * len(arcs)
    total = sum(weights)
    start = note["startTime"]
    for arc, weight in zip(arcs, weights):
        end = start + duration(note) * weight / total
        yield start, end, arc
        start = end


def groups(notes):
    ordered = sorted(notes, key=lambda note: note["startTime"])
    result = []
    for note in ordered:
        if not result or note["startTime"] - result[-1][0]["startTime"] > EPS:
            result.append([note])
        else:
            result[-1].append(note)
    return result


def chord_readability(notes):
    """Check the approved half-of-narrower-window rule using final gameplay widths."""
    rows = []
    for group in groups([note for note in notes if note["kind"] != "Click"]):
        if len(group) != 2 or group[0]["side"] == group[1]["side"]:
            continue
        first, second = group
        distance = abs(delta(first["angle"], second["angle"]))
        first_width = first.get("primaryHitAngle", 0)
        second_width = second.get("primaryHitAngle", 0)
        overlap = max(0, min(first_width / 2, distance + second_width / 2)
                         - max(-first_width / 2, distance - second_width / 2))
        threshold = min(first_width, second_width) / 2
        if threshold <= 0:
            continue
        if overlap >= threshold - 1e-5 and distance > ANGLE_EPS:
            rows.append({"time": first["startTime"], "separationDegrees": distance,
                         "overlapDegrees": overlap, "requiredOverlapDegrees": threshold,
                         "firstWidth": first_width, "secondWidth": second_width})
    return rows


def allowed_stack_rotation(old, new, after):
    """A bounded midpoint rotation into a final exact two-hand stack is intentional."""
    group = [note for note in after if note["kind"] != "Click" and abs(note["startTime"] - new["startTime"]) <= EPS]
    if len(group) != 2 or group[0]["side"] == group[1]["side"]:
        return False
    if abs(delta(group[0]["angle"], group[1]["angle"])) > ANGLE_EPS:
        return False
    # Half overlap permits centre separation at most half the wider window.
    # Moving both centres to their circular midpoint moves each at most W/4.
    maximum_rotation = max(note.get("primaryHitAngle", 0) for note in group) / 4
    return abs(delta(old["angle"], new["angle"])) <= maximum_rotation + ANGLE_EPS


def beat_at(map_data, time):
    timeline = map_data.get("sourceTimeline", [])
    selected = max((note for note in timeline if note["startTime"] <= time + EPS),
                   key=lambda note: note["startTime"], default=timeline[0] if timeline else None)
    value = selected["beatLength"] if selected else 500
    return value if isinstance(value, (int, float)) and math.isfinite(value) and value > 0 else 500


def lead_hand_sections(map_data, before, after, matches):
    pairs = {old: new for old, new, _ in matches}
    sections, run = [], []

    def finish():
        if len(run) < 4 or not any(before[old]["side"] != after[new]["side"] for old, new in run):
            return
        first, last = after[run[0][1]], after[run[-1][1]]
        partner = [note for note in after if note["side"] != first["side"]
                   and first["startTime"] - EPS <= note["startTime"] <= last["startTime"] + EPS]
        overlap = union((max(first["startTime"], note["startTime"]), min(last["startTime"], note["endTime"]))
                        for note in after if note["side"] != first["side"] and duration(note) > 0)
        sections.append({"startTime": first["startTime"], "endTime": last["startTime"], "leadSide": first["side"],
                         "heads": len(run), "reassignedHeads": sum(before[old]["side"] != after[new]["side"] for old, new in run),
                         "previousHandSwitches": sum(before[a[0]]["side"] != before[b[0]]["side"] for a, b in zip(run, run[1:])),
                         "minimumHeadGapMs": min(after[b[1]]["startTime"] - after[a[1]]["startTime"] for a, b in zip(run, run[1:])),
                         "oppositeHeadsInsidePhrase": len(partner), "oppositeSustainOverlapMs": length(overlap),
                         "headTimes": [after[new]["startTime"] for _, new in run]})

    for index, old in enumerate(before):
        new_index = pairs.get(index)
        if new_index is None or old["kind"] != "Flick" or after[new_index]["kind"] != "Flick":
            finish()
            run = []
            continue
        new = after[new_index]
        if run:
            previous = after[run[-1][1]]
            gap = new["startTime"] - previous["startTime"]
            if index != run[-1][0] + 1 or new["side"] != previous["side"] or not EPS < gap <= beat_at(map_data, new["startTime"]) * 1.1:
                finish()
                run = []
        run.append((index, new_index))
    finish()
    return sections


def protected_jumps(map_data, before, after, matches, parity):
    if map_data.get("conversion") == "authored-bypass":
        return {"available": False, "reason": "authored-bypass"}
    source = map_data.get("sourceTimeline", [])
    diameter = map_data.get("sourceCircleDiameter")
    if diameter is None or any("positionX" not in note for note in source):
        return {"available": False}
    threshold = map_data.get("_rapidJumpMaximumIntervalMs", 260)
    minimum_heads = map_data.get("_rapidJumpMinimumHeads", 4)
    run_start = 0
    times = set()
    for index in range(1, len(source) + 1):
        if index < len(source):
            first, second = source[index - 1], source[index]
            positions = [first.get("positionX"), first.get("positionY"), second.get("positionX"), second.get("positionY")]
            if (all(isinstance(value, (int, float)) and math.isfinite(value) for value in positions)
                    and first["endTime"] <= first["startTime"] and second["endTime"] <= second["startTime"]
                    and EPS < second["startTime"] - first["startTime"] <= threshold
                    and (positions[2] - positions[0]) ** 2 + (positions[3] - positions[1]) ** 2 > diameter ** 2
                    and first["timingSectionStart"] == second["timingSectionStart"]):
                continue
        if index - run_start >= minimum_heads:
            times.update(round(note["startTime"], 3) for note in source[run_start:index])
        run_start = index
    lookup = {old: new for old, new, _ in matches}
    issues = []
    click_changes = []
    excluded_click_heads = 0
    allowed_rotations = 0
    matched_heads = 0
    for index, old in enumerate(before):
        if round(old["startTime"], 3) not in times:
            continue
        new_index = lookup.get(index)
        new = after[new_index] if new_index is not None else None
        if old["kind"] == "Click" or new is not None and new["kind"] == "Click":
            # Encore selects button clicks after directional arrangements. Its
            # choices and button sides can change while the protected underlying
            # directional conversion remains intact; report them separately.
            excluded_click_heads += 1
            if new is None or shape(old) != shape(new):
                click_changes.append({"time": old["startTime"], "before": old, "after": new})
            continue
        matched_heads += 1
        if new_index is None:
            issues.append({"time": old["startTime"], "issue": "removed"})
            continue
        for key in ("kind", "side", "startTime", "endTime", "segmentArcs", "segmentDurationWeights"):
            if old[key] != new[key]:
                issues.append({"time": old["startTime"], "issue": key, "before": old[key], "after": new[key]})
        if not parity and abs(delta(old["angle"], new["angle"])) > ANGLE_EPS:
            if allowed_stack_rotation(old, new, after):
                allowed_rotations += 1
            else:
                issues.append({"time": old["startTime"], "issue": "angle", "before": old["angle"], "after": new["angle"]})
    return {"available": True, "sourceOnsets": len(times), "existingHeadsChecked": matched_heads,
            "clickHeadsExcluded": excluded_click_heads, "independentClickChanges": click_changes,
            "allowedStackRotations": allowed_rotations, "angleCheckIncludesParity": False, "issues": issues}


def pulse_hand_sections(map_data, before, after, matches):
    """Measure a regular retained pulse owned by one hand, including short sliders."""
    sections = []
    for side in ("Left", "Right"):
        pairs = sorted(((old, new) for old, new, _ in matches
                        if after[new]["side"] == side and before[old]["kind"] != "Click"),
                       key=lambda pair: after[pair[1]]["startTime"])
        run = []

        def finish():
            if len(run) < 4 or not any(before[old]["side"] != after[new]["side"] for old, new in run):
                return
            first, last = after[run[0][1]], after[run[-1][1]]
            gaps = [after[b[1]]["startTime"] - after[a[1]]["startTime"] for a, b in zip(run, run[1:])]
            pulse_gap = statistics.median(gaps)
            phrase_end = last["startTime"] + pulse_gap
            partner = [note for note in after if note["side"] != side and note["kind"] != "Click"
                       and first["startTime"] - EPS <= note["startTime"] < phrase_end - EPS]
            if not partner:
                return
            sections.append({"startTime": first["startTime"], "endTime": phrase_end,
                             "lastPulseTime": last["startTime"], "pulseSide": side,
                             "pulseGapMs": pulse_gap, "pulseHeads": len(run),
                             "reassignedHeads": sum(before[old]["side"] != after[new]["side"] for old, new in run),
                             "pulseKinds": dict(Counter(after[new]["kind"] for _, new in run)),
                             "pulseTimes": [after[new]["startTime"] for _, new in run],
                             "oppositeHeadTimes": [note["startTime"] for note in partner]})

        for pair in pairs:
            if run:
                time = after[pair[1]]["startTime"]
                gap = time - after[run[-1][1]]["startTime"]
                first_gap = after[run[1][1]]["startTime"] - after[run[0][1]]["startTime"] if len(run) > 1 else gap
                if not EPS < gap <= beat_at(map_data, time) * 1.1 or abs(gap - first_gap) > max(1, first_gap * .05):
                    finish()
                    # The previous head can begin the next regular pulse.
                    run = run[-1:] if gap > EPS else []
            run.append(pair)
        finish()
    return sorted(sections, key=lambda section: section["startTime"])


def families(notes):
    directional = [note for note in notes if note["kind"] != "Click"]
    sustains = [note for note in directional if duration(note) > 0]
    counts = Counter()
    chord_times = []
    chord_angles = []
    for group in groups(directional):
        if len({note["side"] for note in group}) < 2:
            continue
        chord_times.append(group[0]["startTime"])
        count_sustains = sum(duration(note) > 0 for note in group)
        category = "flickChords" if count_sustains == 0 else "sustainChords" if count_sustains == len(group) else "mixedChords"
        counts[category] += 1
        for a in group:
            for b in group:
                if a["side"] == "Left" and b["side"] == "Right":
                    chord_angles.append(abs(delta(a["angle"], b["angle"])))

    for note in directional:
        active = any(other["side"] != note["side"] and other["startTime"] + EPS < note["startTime"] < other["endTime"] - EPS
                     for other in sustains)
        if active:
            counts["headsDuringOppositeSustain"] += 1
            counts["sustainEntries" if duration(note) > 0 else "flicksDuringOppositeSustain"] += 1
        if any(other["side"] != note["side"] and abs(other["endTime"] - note["startTime"]) <= EPS for other in sustains):
            counts["tailHandoffs"] += 1

    left = union((note["startTime"], note["endTime"]) for note in sustains if note["side"] == "Left")
    right = union((note["startTime"], note["endTime"]) for note in sustains if note["side"] == "Right")
    dual = intersection_length(left, right)
    occupied = length(union(left + right))
    coordination = Counter()
    for first in (note for note in sustains if note["side"] == "Left"):
        for second in (note for note in sustains if note["side"] == "Right"):
            if first["endTime"] <= second["startTime"] or second["endTime"] <= first["startTime"]:
                continue
            for a_start, a_end, a_arc in segments(first):
                for b_start, b_end, b_arc in segments(second):
                    overlap = min(a_end, b_end) - max(a_start, b_start)
                    if overlap <= 0:
                        continue
                    moving = (abs(a_arc) > ANGLE_EPS) + (abs(b_arc) > ANGLE_EPS)
                    category = ("bothStationary" if moving == 0 else "oneStationary" if moving == 1
                                else "sameDirection" if a_arc * b_arc > 0 else "oppositeDirections")
                    coordination[category] += overlap

    return {
        "counts": {key: counts[key] for key in ("flickChords", "mixedChords", "sustainChords", "tailHandoffs",
                   "headsDuringOppositeSustain", "flicksDuringOppositeSustain", "sustainEntries")},
        "duration": {"leftOccupiedMs": length(left), "rightOccupiedMs": length(right), "eitherOccupiedMs": occupied,
                     "bothOccupiedMs": dual, "onlyOneOccupiedMs": occupied - dual},
        "dualSustainMotionPairMs": {key: coordination[key] for key in ("bothStationary", "oneStationary", "sameDirection", "oppositeDirections")},
        "chordTimes": chord_times,
        "chordAngles": {"coincident": sum(angle <= .01 for angle in chord_angles),
                        "near": sum(.01 < angle <= 5 for angle in chord_angles),
                        "small": sum(5 < angle <= 45 for angle in chord_angles),
                        "medium": sum(45 < angle <= 90 for angle in chord_angles),
                        "wide": sum(angle > 90 for angle in chord_angles)},
    }


def source_events(map_data):
    timeline = map_data.get("sourceTimeline")
    if timeline is None:
        return None
    events = {"head": sorted({note["startTime"] for note in timeline}),
              "repeat": sorted({time for note in timeline for time in note.get("repeatTimes", [])}),
              "tail": sorted({note["endTime"] for note in timeline if note["endTime"] > note["startTime"]})}
    if all("tickTimes" in note for note in timeline):
        events["tick"] = sorted({time for note in timeline for time in note["tickTimes"]})
    return events


def checkpoint(note, map_data, events):
    if events is None:
        return {"kind": "unavailable"}
    time = note["startTime"]
    nearest = {kind: nearest_time(times, time) for kind, times in events.items()}
    aligned = [kind for kind in ("head", "repeat", "tail", "tick") if nearest.get(kind) and nearest[kind]["distanceMs"] <= EPS]
    if aligned:
        return {"kind": aligned[0], "allMatchedKinds": aligned}
    active = [source for source in map_data["sourceTimeline"] if source["startTime"] < time < source["endTime"]]
    grid_error = None
    for source in active:
        beat = source["beatLength"]
        if not isinstance(beat, (int, float)) or not math.isfinite(beat) or beat <= 0:
            continue
        quarter_beats = (time - source["timingSectionStart"]) / beat * 4
        error = abs(quarter_beats - round(quarter_beats)) * beat / 4
        grid_error = error if grid_error is None else min(grid_error, error)
    return {"kind": "quarterBeatGridInsideDuration" if grid_error is not None and grid_error <= EPS else "other",
            "nearestSourceEvents": nearest, "inferredQuarterBeatGridDistanceMs": grid_error}


def recovery(index, notes):
    note = notes[index]
    if note["kind"] == "Click":
        return {"kind": "clickIndependentOfDirectionalOccupancy"}
    same = [(other_index, other) for other_index, other in enumerate(notes)
            if other_index != index and other["side"] == note["side"] and other["kind"] != "Click"]
    overlaps = [other["startTime"] for _, other in same if
                other["startTime"] + EPS < note["startTime"] < other["endTime"] - EPS or
                note["startTime"] + EPS < other["startTime"] < note["endTime"] - EPS or
                abs(note["startTime"] - other["startTime"]) <= EPS]
    previous = [other for _, other in same if other["startTime"] < note["startTime"] - EPS]
    following = [other for _, other in same if other["startTime"] > note["startTime"] + EPS]
    previous_note = max(previous, key=lambda other: other["endTime"], default=None)
    next_note = min(following, key=lambda other: other["startTime"], default=None)
    before_gap = note["startTime"] - previous_note["endTime"] if previous_note else None
    after_gap = next_note["startTime"] - note["endTime"] if next_note else None
    previous_angle = ((previous_note["angle"] + sum(previous_note.get("segmentArcs", []))) % 360) if previous_note else None
    return {"beforeMs": before_gap, "afterMs": after_gap,
            "previousKind": previous_note["kind"] if previous_note else None,
            "nextKind": next_note["kind"] if next_note else None,
            "turnFromPreviousEndpointDegrees": abs(delta(previous_angle, note["angle"])) if previous_angle is not None else None,
            "overlappingOtherHeadTimes": overlaps}


def quantiles(values):
    values = sorted(value for value in values if value is not None and math.isfinite(value))
    if not values:
        return {"count": 0, "minimum": None, "p10": None, "median": None, "p90": None, "maximum": None}

    def at(fraction):
        index = (len(values) - 1) * fraction
        lower = math.floor(index)
        upper = math.ceil(index)
        return values[lower] + (values[upper] - values[lower]) * (index - lower)

    return {"count": len(values), "minimum": values[0], "p10": at(.1), "median": at(.5), "p90": at(.9), "maximum": values[-1]}


def compare_pair(map_data, baseline, experimental):
    before, after = baseline["objects"], experimental["objects"]
    matches, removed, added = match_existing(before, after)
    match_counts = Counter(category for _, _, category in matches)
    before_times = sorted({note["startTime"] for note in before})
    after_times = sorted({note["startTime"] for note in after})
    lost_onsets = [time for time in before_times if (nearest_time(after_times, time) or {"distanceMs": math.inf})["distanceMs"] > EPS]
    modified = []
    hand_changes = []
    for old_index, new_index, category in matches:
        old, new = before[old_index], after[new_index]
        if category == "handOnly":
            hand_changes.append({"startTime": old["startTime"], "kind": old["kind"], "from": old["side"], "to": new["side"]})
        if category != "modified":
            continue
        modified.append({"before": old, "after": new,
                         "kindChanged": old["kind"] != new["kind"],
                         "angleChanged": abs(delta(old["angle"], new["angle"])) > ANGLE_EPS,
                         "durationChanged": abs(duration(old) - duration(new)) > EPS,
                         "pathChanged": old["segmentArcs"] != new["segmentArcs"] or old.get("segmentDurationWeights", []) != new.get("segmentDurationWeights", []),
                         "sideChanged": old["side"] != new["side"]})
    events = source_events(map_data)
    added_heads = [{"note": after[index], "sourceCheckpoint": checkpoint(after[index], map_data, events),
                    "recovery": recovery(index, after)} for index in added]
    for head in added_heads:
        time = head["note"]["startTime"]
        beat = beat_at(map_data, time)
        local = [value for value in before_times if time - 4 * beat <= value <= time + 4 * beat]
        gaps = [second - first for first, second in zip(local, local[1:])]
        local_median = statistics.median(gaps) if gaps else None
        previous = max((value for value in after_times if value < time - EPS), default=None)
        following = min((value for value in after_times if value > time + EPS), default=None)
        nearest = min((abs(value - time) for value in (previous, following) if value is not None), default=None)
        head["manualCadence"] = {"localBaselineMedianGapMs": local_median, "nearestOtherOnsetGapMs": nearest,
                                  "gapToLocalMedianRatio": nearest / local_median if nearest is not None and local_median else None}
    before_families, after_families = families(before), families(after)
    old_chords = before_families["chordTimes"]
    new_chord_times = [time for time in after_families["chordTimes"] if
                       (nearest_time(old_chords, time) or {"distanceMs": math.inf})["distanceMs"] > EPS]
    chord_gaps = [second - first for first, second in zip(new_chord_times, new_chord_times[1:])]
    deltas = {section: {key: after_families[section][key] - value for key, value in before_families[section].items()}
              for section in ("counts", "duration", "dualSustainMotionPairMs", "chordAngles")}
    lead_sections = lead_hand_sections(map_data, before, after, matches)
    intentional_rotations = [entry["before"]["startTime"] for entry in modified
                            if "Parity" not in experimental["mode"] and entry["angleChanged"]
                            and allowed_stack_rotation(entry["before"], entry["after"], after)]
    return {
        "baselineMode": baseline["mode"], "experimentalMode": experimental["mode"],
        "stars": {"before": baseline["stars"], "after": experimental["stars"], "delta": experimental["stars"] - baseline["stars"]},
        "existingHeads": {"before": len(before), "after": len(after), "exactRetained": match_counts["exact"],
                          "retainedWithHandOnlyChange": match_counts["handOnly"], "modifiedAtSameOnset": len(modified),
                          "removed": len(removed), "added": len(added), "lostOnsets": lost_onsets,
                          "kindChanges": sum(note["kindChanged"] for note in modified),
                          "angleChanges": sum(note["angleChanged"] for note in modified),
                          "durationChanges": sum(note["durationChanged"] for note in modified),
                          "pathChanges": sum(note["pathChanged"] for note in modified)},
        "handChanges": hand_changes, "modifiedExistingHeads": modified, "removedExistingHeads": [before[index] for index in removed],
        "leadHandSections": lead_sections,
        "pulseHandSections": pulse_hand_sections(map_data, before, after, matches),
        "arrangementObservationAvailable": "arrangements" in experimental,
        "selectedArrangements": experimental.get("arrangements", []),
        "allowedStackRotationTimes": intentional_rotations,
        "protectedJumps": protected_jumps(map_data, before, after, matches, "Parity" in experimental["mode"]),
        "chordReadability": {"applicable": map_data.get("conversion") != "authored-bypass",
                             "halfOverlapViolations": chord_readability(after) if map_data.get("conversion") != "authored-bypass" else []},
        "addedHeads": added_heads,
        "addedHeadCheckpointCounts": dict(Counter(head["sourceCheckpoint"]["kind"] for head in added_heads)),
        "addedHeadRecovery": {"beforeMs": quantiles(head["recovery"].get("beforeMs") for head in added_heads),
                              "afterMs": quantiles(head["recovery"].get("afterMs") for head in added_heads),
                              "overlapHeads": sum(bool(head["recovery"].get("overlappingOtherHeadTimes")) for head in added_heads)},
        "newDoubleTimes": new_chord_times, "newDoubleGapMs": quantiles(chord_gaps),
        "newDoubleFractionOfTimingGroups": len(new_chord_times) / len(groups(after)) if after else 0,
        "familiesBefore": before_families, "familiesAfter": after_families, "familyDeltas": deltas,
        "sourceIdentityBefore": baseline.get("sourceIdentity"), "sourceIdentityAfter": experimental.get("sourceIdentity"),
        "validationBefore": baseline["validation"], "validationAfter": experimental["validation"],
    }


def saved_baseline_check(current_maps, saved, current_schema=4):
    old_maps = {map_data["sha256"]: map_data for map_data in saved["maps"]}
    matched = []
    for map_data in current_maps:
        old = old_maps.get(map_data["sha256"])
        if old is None:
            continue
        old_modes = {mode["mode"]: mode for mode in old["modes"]}
        mode_checks = []
        current_modes = {mode["mode"]: mode for mode in map_data["modes"]}
        for (name, _), (old_name, _) in zip(mode_pairs(current_schema), mode_pairs(saved.get("schemaVersion", 4))):
            if name not in current_modes or old_name not in old_modes:
                continue
            mode = current_modes[name]
            before = old_modes[old_name]
            equal = Counter(shape(note) for note in before["objects"]) == Counter(shape(note) for note in mode["objects"])
            mode_checks.append({"mode": name, "savedMode": old_name, "objectsUnchanged": equal,
                                "objectsExactlyUnchanged": before["objects"] == mode["objects"],
                                "starsDelta": mode["stars"] - before["stars"]})
        matched.append({"sha256": map_data["sha256"], "beatmapId": map_data.get("beatmapId"), "modes": mode_checks})
    current_hashes = {map_data["sha256"] for map_data in current_maps}
    return {"savedSchema": saved.get("schemaVersion"), "matchedMaps": len(matched),
            "currentOnlyMaps": len(current_hashes - old_maps.keys()),
            "savedOnlyMaps": len(old_maps.keys() - current_hashes),
            "changedModeOutputs": sum(not mode["objectsUnchanged"] for entry in matched for mode in entry["modes"]),
            "exactlyChangedModeOutputs": sum(not mode["objectsExactlyUnchanged"] for entry in matched for mode in entry["modes"]),
            "maps": matched}


def previous_experiment_check(current_maps, previous, current_schema=4):
    """Revision differences are descriptive; superseded experimental additions may disappear."""
    old_maps = {entry["sha256"]: entry for entry in previous["maps"]}
    rows = []
    for current in current_maps:
        old = old_maps.get(current["sha256"])
        if old is None:
            continue
        old_modes = {mode["mode"]: mode for mode in old["modes"]}
        checks = []
        current_modes = {mode["mode"]: mode for mode in current["modes"]}
        for (_, name), (_, old_name) in zip(mode_pairs(current_schema), mode_pairs(previous.get("schemaVersion", 4))):
            if name not in current_modes or old_name not in old_modes:
                continue
            mode = current_modes[name]
            before, after = old_modes[old_name]["objects"], mode["objects"]
            matches, removed, added = match_existing(before, after)
            counts = Counter(category for _, _, category in matches)
            checks.append({"mode": name, "previousMode": old_name, "before": len(before), "after": len(after),
                           "exactRetained": counts["exact"], "handOnlyChanges": counts["handOnly"],
                           "modifiedAtSameOnset": counts["modified"],
                           "removedHeads": [before[index] for index in removed],
                           "addedHeads": [after[index] for index in added],
                           "starsDelta": mode["stars"] - old_modes[old_name]["stars"]})
        rows.append({"sha256": current["sha256"], "beatmapId": current.get("beatmapId"),
                     "title": current["title"], "difficulty": current["difficulty"], "modes": checks})
    return {"matchedMaps": len(rows), "maps": rows}


def summarize(entries):
    result = {}
    names = sorted({pair["experimentalMode"] for entry in entries for pair in entry["comparisons"]})
    for name in names:
        pairs = [pair for entry in entries if entry["conversion"] != "authored-bypass"
                 for pair in entry["comparisons"] if pair["experimentalMode"] == name]
        counters = Counter()
        checkpoints = Counter()
        recovery_before, recovery_after, chord_gaps = [], [], []
        family_totals = {section: Counter() for section in ("counts", "duration", "dualSustainMotionPairMs", "chordAngles")}
        arrangement_totals = defaultdict(Counter)
        for pair in pairs:
            counters.update({key: value for key, value in pair["existingHeads"].items() if isinstance(value, (int, float))})
            checkpoints.update(pair["addedHeadCheckpointCounts"])
            for head in pair["addedHeads"]:
                recovery_before.append(head["recovery"].get("beforeMs"))
                recovery_after.append(head["recovery"].get("afterMs"))
            times = pair["newDoubleTimes"]
            chord_gaps.extend(second - first for first, second in zip(times, times[1:]))
            for section in family_totals:
                # Counter.update adds signed values; unary plus would discard decreases.
                family_totals[section].update(pair["familyDeltas"][section])
            for family in {entry["family"] for entry in pair["selectedArrangements"]}:
                arrangement_totals[family]["maps"] += 1
            for entry in pair["selectedArrangements"]:
                arrangement_totals[entry["family"]].update({"selections": 1, "changedHandCount": entry["changedHandCount"],
                                                          "addedHeadCount": entry["addedHeadCount"]})
        result[name] = {"maps": len(pairs), "mapsChanged": sum(pair["existingHeads"]["exactRetained"] != pair["existingHeads"]["before"]
                                                                    or pair["existingHeads"]["added"] > 0 for pair in pairs),
                        "existingHeads": dict(counters), "mapsWithLostOnsets": sum(bool(pair["existingHeads"]["lostOnsets"]) for pair in pairs),
                        "mapsWithSameHandAddedOverlaps": sum(pair["addedHeadRecovery"]["overlapHeads"] > 0 for pair in pairs),
                        "addedHeadCheckpointCounts": dict(checkpoints),
                        "addedHeadRecoveryBeforeMs": quantiles(recovery_before), "addedHeadRecoveryAfterMs": quantiles(recovery_after),
                        "leadHandSections": sum(len(pair["leadHandSections"]) for pair in pairs),
                        "mapsWithLeadHandSections": sum(bool(pair["leadHandSections"]) for pair in pairs),
                        "headsInReassignedLeadSections": sum(section["heads"] for pair in pairs for section in pair["leadHandSections"]),
                        "pulseHandSections": sum(len(pair["pulseHandSections"]) for pair in pairs),
                        "mapsWithPulseHandSections": sum(bool(pair["pulseHandSections"]) for pair in pairs),
                        "mapsWithArrangementObservationsAvailable": sum(pair["arrangementObservationAvailable"] for pair in pairs),
                        "selectedArrangementFamilies": {family: dict(counts) for family, counts in arrangement_totals.items()},
                        "allowedStackRotations": sum(len(pair["allowedStackRotationTimes"]) for pair in pairs),
                        "protectedJumpMapsMeasured": sum(pair["protectedJumps"]["available"] for pair in pairs),
                        "protectedJumpHeadsChecked": sum(pair["protectedJumps"].get("existingHeadsChecked", 0) for pair in pairs),
                        "protectedJumpIssues": sum(len(pair["protectedJumps"].get("issues", [])) for pair in pairs),
                        "chordHalfOverlapViolations": sum(len(pair["chordReadability"]["halfOverlapViolations"]) for pair in pairs),
                        "newDoubles": sum(len(pair["newDoubleTimes"]) for pair in pairs),
                        "mapsWithNewDoubles": sum(bool(pair["newDoubleTimes"]) for pair in pairs),
                        "newDoubleGapMs": quantiles(chord_gaps),
                        "medianPerMapNewDoubleFraction": statistics.median(pair["newDoubleFractionOfTimingGroups"] for pair in pairs) if pairs else None,
                        "familyDeltas": {section: dict(values) for section, values in family_totals.items()},
                        "starDelta": quantiles(pair["stars"]["delta"] for pair in pairs),
                        "validationIssues": sum(len(pair["validationAfter"]["issues"]) for pair in pairs)}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", nargs="?", type=Path, default=ROOT / "mapreference/counterpoint/iteration-01.json")
    parser.add_argument("--baseline", type=Path, default=ROOT / "mapreference/converter-jump-revision/after.json")
    parser.add_argument("--previous-experiment", type=Path, help="Earlier export to compare arrangement revisions, including Counterpoint before its promotion to Default.")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    actual = json.loads(args.input.read_text())
    if actual.get("schemaVersion", 0) < 4:
        parser.error("The current export must be schema 4 or newer for source checkpoint analysis.")
    entries = []
    for map_data in actual["maps"]:
        map_data["_rapidJumpMaximumIntervalMs"] = actual.get("rapidJumpMaximumIntervalMs", 260)
        map_data["_rapidJumpMinimumHeads"] = actual.get("rapidJumpMinimumHeads", 4)
        modes = {mode["mode"]: mode for mode in map_data["modes"]}
        comparisons = [compare_pair(map_data, modes[before], modes[after]) for before, after in mode_pairs(actual["schemaVersion"]) if before in modes and after in modes]
        if comparisons:
            entries.append({"sha256": map_data["sha256"], "beatmapId": map_data.get("beatmapId"), "artist": map_data["artist"],
                            "title": map_data["title"], "difficulty": map_data["difficulty"], "conversion": map_data["conversion"],
                            "overallDifficulty": map_data["overallDifficulty"], "comparisons": comparisons})
    if not entries:
        parser.error("No arrangement/base pairs found. Export with --include-legacy-base (or --include-counterpoint on historical builds).")
    saved = json.loads(args.baseline.read_text()) if args.baseline.exists() else None
    report = {
        "analysisVersion": 4, "input": str(args.input), "sourceSchema": actual["schemaVersion"],
        "converterAssemblySha256": actual.get("converterAssemblySha256"),
        "definitions": {
            "matching": "Exact objects first, then geometry-preserving hand changes, then remaining heads at equal 0.001ms-rounded onsets, prioritizing preserved kind, duration and signed paths before final hand and angle. This distinguishes simultaneous sliders that exchange hands and receive Parity rotations. Shape comparison includes kind, timing, angle, arcs (0.001 precision), exact duration weights and primary angle window. Remaining after objects are additions, not merely changed originals.",
            "checkpoint": "Added heads are matched against decoded source heads/repeats/tails/ticks within 0.01ms. Tick availability depends on export version: older schema-4 files omit tickTimes. New exports use osu's shared mirrored-span event generator and historical tick-density rules, with bounded candidate work. Grid-only classification infers quarter-beat timing inside an active source duration using its head's local tempo; this is weaker evidence than an explicit source checkpoint.",
            "recovery": "Time from the previous same-hand directional occupied endpoint and to the next same-hand directional head after this object's endpoint. Negative values or overlap listings need review; clicks have independent button input and are excluded.",
            "families": "Families describe geometry and overlap, not musical quality. Motion pair milliseconds compare signed segment arcs during overlap; authored same-hand overlapping durations may cause this pair measure to exceed dual occupied time. Occupied duration itself is unioned and is never counted twice.",
            "doubles": "A new double timestamp has both directional hands in the experiment and did not have both in its paired baseline. Chord gaps are within maps, never across map boundaries. Coincident/near/small/medium/wide use the schema-4 angle bins.",
            "leadHandSections": "At least four consecutive retained baseline flicks assigned to one final hand, with at least one changed assignment and positive gaps of at most 1.1 local beats. This measures phrase ownership without requiring additional notes; opposite-hand heads and sustained overlap describe the accompaniment. Counts are not a quality quota.",
            "pulseHandSections": "At least four retained directional heads, including short sliders, assigned to one hand at a regular pulse (successive gaps within 5% or 1ms, and at most 1.1 local beats), with an actual hand reassignment and opposite-hand input inside the phrase. The phrase ends one pulse interval after the final pulse head, excluding that next boundary, so offbeat answers after the last pulse are included. Reports pulse and accompanying timestamps; this can identify existing rhythm split between hands without introducing new onsets.",
            "protectedJumps": "Independently reconstructs protected rapid source jump runs from source position, circle diameter, timing and duration. Every baseline directional head in those runs must retain kind, hand, timing and signed path. Plain CP angles must also remain, except a bounded midpoint rotation into a qualifying exact stack. Parity angle histories may change. Heads changed to/from Encore clicks, and independent click-side changes, are reported separately because Encore selects them after directional arrangements; the plain CP pair checks the underlying directional protection. Older exports without geometry report this check unavailable.",
            "chordReadability": "Final simultaneous opposite-hand directional pairs whose angular windows intersect by at least half the narrower full PrimaryHitAngle should be exactly stacked. Equal widths W imply separation <= W/2, inclusive. Uses final exported gameplay widths. Authored maps bypass this rule. Allowed-stack rotation classification requires a final exact stack and limits each existing head's rotation to one quarter of the wider window; it does not reconstruct the unsnapped candidate.",
            "manualCadence": "Each added head records its nearest distinct final onset gap divided by the median baseline onset gap within four local beats either side. This describes new subdivisions; it is not a pass/fail musical rule, and simultaneous chords have no additional distinct onset.",
            "revisionComparison": "savedBaseline compares the historical base modes. previousExperiment separately compares arrangement revisions. Schema-4 Default/Encore/Parity modes map to schema-5 LegacyBase variants; schema-4 Counterpoint variants map to schema-5 Default/Encore/Parity modes. Removing an earlier arrangement addition is not evidence of losing a source note. Source preservation is assessed against each revision's paired historical base.",
            "selectedArrangements": "Optional converter observations report families actually selected in the gameplay conversion, their timing extent, changed-hand assignments and added heads. Captured before difficulty calculation can request conversion again. These establish implementation coverage; separate actual-object checks establish preservation, cadence, readability and the resulting playable relationships. Older exports lack this field.",
        },
        "inputErrors": actual.get("errors", []), "skipped": actual.get("skipped", []),
        "savedBaseline": saved_baseline_check(actual["maps"], saved, actual["schemaVersion"]) if saved else {"missing": str(args.baseline)},
        "summary": summarize(entries), "maps": entries,
    }
    if args.previous_experiment:
        report["previousExperiment"] = previous_experiment_check(actual["maps"], json.loads(args.previous_experiment.read_text()), actual["schemaVersion"])
    output = args.output or args.input.with_name(args.input.stem + "-analysis.json")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n")
    for name, summary in report["summary"].items():
        heads = summary["existingHeads"]
        print(f"{name}: {summary['mapsChanged']}/{summary['maps']} maps changed; +{heads.get('added', 0)} heads; "
              f"{heads.get('retainedWithHandOnlyChange', 0)} hand-only moves; {heads.get('modifiedAtSameOnset', 0)} originals reshaped; "
              f"{summary['newDoubles']} new doubles; {summary['mapsWithLostOnsets']} maps lost onsets; "
              f"{summary['mapsWithSameHandAddedOverlaps']} maps with added same-hand overlaps")
        print("  Added checkpoints:", summary["addedHeadCheckpointCounts"])
        print("  Family changes:", summary["familyDeltas"]["counts"])
    print(f"Saved baseline changed outputs: {report['savedBaseline'].get('changedModeOutputs', 'unavailable')}")
    print(f"Analysis: {output}")


if __name__ == "__main__":
    main()
