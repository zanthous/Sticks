#!/usr/bin/env node

// Dependency-free companion validation for Mapper/mapper.js. This runs the mapper's real pure
// import/export functions without starting a browser. The C# SticksTimingImportExportTest then
// covers how the same rows are interpreted by lazer's stock LegacyBeatmapDecoder.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const here = dirname(fileURLToPath(import.meta.url));
const mapperSource = readFileSync(resolve(here, '../Mapper/mapper.js'), 'utf8');

// mapper.js installs its test boundary on window. Avoid firing DOMContentLoaded: all functions
// under test are pure and should not need a browser DOM or an audio device.
globalThis.window = globalThis;
globalThis.document = { addEventListener() {} };
vm.runInThisContext(mapperSource, { filename: 'Mapper/mapper.js' });

const {
    parseOsuFile,
    parseTimingPoints,
    normaliseTimingPoints,
    activeTimingPoint,
    beatLengthWithTiming,
    snapTimeWithTiming,
    createOsuFile,
} = globalThis.SticksMapperExport;

for (const [name, helper] of Object.entries({
    parseOsuFile,
    parseTimingPoints,
    normaliseTimingPoints,
    activeTimingPoint,
    beatLengthWithTiming,
    snapTimeWithTiming,
    createOsuFile,
})) {
    assert.equal(typeof helper, 'function', `Mapper validation hook is missing ${name}()`);
}

const validRows = [
    '-250.5,600.25,3,1,2,70,1,8',
    '-250.5,-50,4,3,1,35,0,1',
    '1000.125,500.5,4,2,0,80,1,0',
    '1000.125,-80,4,2,2,45,0,9',
    '2000,NaN,4,2,0,80,0,0',
    '4500.75,333.333333333333,7,3,4,100,1,1',
];

const sourceOsu = `\uFEFFosu file format v14\r
\r
[General]\r
AudioFilename: source audio.ogg\r
Mode: 0\r
\r
[Editor]\r
BeatDivisor: 8\r
\r
[TimingPoints]\r
${validRows[0]}\r
${validRows[1]}\r
not,a,valid,timing,row\r
${validRows[2]}\r
${validRows[3]}\r
${validRows[4]}\r
${validRows[5]}\r
\r
[HitObjects]\r
256,192,5000,1,0,0:0:0:100:\r
`;

const imported = parseOsuFile(sourceOsu, 'source timing.osu');
assert.equal(imported.sourceName, 'source timing.osu');
assert.equal(imported.beatDivisor, 8);
assert.equal(imported.points.length, validRows.length);
assert.equal(imported.ignoredTimingLines, 1);
assert.deepEqual(imported.points.map(point => point.raw), validRows);
assert.deepEqual(imported.points.filter(point => point.uninherited).map(point => point.time), [-250.5, 1000.125, 4500.75]);

// The project persists parsed fields as well as the original row. JSON must not alter precision,
// order, same-time red/green pairs, or flags when the project is reopened and exported.
const timingV2 = {
    bpm: 60000 / 600.25,
    offset: -250.5,
    snap: 8,
    sourceName: imported.sourceName,
    points: imported.points,
};
const reloadedTiming = JSON.parse(JSON.stringify(timingV2));
assert.deepEqual(normaliseTimingPoints(reloadedTiming.points).map(point => point.raw), validRows);
assert.deepEqual(parseTimingPoints(reloadedTiming.points).map(point => point.raw), validRows);

const timingProject = {
    format: 'sticks-mapper-project',
    version: 1,
    metadata: {
        title: 'Timing validation',
        artist: 'Sticks',
        creator: 'Zanthous',
        difficulty: 'Round trip',
        audioName: 'source audio.ogg',
    },
    timing: reloadedTiming,
    objects: [{ id: 'validation', type: 'flick', startTime: 5000, side: 'left', angle: 0 }],
};

const exportedTimingProject = createOsuFile(timingProject, 'source audio.ogg');
assert.deepEqual(sectionRows(exportedTimingProject, 'TimingPoints'), validRows);
assert.match(exportedTimingProject, /BeatDivisor: 8\r?$/m);

// The active beat section must ignore inherited points, switch at the exact red-line boundary,
// and extrapolate the first timing point backwards like osu!'s timing grid.
assert.equal(activeTimingPoint(reloadedTiming.points, -1000).time, -250.5);
assert.equal(activeTimingPoint(reloadedTiming.points, 1000.124).beatLength, 600.25);
assert.equal(activeTimingPoint(reloadedTiming.points, 1000.125).beatLength, 500.5);
assert.equal(activeTimingPoint(reloadedTiming.points, 4500.75).beatLength, 333.333333333333);
assert.equal(beatLengthWithTiming(1000.124, reloadedTiming), 600.25);
assert.equal(beatLengthWithTiming(1000.125, reloadedTiming), 500.5);
assertNearlyEqual(snapTimeWithTiming(50, reloadedTiming), 49.625);
assertNearlyEqual(snapTimeWithTiming(1126, reloadedTiming), 1125.25);

// Manual timing uses the same project schema and synthesises one uninherited timing row.
const manualProject = {
    format: 'sticks-mapper-project',
    version: 1,
    metadata: timingProject.metadata,
    timing: { bpm: 150, offset: 123.5, snap: 4 },
    objects: timingProject.objects,
};
assert.deepEqual(sectionRows(createOsuFile(manualProject, 'source audio.ogg'), 'TimingPoints'), [
    '123.5,400,4,2,0,100,1,0',
]);

process.stdout.write('Mapper timing import/export validation passed.\n');

function sectionRows(osuText, sectionName) {
    const lines = osuText.replace(/^\uFEFF/, '').split(/\r?\n/);
    const start = lines.findIndex(line => line.trim() === `[${sectionName}]`);
    assert.notEqual(start, -1, `Missing [${sectionName}] section`);

    const rows = [];
    for (let i = start + 1; i < lines.length; i++) {
        const line = lines[i].trim();
        if (line.startsWith('['))
            break;
        if (line && !line.startsWith('//'))
            rows.push(line);
    }
    return rows;
}

function assertNearlyEqual(actual, expected, epsilon = 1e-9) {
    assert.ok(Math.abs(actual - expected) <= epsilon, `Expected ${actual} to be within ${epsilon} of ${expected}`);
}
