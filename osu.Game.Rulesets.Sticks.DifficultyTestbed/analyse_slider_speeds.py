#!/usr/bin/env python3
"""Compare observed Surge output with uncapped speed proposals; never modify maps.

Input: schema 7 --compare-converters --include-surge reports.
One observation per original slider. Repeat spans are already included in its speed.
"""

import argparse
from decimal import Decimal, ROUND_HALF_UP
import json
import math
from pathlib import Path
import statistics


def near(a, b):
    return math.isclose(a, b, rel_tol=1e-6, abs_tol=1e-6)


def quantile(values, fraction):
    position = (len(values) - 1) * fraction
    lower = math.floor(position)
    upper = math.ceil(position)
    return values[lower] + (values[upper] - values[lower]) * (position - lower)


def distribution(values):
    values = sorted(values)
    if not values:
        return None
    median = statistics.median(values)
    return dict(count=len(values), minimum=values[0], median=median, maximum=values[-1],
                p10=quantile(values, .1), p90=quantile(values, .9),
                atMedian=sum(near(value, median) for value in values),
                atLeastTwiceMedian=sum(value >= median * 2 or near(value, median * 2) for value in values))


def triplet(summary):
    # Remove insignificant decoder floating-point noise before rounding for display.
    return ' / '.join(str(Decimal(str(round(summary[key], 6))).quantize(Decimal('.1'), rounding=ROUND_HALF_UP))
                      for key in ['minimum', 'median', 'maximum']) if summary else '—'


def relative_request(speed, median):
    # Keep the existing 120-degree reference and faster-than-median boost curve.
    # Extend that curve below the median, bounded by the proposed 80-degree floor.
    return max(80.0, 120.0 + 120.0 * (1.0 - median / speed))


def rapid_repeat(note):
    # Broad research exclusion: also removes short rapid reversals, not only long buzzes.
    span = (note['endTime'] - note['startTime']) / (note['repeatCount'] + 1)
    return note['repeatCount'] > 0 and span <= note['beatLength'] / 4 + .01


def analyse(beatmap, exclude_buzz=False):
    all_source = [note for note in beatmap['sourceTimeline'] if note['sliderPixelsPerSecond'] is not None]
    excluded = [note for note in all_source if exclude_buzz and rapid_repeat(note)]
    source = [note for note in all_source if not exclude_buzz or not rapid_repeat(note)]
    speeds = [note['sliderPixelsPerSecond'] for note in source]
    summary = distribution(speeds)
    if not summary:
        return None
    modes = {mode['mode']: mode for mode in beatmap['modes']}
    def retained_in_sample(note):
        def matches(original):
            return near(note['startTime'], original['startTime']) and near(note['sourcePixelsPerSecond'], original['sliderPixelsPerSecond'])
        kept = any(matches(original) for original in source)
        removed = any(matches(original) for original in excluded)
        assert not (kept and removed), 'Ambiguous simultaneous source-slider trace; export source identity before filtering.'
        return kept

    baseline = [note for note in modes['Default']['sourceSliderSpeeds'] if retained_in_sample(note)]
    # Independently exported original and retained-source observations must agree.
    for note in baseline:
        assert any(near(note['startTime'], original['startTime'])
                   and near(note['sourcePixelsPerSecond'], original['sliderPixelsPerSecond']) for original in source)
    groups = []
    for speed in sorted(speeds):
        if groups and near(groups[-1]['speed'], speed):
            groups[-1]['count'] += 1
        else:
            groups.append(dict(speed=speed, count=1))
    proposals = {}
    for name in ['source', 'relative']:
        def request(speed):
            return (speed * 45 / (100 * beatmap['sourceSliderMultiplier']) if name == 'source'
                    else relative_request(speed, summary['median']))
        # The complete-source request is distinct from a projection onto retained paths.
        # The latter scales each moving path to the requested maximum segment speed.
        moving = [note for note in baseline if note['convertedDegreesPerSecond'] > 0]
        raw = [request(note['sourcePixelsPerSecond']) for note in moving]
        ceilings = [160 if note['reverses'] else 240 for note in moving]
        above = [value > ceiling and not near(value, ceiling) for value, ceiling in zip(raw, ceilings)]
        proposals[name] = dict(allSourceRequests=distribution([request(speed) for speed in speeds]),
                               retainedMovingRequests=distribution(raw),
                               retainedMovingWithCeilings=distribution([min(value, ceiling) for value, ceiling in zip(raw, ceilings)]),
                               exceedCeilingCount=sum(above),
                               below80Count=sum(value < 80 and not near(value, 80) for value in raw),
                               above120Count=sum(value > 120 and not near(value, 120) for value in raw))
    observed = {name: dict(stars=mode['stars'],
                           retainedSourceSpeeds=distribution([n['convertedDegreesPerSecond'] for n in mode['sourceSliderSpeeds'] if retained_in_sample(n)]),
                           changedSliders=sum(retained_in_sample(n) for n in mode['sliderBursts']))
                for name, mode in modes.items() if name in ['Default', 'SurgeSourceSpeed', 'SurgeRelativeEmphasis']}
    return dict(beatmapId=beatmap['beatmapId'], beatmapSetId=beatmap['beatmapSetId'],
                title=beatmap['title'], artist=beatmap['artist'], difficulty=beatmap['difficulty'],
                source=beatmap['source'], sha256=beatmap['sha256'], sliderMultiplier=beatmap['sourceSliderMultiplier'],
                unfilteredSourceCount=len(all_source), excludedSourceCount=len(excluded), excludedSourceSliders=excluded,
                sourceSpeeds=summary, sourceSpeedGroups=groups,
                extremes=[dict(time=n['startTime'], duration=n['endTime'] - n['startTime'],
                               repeats=n['repeatCount'], speed=n['sliderPixelsPerSecond']) for n in source
                          if near(n['sliderPixelsPerSecond'], summary['minimum']) or near(n['sliderPixelsPerSecond'], summary['maximum'])],
                retainedSourceCount=len(baseline), observed=observed, proposals=proposals)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reports', nargs='+', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--exclude-buzz', action='store_true',
                        help='Broad filter: exclude repeated sliders with spans at most 1/4 local beat, including short rapid reversals. Recompute proposal median on the remaining sliders.')
    args = parser.parse_args()
    maps = []
    seen = set()
    for path in args.reports:
        report = json.loads(path.read_text())
        assert report['schemaVersion'] >= 7, f'{path}: run an updated converter comparison first'
        assert not report['errors'], f'{path}: input errors'
        for beatmap in report['maps']:
            assert all(not mode['validation']['issues'] for mode in beatmap['modes'])
            if beatmap['conversion'] == 'authored-bypass' or beatmap['sha256'] in seen:
                continue
            seen.add(beatmap['sha256'])
            result = analyse(beatmap, args.exclude_buzz)
            if result:
                maps.append(result)
    maps.sort(key=lambda m: (m['artist'], m['title'], m['difficulty']))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(dict(
        definitions={
            'source': 'Decoded path distance / one span duration, px/s; each source slider counted once, without duration weighting.',
            'sourceProposal': 'source_px_per_s * 45 / (100 * map_SliderMultiplier), degrees/s.',
            'relativeProposal': 'max(80, 120 + 120 * (1 - map_median_source_px_per_s / source_px_per_s)), degrees/s. Keeps the 120 base and existing faster-slider boost curve; extends it below the median with an 80 floor.',
            'ceilingProjection': 'Only retained moving primary sliders, using the actual converted reversal flag: 160 degrees/s for reversals, otherwise 240. Direct target replacement, without current accent/entry/duration gates or 120 floor. This is a numerical projection, not implemented gameplay or proposed stars.',
            'observed': 'Current implemented default/Source speed/Relative emphasis, through the gameplay mod pipeline. Includes unchanged retained source sliders.',
            'equality': 'Relative tolerance 1e-6, absolute tolerance 1e-6, before display rounding.'},
        filter='Repeated sliders with span duration <= one quarter of the local beat (+0.01ms rounding tolerance); includes short rapid reversals. Source/proposal medians recomputed after exclusion. Current-build observations are only filtered, not reconverted.' if args.exclude_buzz else 'None',
        inputReports=[str(p) for p in args.reports], maps=maps), indent=2) + '\n')
    lines = ['# Slider-speed study', '',
             f'{len(maps)} unique source maps. All triplets are minimum / median / maximum. Source units are px/s; converted units are degrees/s.', '',
             ('Buzz/rapid-repeat exclusion: remove source sliders with at least one reversal and span duration at most one quarter of the local beat (+0.01ms rounding tolerance). This deliberately includes short rapid reversals. Ordinary slower reversals and fast single-pass sliders remain. Recalculate source/proposal medians after exclusion. Current-build observations are filtered without reconverting, so their stars and normalization still describe the full current map.' if args.exclude_buzz else 'No source-slider exclusions.'), '',
             'Proposals below are requested speeds for every source slider, before per-path ceilings. Source = speed × 45 / (100 × map slider multiplier). Relative = max(80, 120 + 120 × (1 − map median / speed)): 120 base, 80 floor, existing faster-slider boost curve. The slower side extends that curve below the median. These proposals are not the currently implemented mod.', '',
             '| Map | Sliders | Excluded | At median | At least 2× median | Source | Proposed Source | Proposed Relative |',
             '| --- | ---: | ---: | ---: | ---: | --- | --- | --- |']
    for m in maps:
        s = m['sourceSpeeds']
        label = f"{m['artist']} — {m['title']} [{m['difficulty']}]".replace('|', '\\|')
        url = f"https://osu.ppy.sh/beatmapsets/{m['beatmapSetId']}#osu/{m['beatmapId']}"
        lines.append(f"| [{label}]({url}) | {s['count']} | {m['excludedSourceCount']} | {s['atMedian']} | {s['atLeastTwiceMedian']} | {triplet(s)} | {triplet(m['proposals']['source']['allSourceRequests'])} | {triplet(m['proposals']['relative']['allSourceRequests'])} |")
    lines += ['', '## Ceiling projection and current build', '',
              'Ceilings use the retained converted paths: 160°/s for reversals, 240°/s otherwise. Counts show requests exceeding those ceilings. Current stars are measurements of the existing build; no stars have been calculated for the proposals.', '',
              '| Map ID / difficulty | Retained | Source exceeding ceiling | Relative exceeding ceiling | Proposed Source with ceilings | Proposed Relative with ceilings | Current Default ★ | Current SG Source | Current SG Relative |',
              '| --- | ---: | ---: | ---: | --- | --- | ---: | --- | --- |']
    for m in maps:
        ps, pr, obs = m['proposals']['source'], m['proposals']['relative'], m['observed']
        lines.append(f"| {m['beatmapId']} / {m['difficulty']} | {m['retainedSourceCount']} | {ps['exceedCeilingCount']} | {pr['exceedCeilingCount']} | {triplet(ps['retainedMovingWithCeilings'])} | {triplet(pr['retainedMovingWithCeilings'])} | {obs['Default']['stars']:.2f} | {triplet(obs.get('SurgeSourceSpeed', {}).get('retainedSourceSpeeds'))} | {triplet(obs.get('SurgeRelativeEmphasis', {}).get('retainedSourceSpeeds'))} |")
    args.output.with_suffix('.md').write_text('\n'.join(lines) + '\n')
    print(f'Analysed {len(maps)} maps and {sum(m["sourceSpeeds"]["count"] for m in maps)} source sliders: {args.output}')


if __name__ == '__main__':
    main()
