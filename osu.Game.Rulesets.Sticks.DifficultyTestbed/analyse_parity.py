#!/usr/bin/env python3
"""Measure parity's directional concentration without changing gameplay.

Input is --audit-parity output from Sticks.DifficultyTestbed. Before emitting
counterfactuals, the default replay must match every actual Parity and ParityDuet
head and endpoint. All arithmetic affecting production parity is rounded to
IEEE-754 binary32 as in C#. Counterfactuals change only the parity reset rule.
"""

import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics
import struct
import sys


ANGLE_TOLERANCE = 0.002
VARIANTS = {
    "resetDisabled": (math.inf, False),
    "reset1Beat": (1.0, False),
    "reset4Beats": (4.0, False),
    "reset2BeatsKeepTurn": (2.0, True),
}


def f32(value):
    return struct.unpack("f", struct.pack("f", value))[0]


def normalise(angle):
    angle = f32(math.fmod(f32(angle), 360.0))
    return f32(angle + 360.0) if angle < 0 else angle


def delta(from_angle, to_angle):
    difference = f32(normalise(to_angle) - normalise(from_angle))
    if difference > 180:
        difference = f32(difference - 360)
    if difference < -180:
        difference = f32(difference + 360)
    return difference


def endpoint(note, angle):
    arcs = note.get("segmentArcs", [])
    if not arcs:
        return normalise(angle)
    result = f32(angle)
    for arc in arcs[:-1]:
        result = f32(result + f32(arc))
    last_step = f32(f32(arcs[-1]) * f32(note.get("endProgress", 1.0)))
    return normalise(f32(result + last_step))


def replay(objects, reset_beats=2.0, preserve_turn_on_reset=False, apply=True):
    """Return (heads, events), preserving input order and all non-angle fields.

    With apply=False this annotates the supplied actual directions, without
    changing them. Events are aligned with heads. First notes, rest resets and
    overlapping gestures are disjoint from eligible parity transitions.
    """
    result = [dict(note) for note in objects]
    events = [None] * len(objects)
    histories = {}
    for index in sorted(range(len(objects)), key=lambda i: objects[i]["startTime"]):
        note = result[index]
        note["angle"] = normalise(note["angle"])
        side = note["side"]
        start = note["startTime"]
        beat_length = note.get("beatLength", 500.0)
        if not math.isfinite(beat_length) or beat_length <= 0:
            beat_length = 500.0
        history = histories.get(side)
        first = history is None
        reset = not first and start - history["endTime"] >= beat_length * reset_beats
        event = {"cohort": "firstOnStick" if first else "afterReset" if reset else "overlapping",
                 "eligible": False, "corrected": False, "tie": False,
                 "previousIndex": None if first else history["index"],
                 "correctionDegrees": 0.0}
        if first or reset:
            preferred = 1 if side == "Left" else -1
            if reset and preserve_turn_on_reset:
                preferred = history["preferredTurn"]
            history = {"startTime": -math.inf, "endTime": -math.inf,
                       "endAngle": 0.0, "preferredTurn": preferred,
                       "index": None, "sinceReset": 0}
            histories[side] = history
        elif start >= history["endTime"] and start > history["startTime"]:
            event["eligible"] = True
            event["cohort"] = "eligible"
            movement = delta(history["endAngle"], note["angle"])
            event["inputMovementDegrees"] = abs(movement)
            if apply and abs(movement) < 135.0:
                positive = normalise(f32(history["endAngle"] + 135.0))
                negative = normalise(f32(history["endAngle"] - 135.0))
                positive_cost = abs(delta(note["angle"], positive))
                negative_cost = abs(delta(note["angle"], negative))
                tie = abs(f32(positive_cost - negative_cost)) <= f32(0.001)
                turn = history["preferredTurn"] if tie else 1 if positive_cost < negative_cost else -1
                old_angle = note["angle"]
                note["angle"] = positive if turn > 0 else negative
                movement = f32(turn * 135.0)
                event["corrected"] = True
                event["tie"] = tie
                event["correctionDegrees"] = abs(delta(old_angle, note["angle"]))
            history["preferredTurn"] = (-1 if movement > 0 else 1 if movement < 0 else 0) \
                if abs(movement) < f32(179.999) else -history["preferredTurn"]
            event["movementDegrees"] = abs(delta(history["endAngle"], note["angle"]))

        history["sinceReset"] += 1
        event["positionSinceReset"] = history["sinceReset"]
        note["endAngle"] = endpoint(note, note["angle"]) if apply else normalise(note.get("endAngle", endpoint(note, note["angle"])))
        events[index] = event
        if note["endTime"] < history["endTime"]:
            event["historyRetained"] = True
            continue
        history.update(startTime=start, endTime=note["endTime"], endAngle=note["endAngle"], index=index)
    return result, events


def share(numerator, denominator):
    return numerator / denominator if denominator else None


def angular_metrics(angles):
    count = len(angles)
    histogram = [0] * 24
    for angle in angles:
        histogram[int(((angle + 7.5) % 360) // 15)] += 1
    if not count:
        return {"heads": 0, "histogram15Degrees": histogram, "entropyBits": None,
                "entropyNormalised": None, "topBinShare": None, "lattice45WithinPoint1Share": None,
                "lattice45Within3Share": None, "resultant8": None}
    probabilities = [value / count for value in histogram if value]
    entropy = -sum(probability * math.log2(probability) for probability in probabilities)
    lattice_distances = [abs(((angle + 22.5) % 45) - 22.5) for angle in angles]
    resultant = abs(sum(complex(math.cos(math.radians(angle * 8)), math.sin(math.radians(angle * 8))) for angle in angles)) / count
    return {"heads": count, "histogram15Degrees": histogram, "entropyBits": entropy,
            "entropyNormalised": entropy / math.log2(24), "topBinShare": max(histogram) / count,
            "lattice45WithinPoint1Share": sum(value <= 0.1 for value in lattice_distances) / count,
            "lattice45Within3Share": sum(value <= 3 for value in lattice_distances) / count,
            "resultant8": resultant}


def is_flick(note):
    return note["kind"].lower() in ("flick", "sticksflick", "stickshitobject")


def lattice_runs(objects, events):
    """Disjoint same-side flick runs sharing one modulo-45 phase (within 0.1°)."""
    active = {}
    completed = []
    for index in sorted(range(len(objects)), key=lambda i: objects[i]["startTime"]):
        note = objects[index]
        event = events[index]
        previous = active.get(note["side"])
        continues = (is_flick(note) and previous is not None and event["eligible"]
                     and event["previousIndex"] == previous["index"]
                     and abs(((note["angle"] - previous["anchor"] + 22.5) % 45) - 22.5) <= 0.1)
        if continues:
            previous["count"] += 1
            previous["index"] = index
            continue
        if previous is not None:
            completed.append(previous["count"])
        active[note["side"]] = {"anchor": note["angle"], "count": 1, "index": index} if is_flick(note) else None
    completed.extend(item["count"] for item in active.values() if item is not None)
    long_runs = [count for count in completed if count >= 4]
    return {"lattice45FlickRuns4Plus": len(long_runs), "lattice45FlickHeadsInRuns4Plus": sum(long_runs),
            "longestLattice45FlickRun": max(completed, default=0),
            "lattice45FlickHeadShareInRuns4Plus": share(sum(long_runs), sum(is_flick(note) for note in objects))}


def metrics(objects, events=None):
    """Metrics with identical two-beat eligibility across all counterfactuals.

    Supplied events describe actual replay corrections; angular/transition
    comparisons always use the production two-beat history window.
    """
    _, fixed_events = replay(objects, apply=False)
    movements = [event["movementDegrees"] for event in fixed_events if event["eligible"]]
    turn_histogram = [0] * 37
    for movement in movements:
        turn_histogram[min(36, int((movement + 2.5) // 5))] += 1
    returns = triples = 0
    for index, event in enumerate(fixed_events):
        if not event["eligible"] or not is_flick(objects[index]):
            continue
        middle = event["previousIndex"]
        if middle is None or not fixed_events[middle]["eligible"] or not is_flick(objects[middle]):
            continue
        first = fixed_events[middle]["previousIndex"]
        if first is None or not is_flick(objects[first]):
            continue
        triples += 1
        returns += abs(delta(objects[first]["angle"], objects[index]["angle"])) <= 3.0
    exact135 = sum(abs(value - 135.0) <= 0.01 for value in movements)
    exact180 = sum(abs(value - 180.0) <= 0.01 for value in movements)
    summary = angular_metrics([note["angle"] for note in objects])
    summary.update(eligibleTransitions=len(movements), exact135Count=exact135, exact180Count=exact180,
                   exact135Share=share(exact135, len(movements)), exact180Share=share(exact180, len(movements)),
                   transitionHistogram5Degrees=turn_histogram, eligibleFlickTriples=triples,
                   twoStepFlickReturns=returns, twoStepFlickReturnShare=share(returns, triples))
    summary.update(lattice_runs(objects, fixed_events))
    summary["flickHeads"] = sum(is_flick(note) for note in objects)
    selected_events = fixed_events if events is None else events
    cohorts = {name: angular_metrics([note["angle"] for note, event in zip(objects, selected_events) if event["cohort"] == name])
               for name in ("firstOnStick", "afterReset", "overlapping", "eligible")}
    cohorts["firstThreeSinceReset"] = angular_metrics([note["angle"] for note, event in zip(objects, selected_events)
                                                      if event["positionSinceReset"] <= 3])
    corrected = [event for event in selected_events if event["corrected"]]
    correction_eligible = sum(event["eligible"] for event in selected_events)
    summary.update(cohorts=cohorts, replayEligibleTransitions=correction_eligible,
                   correctedHeads=len(corrected), correctedShare=share(len(corrected), correction_eligible),
                   correctedTies=sum(event["tie"] for event in corrected),
                   meanCorrectionDegrees=statistics.fmean(event["correctionDegrees"] for event in corrected) if corrected else None)
    return summary


def structure(note):
    return (note["kind"], note["startTime"], note["endTime"], note["side"], tuple(note["segmentArcs"]))


def validate_objects(baseline, actual, label):
    if len(baseline) != len(actual) or any(structure(a) != structure(b) for a, b in zip(baseline, actual)):
        raise ValueError(f"{label}: parity changed object types/times/sides/arcs or ordering")
    for note in baseline + actual:
        for key in ("startTime", "endTime", "angle", "endAngle", "beatLength"):
            if not math.isfinite(note[key]):
                raise ValueError(f"{label}: nonfinite {key}")


def compare_replay(expected, actual, label):
    maximum_head = maximum_end = 0.0
    for index, (replayed, produced) in enumerate(zip(expected, actual)):
        head_error = abs(delta(replayed["angle"], produced["angle"]))
        end_error = abs(delta(replayed["endAngle"], produced["endAngle"]))
        maximum_head = max(maximum_head, head_error)
        maximum_end = max(maximum_end, end_error)
        if max(head_error, end_error) > ANGLE_TOLERANCE:
            raise ValueError(f"{label}: replay diverged at head {index}, time {produced['startTime']}: "
                             f"head error {head_error:.8f}°, endpoint error {end_error:.8f}°. "
                             "No counterfactual results were emitted.")
    return {"heads": len(actual), "maximumHeadErrorDegrees": maximum_head, "maximumEndErrorDegrees": maximum_end}


SCALAR_METRICS = ("entropyNormalised", "topBinShare", "lattice45WithinPoint1Share", "lattice45Within3Share",
                  "resultant8", "exact135Share", "exact180Share", "twoStepFlickReturnShare", "correctedShare",
                  "lattice45FlickHeadShareInRuns4Plus")


def medians(summaries):
    return {key: statistics.median(values) if (values := [summary[key] for summary in summaries if summary.get(key) is not None]) else None
            for key in SCALAR_METRICS}


def paired_differences(before, after):
    result = {}
    for key in SCALAR_METRICS:
        differences = [b[key] - a[key] for a, b in zip(before, after) if a.get(key) is not None and b.get(key) is not None]
        result[key] = {"maps": len(differences), "medianChange": statistics.median(differences) if differences else None,
                       "meanChange": statistics.fmean(differences) if differences else None,
                       "increasedMaps": sum(value > 1e-9 for value in differences),
                       "decreasedMaps": sum(value < -1e-9 for value in differences),
                       "unchangedMaps": sum(abs(value) <= 1e-9 for value in differences)}
    return result


def pooled_metrics(object_groups, event_groups):
    # Avoid joining two maps' histories at the boundary by pooling sufficient
    # statistics calculated separately. Absolute-angle vectors can pool directly.
    summaries = [metrics(objects, events) for objects, events in zip(object_groups, event_groups)]
    pooled = angular_metrics([note["angle"] for objects in object_groups for note in objects])
    for key in ("eligibleTransitions", "exact135Count", "exact180Count", "eligibleFlickTriples", "twoStepFlickReturns",
                "replayEligibleTransitions", "correctedHeads", "correctedTies", "flickHeads",
                "lattice45FlickRuns4Plus", "lattice45FlickHeadsInRuns4Plus"):
        pooled[key] = sum(summary[key] for summary in summaries)
    pooled["longestLattice45FlickRun"] = max(summary["longestLattice45FlickRun"] for summary in summaries)
    pooled["transitionHistogram5Degrees"] = [sum(summary["transitionHistogram5Degrees"][index] for summary in summaries) for index in range(37)]
    for name, numerator, denominator in (("exact135Share", "exact135Count", "eligibleTransitions"),
                                          ("exact180Share", "exact180Count", "eligibleTransitions"),
                                          ("twoStepFlickReturnShare", "twoStepFlickReturns", "eligibleFlickTriples"),
                                          ("lattice45FlickHeadShareInRuns4Plus", "lattice45FlickHeadsInRuns4Plus", "flickHeads"),
                                          ("correctedShare", "correctedHeads", "replayEligibleTransitions")):
        pooled[name] = share(pooled[numerator], pooled[denominator])
    pooled["cohorts"] = {}
    for cohort in ("firstOnStick", "afterReset", "overlapping", "eligible", "firstThreeSinceReset"):
        angles = []
        for objects, supplied_events in zip(object_groups, event_groups):
            events = replay(objects, apply=False)[1] if supplied_events is None else supplied_events
            angles.extend(note["angle"] for note, event in zip(objects, events)
                          if (event["positionSinceReset"] <= 3 if cohort == "firstThreeSinceReset" else event["cohort"] == cohort))
        pooled["cohorts"][cohort] = angular_metrics(angles)
    return {"objectWeighted": pooled, "equalMapMedian": medians(summaries)}


def analyse(report, input_hash):
    if report.get("errors"):
        raise ValueError("Input audit contains map conversion errors; fix them before analysis.")
    maps = report.get("maps", [])
    if not maps:
        raise ValueError("No maps in input audit.")
    comparisons = (("Standard", "Parity"), ("Duet", "ParityDuet"))
    result = {"schemaVersion": 1, "inputSha256": input_hash,
              "converterAssemblySha256": report["converterAssemblySha256"],
              "methods": {"angleHistogram": "24 bins centred at 0,15,...345 degrees; edges offset by 7.5 degrees",
                          "transitionHistogram": "37 bins centred at 0,5,...180 degrees; terminal bins half-width",
                          "resultant8": "abs(mean(exp(i * 8 * angle))); phase-invariant 45-degree lattice concentration",
                          "eligibility": "All transition and two-step metrics use fixed production two-beat rest eligibility, including counterfactuals",
                          "twoStepFlickReturns": "Three consecutive same-side eligible flicks with first/third head angles within 3 degrees",
                          "lattice45FlickRuns": "Disjoint consecutive eligible same-side flick runs sharing initial angle modulo 45 degrees within 0.1 degree; rest resets break runs",
                          "exactTransitionToleranceDegrees": 0.01, "replayToleranceDegrees": ANGLE_TOLERANCE,
                          "counterfactualScope": "Only angle/reset policy changes; baseline heads, timing, types, sides and arcs fixed",
                          "sampling": "Available local reference corpus; descriptive census, not a population estimate"},
              "maps": [], "comparisons": {}}
    grouped = {}
    for map_data in maps:
        mode_data = {mode["mode"]: mode["objects"] for mode in map_data["modes"]}
        entry = {key: value for key, value in map_data.items() if key != "modes"}
        entry["modes"] = {}
        entry["comparisons"] = {}
        for baseline_name, parity_name in comparisons:
            baseline = mode_data[baseline_name]
            actual = mode_data[parity_name]
            label = f"{map_data['source']} / {parity_name}"
            validate_objects(baseline, actual, label)
            replayed, events = replay(baseline)
            validation = compare_replay(replayed, actual, label)
            for mode_name, objects, selected_events in ((baseline_name, baseline, None), (parity_name, actual, events)):
                entry["modes"][mode_name] = metrics(objects, selected_events)
                groups = grouped.setdefault(mode_name, ([], []))
                groups[0].append(objects)
                groups[1].append(selected_events)
            comparison = {"replayValidation": validation, "counterfactuals": {}}
            for variant, (reset_beats, keep_turn) in VARIANTS.items():
                alternative, alternative_events = replay(baseline, reset_beats, keep_turn)
                validate_objects(baseline, alternative, f"{label} / {variant}")
                summary = metrics(alternative, alternative_events)
                changed = sum(abs(delta(left["angle"], right["angle"])) > ANGLE_TOLERANCE for left, right in zip(actual, alternative))
                summary["changedHeadsFromActual"] = changed
                summary["changedHeadShareFromActual"] = share(changed, len(actual))
                comparison["counterfactuals"][variant] = summary
                groups = grouped.setdefault(parity_name + ":" + variant, ([], []))
                groups[0].append(alternative)
                groups[1].append(alternative_events)
            entry["comparisons"][parity_name] = comparison
        result["maps"].append(entry)
    result["aggregateModes"] = {name: pooled_metrics(objects, events) for name, (objects, events) in grouped.items()}
    for baseline_name, parity_name in comparisons:
        before = [entry["modes"][baseline_name] for entry in result["maps"]]
        actual = [entry["modes"][parity_name] for entry in result["maps"]]
        validations = [entry["comparisons"][parity_name]["replayValidation"] for entry in result["maps"]]
        result["comparisons"][parity_name] = {
            "baseline": baseline_name, "maps": len(maps),
            "replayValidation": {"heads": sum(item["heads"] for item in validations),
                                 "maximumHeadErrorDegrees": max(item["maximumHeadErrorDegrees"] for item in validations),
                                 "maximumEndErrorDegrees": max(item["maximumEndErrorDegrees"] for item in validations)},
            "actualMinusBaseline": paired_differences(before, actual),
            "counterfactualMinusActual": {variant: paired_differences(actual, [entry["comparisons"][parity_name]["counterfactuals"][variant]
                                                                               for entry in result["maps"]]) for variant in VARIANTS}}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="JSON produced by --audit-parity")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    raw = args.input.read_bytes()
    try:
        result = analyse(json.loads(raw), hashlib.sha256(raw).hexdigest())
    except (ValueError, KeyError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(f"Validated parity replay across {len(result['maps'])} maps.")
    for parity_name, comparison in result["comparisons"].items():
        actual = result["aggregateModes"][parity_name]["objectWeighted"]
        baseline = result["aggregateModes"][comparison["baseline"]]["objectWeighted"]
        validation = comparison["replayValidation"]
        print(f"{parity_name}: {validation['heads']} heads; replay maximum error "
              f"{max(validation['maximumHeadErrorDegrees'], validation['maximumEndErrorDegrees']):.8f} degrees")
        for key in ("resultant8", "lattice45Within3Share", "exact135Share", "twoStepFlickReturnShare"):
            print(f"  {key}: {baseline[key]:.4f} -> {actual[key]:.4f}")
        for variant in VARIANTS:
            alternative = result["aggregateModes"][parity_name + ":" + variant]["objectWeighted"]
            print(f"  {variant}: R8 {alternative['resultant8']:.4f}, exact135 {alternative['exact135Share']:.4f}, "
                  f"two-step returns {alternative['twoStepFlickReturnShare']:.4f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
