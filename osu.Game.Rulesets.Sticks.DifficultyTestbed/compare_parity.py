#!/usr/bin/env python3
"""Compare two actual gameplay exports without replaying a parity algorithm."""

import argparse
import hashlib
import json
import math
from pathlib import Path
import statistics
import sys

from analyse_parity import delta, endpoint, metrics, pooled_metrics, replay, validate_objects


MODES = ("Standard", "Parity", "Duet", "ParityDuet")
PARITY_BASELINES = {"Parity": "Standard", "ParityDuet": "Duet"}
COMPARISON_METRICS = (
    "exact135Share", "twoStepFlickReturnShare", "lattice45FlickHeadShareInRuns4Plus",
    "resultant8", "entropyNormalised", "meanMovementDegrees", "medianMovementDegrees",
    "movementP10Degrees", "movementP90Degrees", "under90Share",
)
# No parity algorithm is replayed, so correction/tie counts are not measured.
REPLAY_ONLY_METRICS = (
    "replayEligibleTransitions", "correctedHeads", "correctedShare", "correctedTies", "meanCorrectionDegrees",
)


def quantile(values, fraction):
    if not values:
        return None
    values = sorted(values)
    position = (len(values) - 1) * fraction
    lower = int(position)
    return values[lower] + (values[min(lower + 1, len(values) - 1)] - values[lower]) * (position - lower)


def movement_summary(movements):
    return {
        "meanMovementDegrees": statistics.fmean(movements) if movements else None,
        "medianMovementDegrees": statistics.median(movements) if movements else None,
        "movementP10Degrees": quantile(movements, 0.1),
        "movementP90Degrees": quantile(movements, 0.9),
        "under90Share": sum(value < 90 for value in movements) / len(movements) if movements else None,
    }


def measured(objects):
    _, events = replay(objects, apply=False)
    movements = [event["movementDegrees"] for event in events if event["eligible"]]
    summary = metrics(objects)
    for key in REPLAY_ONLY_METRICS:
        summary.pop(key, None)
    summary.update(movement_summary(movements))
    return summary, events, movements


def differences(before, after):
    return {key: after[key] - before[key] if before.get(key) is not None and after.get(key) is not None else None
            for key in COMPARISON_METRICS}


def equal_map_medians(summaries):
    return {key: statistics.median(values) if (values := [item[key] for item in summaries if item.get(key) is not None]) else None
            for key in COMPARISON_METRICS}


def validate_pair(before, after, label):
    validate_objects(before, after, label)
    for index, (left, right) in enumerate(zip(before, after)):
        # Preserve every non-angle field, including local timing and end progress.
        if {key: value for key, value in left.items() if key not in ("angle", "endAngle")} != \
                {key: value for key, value in right.items() if key not in ("angle", "endAngle")}:
            raise ValueError(f"{label}: changed non-angle field at head {index}")
        for note in (left, right):
            if note["endTime"] < note["startTime"] or note["beatLength"] <= 0:
                raise ValueError(f"{label}: invalid duration or beat length at head {index}")
            if any(not math.isfinite(value) for value in [note["endProgress"], *note["segmentArcs"]]):
                raise ValueError(f"{label}: nonfinite slider geometry at head {index}")
            if abs(delta(endpoint(note, note["angle"]), note["endAngle"])) > 0.002:
                raise ValueError(f"{label}: endpoint does not preserve slider motion at head {index}")


def signed_summary(values):
    return {"transitions": len(values),
            "signedMeanDegrees": statistics.fmean(values) if values else None,
            "signedStdDegrees": statistics.pstdev(values) if values else None,
            "absoluteMeanDegrees": statistics.fmean(abs(value) for value in values) if values else None}


def section_summaries(source, before, after, events):
    """Local windows share exactly the same transition indices across variants."""
    sections = []
    current = None
    for index in sorted(range(len(source)), key=lambda i: source[i]["startTime"]):
        note = source[index]
        if current is None or note["startTime"] >= current["endTime"] or \
                not math.isclose(note["beatLength"], current["beatLength"], abs_tol=1e-6):
            if current is not None:
                current["endTime"] = min(current["endTime"], note["startTime"])
            current = {"startTime": note["startTime"], "endTime": note["startTime"] + 8 * note["beatLength"],
                       "beatLength": note["beatLength"], "heads": 0,
                       "turns": {side: {version: [] for version in ("source", "before", "after")} for side in ("Left", "Right")}}
            sections.append(current)
        current["heads"] += 1
        event = events[index]
        if not event["eligible"]:
            continue
        previous = event["previousIndex"]
        for version, objects in (("source", source), ("before", before), ("after", after)):
            current["turns"][note["side"]][version].append(delta(objects[previous]["endAngle"], objects[index]["angle"]))
    for section in sections:
        section["sides"] = {side: {version: signed_summary(values) for version, values in versions.items()}
                            for side, versions in section.pop("turns").items()}
    return sections


def index_report(report, label):
    if report.get("errors") or not report.get("maps"):
        raise ValueError(f"{label}: empty export or conversion errors")
    indexed = {}
    for item in report["maps"]:
        if item["sha256"] in indexed:
            raise ValueError(f"{label}: duplicate source SHA-256 {item['sha256']}")
        modes = {mode["mode"]: mode["objects"] for mode in item["modes"]}
        if len(item["modes"]) != len(MODES) or set(modes) != set(MODES):
            raise ValueError(f"{label}: expected all four modes for {item['sha256']}")
        indexed[item["sha256"]] = (item, modes)
    return indexed


def compare(before_report, after_report):
    before_maps = index_report(before_report, "before")
    after_maps = index_report(after_report, "after")
    if before_maps.keys() != after_maps.keys():
        raise ValueError("Exports must contain the same source SHA-256 set; no maps are silently dropped")
    result = {
        "schemaVersion": 1,
        "converterAssemblySha256": {"before": before_report["converterAssemblySha256"], "after": after_report["converterAssemblySha256"]},
        "methods": {
            "comparison": "Actual gameplay exports matched by source SHA-256; no parity algorithm replay or counterfactuals",
            "validation": "Standard/Duet exactly unchanged; all modes preserve every non-angle object field and valid slider endpoints",
            "eligibility": "Fixed two-local-beat rest window; first notes and overlapping same-stick gestures excluded; slider endpoints included",
            "twoStepFlickReturns": "Three consecutive eligible same-stick flicks whose first/third directions differ by at most 3 degrees",
            "lattice45FlickRuns": "Eligible same-stick flick runs of at least four, within 0.1 degree of their initial angle modulo 45 degrees",
            "exact135ToleranceDegrees": 0.01,
            "movementQuantiles": "Absolute same-stick endpoint-to-head turns; linear interpolation between ordered samples",
            "sections": "Eight-beat windows anchored to their first head; tempo changes start a new window; empty windows omitted",
            "sectionTurns": "Same eligible indices in source/before/after. Signed arithmetic mean/std on [-180,180]; branch cut affects these summaries",
            "sampling": "Available local corpus; descriptive comparison, not a population estimate",
        },
        "maps": [], "aggregateModes": {},
    }
    grouped = {mode: {version: {"objects": [], "movements": [], "summaries": []} for version in ("before", "after")} for mode in MODES}
    for sha, (metadata, before_modes) in before_maps.items():
        _, after_modes = after_maps[sha]
        entry = {key: value for key, value in metadata.items() if key != "modes"}
        entry["modes"] = {}
        for mode in MODES:
            before, after = before_modes[mode], after_modes[mode]
            validate_pair(before, after, f"{sha}/{mode}")
            if mode in ("Standard", "Duet") and before != after:
                raise ValueError(f"{sha}/{mode}: baseline gameplay objects changed")
            comparison = {}
            for version, objects in (("before", before), ("after", after)):
                summary, events, movements = measured(objects)
                comparison[version] = summary
                group = grouped[mode][version]
                group["objects"].append(objects)
                group["movements"].extend(movements)
                group["summaries"].append(summary)
            comparison["afterMinusBefore"] = differences(comparison["before"], comparison["after"])
            comparison["changedHeads"] = sum(abs(delta(a["angle"], b["angle"])) > 0.002 for a, b in zip(before, after))
            if mode in PARITY_BASELINES:
                source = before_modes[PARITY_BASELINES[mode]]
                validate_pair(source, before, f"{sha}/{mode}/source")
                comparison["sections"] = section_summaries(source, before, after, events)
            entry["modes"][mode] = comparison
        result["maps"].append(entry)
    for mode, versions in grouped.items():
        aggregate = {}
        for version, group in versions.items():
            pooled = pooled_metrics(group["objects"], [None] * len(group["objects"]))["objectWeighted"]
            for key in REPLAY_ONLY_METRICS:
                pooled.pop(key, None)
            pooled.update(movement_summary(group["movements"]))
            aggregate[version] = {"objectWeighted": pooled, "equalMapMedian": equal_map_medians(group["summaries"])}
        aggregate["afterMinusBefore"] = differences(aggregate["before"]["objectWeighted"], aggregate["after"]["objectWeighted"])
        aggregate["pairedMapChanges"] = {}
        for key in COMPARISON_METRICS:
            changes = [entry["modes"][mode]["afterMinusBefore"][key] for entry in result["maps"]
                       if entry["modes"][mode]["afterMinusBefore"][key] is not None]
            aggregate["pairedMapChanges"][key] = {
                "maps": len(changes), "medianChange": statistics.median(changes) if changes else None,
                "increasedMaps": sum(value > 1e-9 for value in changes), "decreasedMaps": sum(value < -1e-9 for value in changes),
                "unchangedMaps": sum(abs(value) <= 1e-9 for value in changes),
            }
        result["aggregateModes"][mode] = aggregate
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        before_raw, after_raw = args.before.read_bytes(), args.after.read_bytes()
        result = compare(json.loads(before_raw), json.loads(after_raw))
        result["inputSha256"] = {"before": hashlib.sha256(before_raw).hexdigest(), "after": hashlib.sha256(after_raw).hexdigest()}
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print(f"Compared actual exports for {len(result['maps'])} maps; Standard/Duet unchanged and parity structure preserved.")
    for mode in PARITY_BASELINES:
        before, after = (result["aggregateModes"][mode][version]["objectWeighted"] for version in ("before", "after"))
        print(f"{mode}: {before['heads']} heads, {before['eligibleTransitions']} eligible transitions")
        for key in ("exact135Share", "twoStepFlickReturnShare", "lattice45FlickHeadShareInRuns4Plus", "under90Share"):
            format_share = lambda value: "n/a" if value is None else f"{value:.2%}"
            print(f"  {key}: {format_share(before[key])} -> {format_share(after[key])}")
        for key in ("meanMovementDegrees", "medianMovementDegrees", "movementP10Degrees", "movementP90Degrees"):
            format_angle = lambda value: "n/a" if value is None else f"{value:.2f}"
            print(f"  {key}: {format_angle(before[key])} -> {format_angle(after[key])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
