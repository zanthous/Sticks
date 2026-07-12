(() => {
    'use strict';

    const PROJECT_FORMAT = 'sticks-mapper-project';
    const PROJECT_VERSION = 1;
    const PLAYFIELD_SIZE = 640;
    const CENTRE = PLAYFIELD_SIZE / 2;
    const OUTER_RADIUS = 246;
    const INNER_RADIUS = 214;
    const NOTE_HALF_SPAN = 10;
    const OBJECT_VISIBILITY_BEFORE = 1800;
    const OBJECT_VISIBILITY_AFTER = 650;
    const TIMELINE_WINDOW = 8000;
    const EXPORT_CENTRE_X = 256;
    const EXPORT_CENTRE_Y = 192;
    const EXPORT_OUTER_RADIUS = 160;
    const EXPORT_INNER_RADIUS = 105;
    const PORTABLE_RULESET_TAG = 'sticks-v1';
    const SUPPORTED_AUDIO_EXTENSIONS = new Set(['mp3', 'ogg', 'wav']);

    const sideColours = {
        left: '#339eff',
        right: '#ff404d',
    };

    const state = {
        objects: [],
        selectedId: null,
        tool: 'select',
        audioUrl: null,
        audioFile: null,
        audioName: '',
        pointer: null,
        pointerInside: false,
        sliderGesture: null,
        draggingObject: null,
        seekDragging: false,
        renderRequested: false,
        timingPoints: [],
        timingControlPoints: [],
        timingSourceName: '',
        timingIgnoredLines: 0,
    };

    const el = {};
    const ids = [
        'audio-file', 'project-file', 'timing-file', 'clear-timing-import', 'timing-status',
        'save-project', 'export-map', 'meta-title', 'meta-artist',
        'meta-creator', 'meta-difficulty', 'timing-bpm', 'timing-offset', 'timing-snap',
        'default-duration', 'default-repeats', 'tool-buttons', 'tool-help', 'play-pause',
        'previous-snap', 'next-snap', 'time-display', 'seek', 'snap-display', 'audio',
        'audio-name', 'placement-readout', 'place-chord', 'delete-selected', 'playfield',
        'object-layer', 'preview-layer', 'beat-spokes', 'timeline-ruler', 'timeline-window', 'selection-label',
        'inspector', 'edit-type', 'edit-side', 'edit-start', 'edit-angle', 'edit-duration',
        'edit-arc', 'edit-repeats', 'snap-selected', 'duplicate-selected', 'object-count',
        'object-list', 'status',
    ];

    const toolHelp = {
        select: 'Select and drag an object around the rings to edit its angle and stick.',
        flick: 'Click either ring to place a flick at the current snapped time.',
        hold: 'Click either ring to place a hold using the default beat length.',
        slider: 'Press on a ring, drag around it to define the arc, then release.',
    };

    document.addEventListener('DOMContentLoaded', initialise);

    function initialise() {
        for (const id of ids)
            el[toCamel(id)] = document.getElementById(id);

        drawBeatSpokes();
        bindControls();
        updateTool('select');
        updateTimingUi();
        updateInspector();
        renderAll();
        requestAnimationFrame(frame);
    }

    function bindControls() {
        el.audioFile.addEventListener('change', loadAudioFile);
        el.projectFile.addEventListener('change', loadProjectFile);
        el.timingFile.addEventListener('change', loadTimingFile);
        el.clearTimingImport.addEventListener('click', clearTimingImport);
        el.saveProject.addEventListener('click', saveProject);
        el.exportMap.addEventListener('click', runExport);

        el.playPause.addEventListener('click', togglePlayback);
        el.previousSnap.addEventListener('click', () => stepTime(-1));
        el.nextSnap.addEventListener('click', () => stepTime(1));
        el.audio.addEventListener('loadedmetadata', audioMetadataLoaded);
        el.audio.addEventListener('play', updatePlaybackButton);
        el.audio.addEventListener('pause', updatePlaybackButton);
        el.audio.addEventListener('ended', updatePlaybackButton);
        el.seek.addEventListener('pointerdown', () => state.seekDragging = true);
        el.seek.addEventListener('pointerup', () => state.seekDragging = false);
        el.seek.addEventListener('input', () => seekTo(Number(el.seek.value)));
        el.timelineRuler.addEventListener('click', event => {
            if (event.target.closest('.timeline-tick'))
                return;
            const bounds = el.timelineRuler.getBoundingClientRect();
            const progress = clamp((event.clientX - bounds.left) / bounds.width, 0, 1);
            seekTo(snapTime(currentTime() + (progress - 0.5) * TIMELINE_WINDOW));
        });

        el.toolButtons.addEventListener('click', event => {
            const button = event.target.closest('[data-tool]');
            if (button)
                updateTool(button.dataset.tool);
        });

        for (const input of [
            el.metaTitle, el.metaArtist, el.metaCreator, el.metaDifficulty,
            el.timingBpm, el.timingOffset, el.timingSnap,
        ]) {
            input.addEventListener('change', () => {
                renderAll();
                updateTimingUi();
                setStatus('Project settings updated.');
            });
        }

        el.playfield.addEventListener('pointermove', playfieldPointerMove);
        el.playfield.addEventListener('pointerdown', playfieldPointerDown);
        el.playfield.addEventListener('pointerup', playfieldPointerUp);
        el.playfield.addEventListener('pointercancel', cancelPointerGesture);
        el.playfield.addEventListener('pointerleave', () => {
            state.pointerInside = false;
            if (!state.sliderGesture && !state.draggingObject)
                renderPreview();
        });
        el.playfield.addEventListener('wheel', event => {
            event.preventDefault();
            stepTime(event.deltaY > 0 ? 1 : -1);
        }, { passive: false });

        el.objectLayer.addEventListener('pointerdown', objectPointerDown);
        el.objectList.addEventListener('click', event => {
            const row = event.target.closest('[data-object-id]');
            if (row)
                selectObject(row.dataset.objectId, true);
        });

        el.deleteSelected.addEventListener('click', deleteSelected);
        el.snapSelected.addEventListener('click', snapSelectedTime);
        el.duplicateSelected.addEventListener('click', duplicateSelected);
        el.placeChord.addEventListener('click', placeChord);
        el.inspector.addEventListener('submit', event => {
            event.preventDefault();
            applyInspectorChanges();
        });

        for (const input of [el.editType, el.editSide, el.editStart, el.editAngle, el.editDuration, el.editArc, el.editRepeats])
            input.addEventListener('change', applyInspectorChanges);

        window.addEventListener('keydown', handleKeyboard);
        window.addEventListener('beforeunload', () => {
            if (state.audioUrl)
                URL.revokeObjectURL(state.audioUrl);
        });
    }

    function toCamel(id) {
        return id.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
    }

    function timing() {
        return {
            bpm: clamp(finiteNumber(el.timingBpm.value, 120), 1, 1000),
            offset: finiteNumber(el.timingOffset.value, 0),
            snap: clamp(Math.round(finiteNumber(el.timingSnap.value, 4)), 1, 64),
        };
    }

    function currentTimingConfiguration() {
        return {
            ...timing(),
            points: state.timingPoints,
            controlPoints: state.timingControlPoints,
        };
    }

    function beatLength(time = currentTime()) {
        return beatLengthWithTiming(time, currentTimingConfiguration());
    }

    function snapLength(time = currentTime()) {
        return beatLength(time) / timing().snap;
    }

    function snapTime(time) {
        return snapTimeWithTiming(time, currentTimingConfiguration());
    }

    /**
     * Parse only the reusable parts of a standard .osu file. Timing rows keep their original
     * text so sample/effect fields (including inherited rows) survive a Sticks round trip.
     */
    function parseOsuFile(text, sourceName = '') {
        const timingLines = [];
        let section = '';
        let beatDivisor = null;
        let ignoredTimingLines = 0;

        for (const originalLine of String(text ?? '').replace(/^\uFEFF/, '').split(/\r?\n/)) {
            const line = originalLine.trim();
            const sectionMatch = line.match(/^\[([^\]]+)\]$/);
            if (sectionMatch) {
                section = sectionMatch[1].trim().toLowerCase();
                continue;
            }

            if (!line || line.startsWith('//'))
                continue;

            if (section === 'timingpoints') {
                if (parseTimingPointLine(line))
                    timingLines.push(line);
                else
                    ignoredTimingLines++;
                continue;
            }

            if (section === 'editor') {
                const separator = line.indexOf(':');
                if (separator < 0)
                    continue;
                const key = line.slice(0, separator).trim().toLowerCase();
                const value = Number(line.slice(separator + 1).trim());
                if (key === 'beatdivisor' && Number.isInteger(value) && value >= 1 && value <= 64)
                    beatDivisor = value;
            }
        }

        const points = parseTimingPoints(timingLines);
        return {
            points,
            timingPoints: points,
            beatDivisor,
            ignoredTimingLines,
            sourceName: String(sourceName || ''),
        };
    }

    function parseTimingPoints(input) {
        if (typeof input === 'string') {
            if (/^\s*\[[^\]]+\]/m.test(input))
                return parseOsuFile(input).points;
            input = input.split(/\r?\n/);
        }

        if (!Array.isArray(input))
            return [];

        const points = [];
        for (const item of input) {
            let point = null;
            if (typeof item === 'string') {
                point = parseTimingPointLine(item);
            } else if (item && typeof item === 'object') {
                if (typeof item.raw === 'string')
                    point = parseTimingPointLine(item.raw);
                if (!point)
                    point = parseTimingPointObject(item);
            }
            if (point)
                points.push(point);
        }
        return points;
    }

    function normaliseTimingPoints(input) {
        return parseTimingPoints(input);
    }

    function parseTimingPointLine(line) {
        const raw = String(line ?? '').trim();
        if (!raw || raw.startsWith('//'))
            return null;

        const fields = raw.split(',');
        if (fields.length < 2 || fields.length > 8)
            return null;

        const requiredNumber = index => {
            const value = fields[index]?.trim();
            return value ? Number(value) : Number.NaN;
        };
        const optionalNumber = (index, fallback) => {
            const value = fields[index]?.trim();
            return value == null || value === '' ? fallback : Number(value);
        };

        const time = requiredNumber(0);
        const rawBeatLength = fields[1]?.trim() ?? '';
        const beatLength = Number(rawBeatLength);
        const meter = optionalNumber(2, 4);
        const sampleSet = optionalNumber(3, 0);
        const sampleIndex = optionalNumber(4, 0);
        const volume = optionalNumber(5, 100);
        const uninheritedValue = optionalNumber(6, 1);
        const effects = optionalNumber(7, 0);
        const uninherited = uninheritedValue !== 0;
        const inheritedNaN = !uninherited && /^[-+]?nan$/i.test(rawBeatLength);

        if (!Number.isFinite(time)
            || (!Number.isFinite(beatLength) && !inheritedNaN)
            || (Number.isFinite(beatLength) && beatLength === 0)
            || ![meter, sampleSet, sampleIndex, volume, uninheritedValue, effects].every(Number.isFinite))
            return null;

        return {
            raw,
            time,
            beatLength,
            meter,
            sampleSet,
            sampleIndex,
            volume,
            uninherited,
            effects,
        };
    }

    function parseTimingPointObject(input) {
        const time = finiteNumber(input.time, Number.NaN);
        const beatLengthValue = Number(input.beatLength);
        const uninherited = input.uninherited == null ? beatLengthValue > 0 : Boolean(input.uninherited);
        if (!Number.isFinite(time) || !Number.isFinite(beatLengthValue) || beatLengthValue === 0)
            return null;

        const fields = [
            formatOsuNumber(time),
            formatOsuNumber(beatLengthValue),
            String(Math.round(finiteNumber(input.meter, 4))),
            String(Math.round(finiteNumber(input.sampleSet, 0))),
            String(Math.round(finiteNumber(input.sampleIndex, 0))),
            String(Math.round(finiteNumber(input.volume, 100))),
            uninherited ? '1' : '0',
            String(Math.round(finiteNumber(input.effects, 0))),
        ];
        return parseTimingPointLine(fields.join(','));
    }

    function serialiseTimingPoints(points) {
        return normaliseTimingPoints(points).map(point => ({
            raw: point.raw,
            time: point.time,
            beatLength: Number.isFinite(point.beatLength) ? point.beatLength : null,
            meter: point.meter,
            sampleSet: point.sampleSet,
            sampleIndex: point.sampleIndex,
            volume: point.volume,
            uninherited: point.uninherited,
            effects: point.effects,
        }));
    }

    function controlTimingPoints(points) {
        const sorted = normaliseTimingPoints(points)
            .map((point, sourceIndex) => ({ ...point, sourceIndex }))
            .filter(point => point.uninherited && Number.isFinite(point.beatLength) && point.beatLength > 0)
            .sort((a, b) => a.time - b.time || a.sourceIndex - b.sourceIndex);
        const controls = [];
        for (const point of sorted) {
            if (controls.length && controls[controls.length - 1].time === point.time)
                controls[controls.length - 1] = point;
            else
                controls.push(point);
        }
        return controls;
    }

    function timingControlPoints(configuration) {
        if (Array.isArray(configuration?.controlPoints))
            return configuration.controlPoints;
        return controlTimingPoints(configuration?.points ?? configuration?.timingPoints ?? []);
    }

    function activeTimingPoint(points, time) {
        const controls = controlTimingPoints(points);
        if (!controls.length)
            return null;
        return controls[activeTimingIndex(controls, finiteNumber(time, 0))];
    }

    function activeTimingIndex(controls, time) {
        let low = 0;
        let high = controls.length - 1;
        let result = 0;
        while (low <= high) {
            const middle = (low + high) >> 1;
            if (controls[middle].time <= time) {
                result = middle;
                low = middle + 1;
            } else {
                high = middle - 1;
            }
        }
        return result;
    }

    function beatLengthWithTiming(time, configuration) {
        const controls = timingControlPoints(configuration);
        if (controls.length)
            return controls[activeTimingIndex(controls, finiteNumber(time, 0))].beatLength;
        return 60000 / clamp(finiteNumber(configuration?.bpm, 120), 1, 1000);
    }

    function timingGridCandidates(time, configuration) {
        const target = finiteNumber(time, 0);
        const snap = clamp(Math.round(finiteNumber(configuration?.snap, 4)), 1, 64);
        let controls = timingControlPoints(configuration);
        if (!controls.length) {
            controls = [{
                time: finiteNumber(configuration?.offset, 0),
                beatLength: 60000 / clamp(finiteNumber(configuration?.bpm, 120), 1, 1000),
                meter: 4,
            }];
        }

        const activeIndex = activeTimingIndex(controls, target);
        const candidates = new Set();
        const firstIndex = Math.max(0, activeIndex - 1);
        const lastIndex = Math.min(controls.length - 1, activeIndex + 1);

        for (let index = firstIndex; index <= lastIndex; index++) {
            const point = controls[index];
            const step = point.beatLength / snap;
            const lower = index === 0 ? Number.NEGATIVE_INFINITY : point.time;
            const upper = controls[index + 1]?.time ?? Number.POSITIVE_INFINITY;
            const nearestIndex = Math.floor((target - point.time) / step);
            for (let gridIndex = nearestIndex - 2; gridIndex <= nearestIndex + 3; gridIndex++) {
                const candidate = point.time + gridIndex * step;
                if (candidate >= 0 && candidate >= lower - 0.0001 && candidate < upper - 0.0001)
                    candidates.add(round(candidate, 9));
            }
            if (point.time >= 0)
                candidates.add(round(point.time, 9));
            if (Number.isFinite(upper) && upper >= 0)
                candidates.add(round(upper, 9));
        }

        return [...candidates].sort((a, b) => a - b);
    }

    function snapTimeWithTiming(time, configuration) {
        const target = Math.max(0, finiteNumber(time, 0));
        const candidates = timingGridCandidates(target, configuration);
        if (!candidates.length)
            return target;
        return candidates.reduce((best, candidate) => {
            const distance = Math.abs(candidate - target);
            const bestDistance = Math.abs(best - target);
            return distance < bestDistance - 0.000001 || (Math.abs(distance - bestDistance) <= 0.000001 && candidate < best)
                ? candidate
                : best;
        });
    }

    function adjacentSnapTimeWithTiming(time, configuration, direction) {
        const target = Math.max(0, finiteNumber(time, 0));
        const candidates = timingGridCandidates(target, configuration);
        const epsilon = 0.0001;
        if (direction >= 0)
            return candidates.find(candidate => candidate > target + epsilon) ?? target;

        for (let index = candidates.length - 1; index >= 0; index--) {
            if (candidates[index] < target - epsilon)
                return candidates[index];
        }
        return 0;
    }

    function currentTime() {
        return el.audio.src && Number.isFinite(el.audio.currentTime)
            ? el.audio.currentTime * 1000
            : Number(el.seek.value) || 0;
    }

    function seekTo(timeMs) {
        const maximum = getDurationMs();
        const target = clamp(timeMs, 0, maximum || Math.max(0, timeMs));
        if (Number.isFinite(el.audio.duration))
            el.audio.currentTime = target / 1000;
        el.seek.value = String(target);
        requestRender();
    }

    function stepTime(direction) {
        const current = currentTime();
        seekTo(adjacentSnapTimeWithTiming(current, currentTimingConfiguration(), direction));
    }

    async function togglePlayback() {
        if (!el.audio.src) {
            setStatus('Load an audio file before playing.', true);
            return;
        }

        if (el.audio.paused) {
            try {
                await el.audio.play();
            } catch (error) {
                setStatus(`Could not play audio: ${error.message}`, true);
            }
        } else {
            el.audio.pause();
        }
    }

    function updatePlaybackButton() {
        el.playPause.textContent = el.audio.paused ? 'Play' : 'Pause';
        el.playPause.setAttribute('aria-label', el.audio.paused ? 'Play' : 'Pause');
    }

    function loadAudioFile(event) {
        const file = event.target.files?.[0];
        if (!file)
            return;

        if (state.audioUrl)
            URL.revokeObjectURL(state.audioUrl);

        state.audioUrl = URL.createObjectURL(file);
        state.audioFile = file;
        state.audioName = file.name;
        el.audio.src = state.audioUrl;
        el.audioName.textContent = file.name;
        setStatus(`Loaded audio: ${file.name}`);
        event.target.value = '';
    }

    async function loadTimingFile(event) {
        const file = event.target.files?.[0];
        if (!file)
            return;

        try {
            const parsed = parseOsuFile(await file.text(), file.name);
            const controls = controlTimingPoints(parsed.points);
            if (!controls.length)
                throw new Error('No valid uninherited BPM timing points were found.');

            setImportedTimingPoints(parsed.points, file.name, parsed.ignoredTimingLines);
            const first = controls[0];
            el.timingBpm.value = String(round(60000 / first.beatLength, 6));
            el.timingOffset.value = String(round(first.time, 6));
            if (parsed.beatDivisor != null)
                setSnapSelector(parsed.beatDivisor);

            updateTimingUi();
            renderAll();
            setStatus(`Imported timing from ${file.name}: ${controls.length} BPM section${controls.length === 1 ? '' : 's'}, ${parsed.points.length - controls.length} inherited/auxiliary point${parsed.points.length - controls.length === 1 ? '' : 's'}.`);
        } catch (error) {
            setStatus(`Could not import timing: ${error.message}`, true);
        } finally {
            event.target.value = '';
        }
    }

    function setImportedTimingPoints(points, sourceName = '', ignoredLines = 0) {
        state.timingPoints = normaliseTimingPoints(points);
        state.timingControlPoints = controlTimingPoints(state.timingPoints);
        state.timingSourceName = String(sourceName || 'Imported .osu');
        state.timingIgnoredLines = Math.max(0, Math.round(finiteNumber(ignoredLines, 0)));
    }

    function clearTimingImport() {
        if (!state.timingControlPoints.length)
            return;

        const first = state.timingControlPoints[0];
        el.timingBpm.value = String(round(60000 / first.beatLength, 6));
        el.timingOffset.value = String(round(first.time, 6));
        state.timingPoints = [];
        state.timingControlPoints = [];
        state.timingSourceName = '';
        state.timingIgnoredLines = 0;
        updateTimingUi();
        renderAll();
        setStatus('Switched to manual timing using the imported map’s first BPM section.');
    }

    function updateTimingUi(time = currentTime()) {
        const imported = state.timingControlPoints.length > 0;
        el.timingBpm.disabled = imported;
        el.timingOffset.disabled = imported;
        el.clearTimingImport.disabled = !imported;
        el.timingStatus.classList.toggle('imported', imported);

        if (!imported) {
            const config = timing();
            el.timingStatus.textContent = `Manual timing · ${round(config.bpm, 3)} BPM · offset ${round(config.offset, 3)} ms`;
            return;
        }

        const active = state.timingControlPoints[activeTimingIndex(state.timingControlPoints, finiteNumber(time, 0))];
        const inheritedCount = state.timingPoints.filter(point => !point.uninherited).length;
        const ignored = state.timingIgnoredLines ? ` · ${state.timingIgnoredLines} invalid row${state.timingIgnoredLines === 1 ? '' : 's'} ignored` : '';
        el.timingStatus.textContent = `${state.timingSourceName} · ${state.timingControlPoints.length} BPM section${state.timingControlPoints.length === 1 ? '' : 's'} + ${inheritedCount} inherited · active ${round(60000 / active.beatLength, 3)} BPM at ${formatTime(active.time)}${ignored}`;
    }

    function setSnapSelector(value) {
        const snap = String(clamp(Math.round(finiteNumber(value, 4)), 1, 64));
        for (const option of [...el.timingSnap.options]) {
            if (option.dataset.imported === 'true' && option.value !== snap)
                option.remove();
        }
        if (![...el.timingSnap.options].some(option => option.value === snap)) {
            const option = document.createElement('option');
            option.value = snap;
            option.textContent = `1/${snap} (imported)`;
            option.dataset.imported = 'true';
            el.timingSnap.append(option);
        }
        el.timingSnap.value = snap;
    }

    function audioMetadataLoaded() {
        const duration = getDurationMs();
        el.seek.max = String(Math.max(1, duration));
        el.seek.value = '0';
        renderAll();
    }

    function getDurationMs() {
        if (Number.isFinite(el.audio.duration))
            return el.audio.duration * 1000;

        const finalObject = [...state.objects].sort((a, b) => objectEndTime(b) - objectEndTime(a))[0];
        return finalObject ? objectEndTime(finalObject) + 2000 : 1;
    }

    function frame() {
        if (!el.audio.paused || state.renderRequested) {
            state.renderRequested = false;
            renderDynamic();
        }
        requestAnimationFrame(frame);
    }

    function requestRender() {
        state.renderRequested = true;
    }

    function renderAll() {
        sortObjects();
        if (!el.audio.src)
            el.seek.max = String(Math.max(1, getDurationMs()));
        renderObjectList();
        updateInspector();
        renderDynamic();
    }

    function renderDynamic() {
        const now = currentTime();
        const snapped = snapTime(now);

        if (!state.seekDragging)
            el.seek.value = String(now);

        el.timeDisplay.value = formatTime(now);
        el.timeDisplay.textContent = formatTime(now);
        el.snapDisplay.value = `snap ${formatTime(snapped)}`;
        el.snapDisplay.textContent = `snap ${formatTime(snapped)}`;
        updateTimingUi(now);

        renderPlayfieldObjects(now);
        renderPreview();
        renderTimeline(now);
    }

    function drawBeatSpokes() {
        const fragment = document.createDocumentFragment();
        for (let angle = 0; angle < 360; angle += 45) {
            const start = pointAt(angle, INNER_RADIUS - 16);
            const end = pointAt(angle, OUTER_RADIUS + 16);
            fragment.append(svg('line', {
                class: 'spoke', x1: start.x, y1: start.y, x2: end.x, y2: end.y,
            }));
        }
        el.beatSpokes.append(fragment);
    }

    function playfieldPointerMove(event) {
        state.pointer = pointerInfo(event);
        state.pointerInside = true;

        if (state.sliderGesture) {
            updateSliderGesture(state.pointer);
        } else if (state.draggingObject) {
            updateDraggedObject(state.pointer);
        }

        updatePlacementReadout();
        renderPreview();
    }

    function playfieldPointerDown(event) {
        if (event.button !== 0)
            return;

        if (event.target.closest('.mapped-object'))
            return;

        const pointer = pointerInfo(event);
        state.pointer = pointer;
        state.pointerInside = true;

        if (!pointer.inLane) {
            if (state.tool === 'select')
                selectObject(null);
            return;
        }

        if (state.tool === 'select') {
            selectObject(null);
            return;
        }

        el.playfield.setPointerCapture(event.pointerId);

        if (state.tool === 'slider') {
            state.sliderGesture = {
                pointerId: event.pointerId,
                startAngle: pointer.angle,
                lastPointerAngle: pointer.angle,
                arcAngle: 0,
                side: pointer.side,
                startTime: snapTime(currentTime()),
            };
            renderPreview();
            return;
        }

        const object = newObject(state.tool, pointer.side, pointer.angle);
        addObject(object, true);
    }

    function playfieldPointerUp(event) {
        if (state.sliderGesture?.pointerId === event.pointerId) {
            const gesture = state.sliderGesture;
            state.sliderGesture = null;

            let arcAngle = gesture.arcAngle;
            if (Math.abs(arcAngle) < 5)
                arcAngle = 90;

            const object = newObject('slider', gesture.side, gesture.startAngle);
            object.startTime = gesture.startTime;
            object.duration = round(
                Math.max(1, finiteNumber(el.defaultDuration.value, 1) * beatLength(gesture.startTime)),
                3,
            );
            object.arcAngle = round(arcAngle, 3);
            object.repeatCount = clamp(Math.round(finiteNumber(el.defaultRepeats.value, 0)), 0, 16);
            addObject(object, true);
        }

        if (state.draggingObject?.pointerId === event.pointerId) {
            state.draggingObject = null;
            setStatus('Object position updated.');
            renderAll();
        }

        if (el.playfield.hasPointerCapture(event.pointerId))
            el.playfield.releasePointerCapture(event.pointerId);
    }

    function cancelPointerGesture(event) {
        if (state.sliderGesture?.pointerId === event.pointerId)
            state.sliderGesture = null;
        if (state.draggingObject?.pointerId === event.pointerId)
            state.draggingObject = null;
        renderPreview();
    }

    function objectPointerDown(event) {
        if (event.button !== 0 || state.tool !== 'select')
            return;

        const group = event.target.closest('[data-object-id]');
        if (!group)
            return;

        event.stopPropagation();
        const object = findObject(group.dataset.objectId);
        if (!object)
            return;

        selectObject(object.id);
        state.draggingObject = { pointerId: event.pointerId, objectId: object.id };
        el.playfield.setPointerCapture(event.pointerId);
    }

    function updateDraggedObject(pointer) {
        const object = findObject(state.draggingObject.objectId);
        if (!object || !pointer.inLane)
            return;

        object.side = pointer.side;
        object.angle = round(pointer.angle, 3);
        updateInspector();
        renderObjectList();
        requestRender();
    }

    function updateSliderGesture(pointer) {
        const gesture = state.sliderGesture;
        if (!gesture || !pointer.inLane)
            return;

        let delta = normaliseSignedAngle(pointer.angle - gesture.lastPointerAngle);
        if (Math.abs(delta) > 120)
            delta = 0;

        gesture.arcAngle += delta;
        gesture.lastPointerAngle = pointer.angle;
    }

    function pointerInfo(event) {
        const bounds = el.playfield.getBoundingClientRect();
        const x = (event.clientX - bounds.left) / bounds.width * PLAYFIELD_SIZE;
        const y = (event.clientY - bounds.top) / bounds.height * PLAYFIELD_SIZE;
        const dx = x - CENTRE;
        const dy = y - CENTRE;
        const radius = Math.hypot(dx, dy);
        const angle = normaliseAngle(Math.atan2(dy, dx) * 180 / Math.PI);
        const side = Math.abs(radius - OUTER_RADIUS) <= Math.abs(radius - INNER_RADIUS) ? 'left' : 'right';
        const inLane = radius >= INNER_RADIUS - 30 && radius <= OUTER_RADIUS + 30;
        return { x, y, radius, angle, side, inLane };
    }

    function updatePlacementReadout() {
        if (!state.pointerInside || !state.pointer?.inLane) {
            el.placementReadout.textContent = 'Move over a ring to preview placement.';
            return;
        }

        const sideName = state.pointer.side === 'left' ? 'Left / outer' : 'Right / inner';
        const arc = state.sliderGesture ? ` · arc ${formatSigned(state.sliderGesture.arcAngle)}°` : '';
        el.placementReadout.textContent = `${sideName} · ${state.pointer.angle.toFixed(1)}° · ${formatTime(snapTime(currentTime()))}${arc}`;
    }

    function newObject(type, side, angle) {
        const startTime = round(snapTime(currentTime()), 3);
        const duration = Math.max(1, finiteNumber(el.defaultDuration.value, 1) * beatLength(startTime));
        const object = {
            id: createId(),
            type,
            startTime,
            side,
            angle: round(normaliseAngle(angle), 3),
        };

        if (type === 'hold')
            object.duration = round(duration, 3);

        if (type === 'slider') {
            object.duration = round(duration, 3);
            object.arcAngle = 90;
            object.repeatCount = clamp(Math.round(finiteNumber(el.defaultRepeats.value, 0)), 0, 16);
        }

        return object;
    }

    function addObject(object, select) {
        state.objects.push(normaliseObject(object));
        if (select)
            state.selectedId = object.id;
        setStatus(`Placed ${object.type} at ${formatTime(object.startTime)}.`);
        renderAll();
    }

    function placeChord() {
        const angle = state.pointer?.inLane
            ? state.pointer.angle
            : findObject(state.selectedId)?.angle ?? 0;
        const startTime = snapTime(currentTime());
        const left = newObject('flick', 'left', angle);
        const right = newObject('flick', 'right', normaliseAngle(angle + 180));
        left.startTime = right.startTime = round(startTime, 3);
        state.objects.push(left, right);
        state.selectedId = left.id;
        setStatus('Placed a 180° two-stick chord. Adjust either angle in the inspector if needed.');
        renderAll();
    }

    function renderPlayfieldObjects(now) {
        el.objectLayer.replaceChildren();
        const fragment = document.createDocumentFragment();

        for (const object of state.objects) {
            const isSelected = object.id === state.selectedId;
            const delta = object.startTime - now;
            if (!isSelected && (delta > OBJECT_VISIBILITY_BEFORE || objectEndTime(object) < now - OBJECT_VISIBILITY_AFTER))
                continue;

            const opacity = isSelected ? 1 : clamp(1 - Math.max(0, -delta) / OBJECT_VISIBILITY_AFTER, 0.18, 0.9);
            const group = svg('g', {
                class: `mapped-object ${object.type} ${isSelected ? 'selected' : ''}`,
                'data-object-id': object.id,
                opacity,
            });
            drawObject(group, object, false);
            fragment.append(group);
        }

        el.objectLayer.append(fragment);
    }

    function drawObject(group, object, preview) {
        const colour = sideColours[object.side];
        const radius = radiusFor(object.side);

        if (object.type === 'slider') {
            const path = arcPath(radius, object.angle, object.arcAngle);
            group.append(svg('path', {
                d: path,
                class: `${preview ? 'preview-visual' : 'slider-path visual'}`,
                stroke: colour,
            }));
            if (!preview) {
                group.append(svg('path', { d: path, class: 'hit-target' }));
                const head = pointAt(object.angle, radius);
                const tail = pointAt(object.angle + object.arcAngle, radius);
                group.append(svg('circle', { class: 'slider-head head-visual', cx: head.x, cy: head.y, r: 9, fill: colour }));
                group.append(svg('circle', { class: 'slider-tail', cx: tail.x, cy: tail.y, r: 7, fill: colour }));
                if (object.repeatCount > 0) {
                    group.append(svg('circle', { class: 'repeat-badge', cx: tail.x, cy: tail.y, r: 11 }));
                    const text = svg('text', { class: 'repeat-text', x: tail.x, y: tail.y });
                    text.textContent = `×${object.repeatCount + 1}`;
                    group.append(text);
                }
            }
            return;
        }

        const notePath = arcPath(radius, object.angle - NOTE_HALF_SPAN, NOTE_HALF_SPAN * 2);
        group.append(svg('path', {
            d: notePath,
            class: `${preview ? 'preview-visual' : 'note-arc visual'}`,
            stroke: colour,
        }));

        if (preview)
            return;

        group.append(svg('path', { d: notePath, class: 'hit-target' }));
        const centre = pointAt(object.angle, radius);
        const tangent = tangentSegment(object.angle, radius, 8);
        group.append(svg('line', {
            class: 'note-tick', x1: tangent.x1, y1: tangent.y1, x2: tangent.x2, y2: tangent.y2,
        }));

        if (object.type === 'hold') {
            const visualLength = clamp(object.duration / beatLength(object.startTime) * 26, 20, 90);
            const direction = object.side === 'left' ? 1 : -1;
            const end = pointAt(object.angle, radius + direction * visualLength);
            group.insertBefore(svg('line', {
                class: 'hold-line', x1: centre.x, y1: centre.y, x2: end.x, y2: end.y, stroke: colour,
            }), group.firstChild);
            group.append(svg('circle', { class: 'hold-end', cx: end.x, cy: end.y, r: 4 }));
        }
    }

    function renderPreview() {
        el.previewLayer.replaceChildren();
        if (state.tool === 'select' || !state.pointerInside || !state.pointer?.inLane)
            return;

        let object;
        if (state.sliderGesture) {
            object = newObject('slider', state.sliderGesture.side, state.sliderGesture.startAngle);
            object.arcAngle = state.sliderGesture.arcAngle || 1;
        } else {
            object = newObject(state.tool, state.pointer.side, state.pointer.angle);
        }

        const group = svg('g', { opacity: 0.9 });
        drawObject(group, object, true);
        const labelPoint = pointAt(object.angle, radiusFor(object.side) + (object.side === 'left' ? 34 : -34));
        const label = svg('text', { class: 'preview-label', x: labelPoint.x, y: labelPoint.y });
        label.textContent = state.sliderGesture ? `${formatSigned(state.sliderGesture.arcAngle)}°` : state.tool;
        group.append(label);
        el.previewLayer.append(group);
    }

    function renderTimeline(now) {
        el.timelineWindow.replaceChildren();
        const fragment = document.createDocumentFragment();
        const min = now - TIMELINE_WINDOW / 2;
        const max = now + TIMELINE_WINDOW / 2;

        let controls = state.timingControlPoints;
        if (!controls.length) {
            const config = timing();
            controls = [{ time: config.offset, beatLength: 60000 / config.bpm, meter: 4 }];
        }

        let renderedBeats = 0;
        for (let timingIndex = 0; timingIndex < controls.length && renderedBeats < 4096; timingIndex++) {
            const point = controls[timingIndex];
            const segmentStart = timingIndex === 0 ? min : Math.max(min, point.time);
            const segmentEnd = Math.min(max, controls[timingIndex + 1]?.time ?? max + 0.0001);
            if (segmentEnd < min || segmentStart > max)
                continue;

            const firstBeatIndex = Math.ceil((segmentStart - point.time) / point.beatLength - 0.000001);
            const meter = Number.isInteger(point.meter) && point.meter > 0 ? point.meter : 4;
            for (let beatIndex = firstBeatIndex; renderedBeats < 4096; beatIndex++) {
                const beatTime = point.time + beatIndex * point.beatLength;
                if (beatTime > max + 0.0001 || beatTime >= segmentEnd - 0.0001)
                    break;
                if (beatTime < min - 0.0001)
                    continue;
                const line = document.createElement('i');
                const timingChange = timingIndex > 0 && beatIndex === 0;
                line.className = `timeline-beat ${positiveModulo(beatIndex, meter) === 0 ? 'strong' : ''} ${timingChange ? 'timing-change' : ''}`;
                line.style.left = `${(beatTime - min) / TIMELINE_WINDOW * 100}%`;
                if (timingChange)
                    line.title = `Timing change · ${round(60000 / point.beatLength, 3)} BPM`;
                fragment.append(line);
                renderedBeats++;
            }
        }

        for (const object of state.objects) {
            if (object.startTime < min || object.startTime > max)
                continue;
            const left = (object.startTime - min) / TIMELINE_WINDOW * 100;
            const tick = document.createElement('button');
            tick.type = 'button';
            tick.className = `timeline-tick ${object.side} ${object.id === state.selectedId ? 'selected' : ''}`;
            tick.style.left = `${left}%`;
            tick.title = `${capitalise(object.type)} · ${formatTime(object.startTime)}`;
            tick.addEventListener('click', () => selectObject(object.id, true));
            fragment.append(tick);
        }

        el.timelineWindow.append(fragment);
    }

    function renderObjectList() {
        el.objectList.replaceChildren();
        el.objectCount.textContent = String(state.objects.length);

        if (state.objects.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'empty-list';
            empty.textContent = 'No objects yet';
            el.objectList.append(empty);
            return;
        }

        const fragment = document.createDocumentFragment();
        for (const object of state.objects) {
            const row = document.createElement('button');
            row.type = 'button';
            row.dataset.objectId = object.id;
            row.className = `object-row ${object.side} ${object.id === state.selectedId ? 'selected' : ''}`;

            const colour = document.createElement('span');
            colour.className = 'colour-bar';
            const type = document.createElement('span');
            type.className = 'type';
            type.textContent = object.type;
            const details = document.createElement('span');
            details.className = 'details';
            details.textContent = `${object.side} · ${object.angle.toFixed(1)}°${object.type === 'slider' ? ` · ${formatSigned(object.arcAngle)}°` : ''}`;
            const time = document.createElement('time');
            time.textContent = formatTime(object.startTime);

            row.append(colour, type, details, time);
            fragment.append(row);
        }
        el.objectList.append(fragment);
    }

    function selectObject(id, seek = false) {
        state.selectedId = id && findObject(id) ? id : null;
        if (seek && state.selectedId)
            seekTo(findObject(state.selectedId).startTime);
        renderAll();

        if (state.selectedId) {
            const row = el.objectList.querySelector(`[data-object-id="${cssEscape(state.selectedId)}"]`);
            row?.scrollIntoView({ block: 'nearest' });
        }
    }

    function updateInspector() {
        const object = findObject(state.selectedId);
        const controls = [el.editType, el.editSide, el.editStart, el.editAngle];
        const durationControls = [el.editDuration];
        const sliderControls = [el.editArc, el.editRepeats];

        for (const control of [...controls, ...durationControls, ...sliderControls])
            control.disabled = !object;
        for (const button of [el.deleteSelected, el.snapSelected, el.duplicateSelected])
            button.disabled = !object;

        if (!object) {
            el.selectionLabel.textContent = 'No selection';
            for (const control of [...controls, ...durationControls, ...sliderControls])
                control.value = '';
            showInspectorFields(null);
            return;
        }

        el.selectionLabel.textContent = `${capitalise(object.type)} · ${object.side}`;
        el.editType.value = object.type;
        el.editSide.value = object.side;
        el.editStart.value = String(round(object.startTime, 3));
        el.editAngle.value = String(round(object.angle, 3));
        el.editDuration.value = object.duration == null ? '' : String(round(object.duration, 3));
        el.editArc.value = object.arcAngle == null ? '' : String(round(object.arcAngle, 3));
        el.editRepeats.value = object.repeatCount == null ? '0' : String(object.repeatCount);
        showInspectorFields(object.type);
    }

    function showInspectorFields(type) {
        for (const field of document.querySelectorAll('.duration-field'))
            field.hidden = type === 'flick' || !type;
        for (const field of document.querySelectorAll('.arc-field, .repeat-field'))
            field.hidden = type !== 'slider';
    }

    function applyInspectorChanges() {
        const object = findObject(state.selectedId);
        if (!object)
            return;

        const oldType = object.type;
        object.type = ['flick', 'hold', 'slider'].includes(el.editType.value) ? el.editType.value : oldType;
        object.side = el.editSide.value === 'right' ? 'right' : 'left';
        object.startTime = Math.max(0, finiteNumber(el.editStart.value, object.startTime));
        object.angle = normaliseAngle(finiteNumber(el.editAngle.value, object.angle));

        if (object.type === 'flick') {
            delete object.duration;
            delete object.arcAngle;
            delete object.repeatCount;
        } else {
            object.duration = Math.max(1, finiteNumber(el.editDuration.value, object.duration ?? beatLength(object.startTime)));
            if (object.type === 'slider') {
                object.arcAngle = clamp(finiteNumber(el.editArc.value, object.arcAngle ?? 90), -2880, 2880);
                if (Math.abs(object.arcAngle) < 1)
                    object.arcAngle = 1;
                object.repeatCount = clamp(Math.round(finiteNumber(el.editRepeats.value, object.repeatCount ?? 0)), 0, 16);
            } else {
                delete object.arcAngle;
                delete object.repeatCount;
            }
        }

        setStatus('Object updated.');
        renderAll();
    }

    function snapSelectedTime() {
        const object = findObject(state.selectedId);
        if (!object)
            return;
        object.startTime = round(snapTime(object.startTime), 3);
        setStatus('Selected object snapped to the timing grid.');
        renderAll();
    }

    function duplicateSelected() {
        const object = findObject(state.selectedId);
        if (!object)
            return;
        const copy = {
            ...object,
            id: createId(),
            startTime: round(adjacentSnapTimeWithTiming(object.startTime, currentTimingConfiguration(), 1), 3),
        };
        addObject(copy, true);
    }

    function deleteSelected() {
        const index = state.objects.findIndex(object => object.id === state.selectedId);
        if (index < 0)
            return;
        const [removed] = state.objects.splice(index, 1);
        state.selectedId = null;
        setStatus(`Deleted ${removed.type}.`);
        renderAll();
    }

    function sortObjects() {
        state.objects.sort((a, b) => a.startTime - b.startTime || a.side.localeCompare(b.side) || a.angle - b.angle);
    }

    function findObject(id) {
        return id ? state.objects.find(object => object.id === id) : null;
    }

    function objectEndTime(object) {
        return object.startTime + (object.duration ?? 0);
    }

    function updateTool(tool) {
        if (!['select', 'flick', 'hold', 'slider'].includes(tool))
            return;
        state.tool = tool;
        state.sliderGesture = null;
        state.draggingObject = null;
        for (const button of el.toolButtons.querySelectorAll('[data-tool]'))
            button.classList.toggle('active', button.dataset.tool === tool);
        el.toolHelp.textContent = toolHelp[tool];
        renderPreview();
    }

    function handleKeyboard(event) {
        if (event.target.matches('input, select, textarea'))
            return;

        if (event.code === 'Space') {
            event.preventDefault();
            togglePlayback();
            return;
        }

        if (event.key === 'Delete' || event.key === 'Backspace') {
            event.preventDefault();
            deleteSelected();
            return;
        }

        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
            event.preventDefault();
            stepTime(event.key === 'ArrowRight' ? 1 : -1);
            return;
        }

        const tool = ({ '1': 'flick', '2': 'hold', '3': 'slider', '4': 'select', v: 'select', V: 'select' })[event.key];
        if (tool)
            updateTool(tool);
    }

    function projectData() {
        const manualTiming = timing();
        return {
            format: PROJECT_FORMAT,
            version: PROJECT_VERSION,
            metadata: {
                title: el.metaTitle.value.trim() || 'Untitled',
                artist: el.metaArtist.value.trim() || 'Unknown artist',
                creator: el.metaCreator.value.trim() || 'Zanthous',
                difficulty: el.metaDifficulty.value.trim() || 'Normal',
                audioName: state.audioName || '',
            },
            timing: {
                ...manualTiming,
                sourceName: state.timingSourceName,
                ignoredLines: state.timingIgnoredLines,
                points: serialiseTimingPoints(state.timingPoints),
            },
            objects: state.objects.map(object => ({ ...object })),
        };
    }

    function saveProject() {
        const project = projectData();
        const filename = `${safeFilename(project.metadata.artist)} - ${safeFilename(project.metadata.title)} [${safeFilename(project.metadata.difficulty)}].sticks.json`;
        downloadBlob(new Blob([JSON.stringify(project, null, 2)], { type: 'application/json' }), filename);
        setStatus('Project JSON saved. Audio remains a separate local file.');
    }

    async function loadProjectFile(event) {
        const file = event.target.files?.[0];
        if (!file)
            return;

        try {
            const parsed = JSON.parse(await file.text());
            loadProject(parsed);
            setStatus(`Loaded project: ${file.name}${parsed.metadata?.audioName ? ` · reselect ${parsed.metadata.audioName}` : ''}`);
        } catch (error) {
            setStatus(`Could not load project: ${error.message}`, true);
        } finally {
            event.target.value = '';
        }
    }

    function loadProject(project) {
        if (!project || project.format !== PROJECT_FORMAT)
            throw new Error('This is not a Sticks Mapper project.');
        if (Number(project.version) !== PROJECT_VERSION)
            throw new Error(`Unsupported Sticks Mapper project version: ${project.version}.`);
        if (!Array.isArray(project.objects))
            throw new Error('Project object list is missing.');

        // A project intentionally does not embed audio. Never retain a song from a previously
        // opened project, as that could silently package the wrong file on the next export.
        el.audio.pause();
        el.audio.removeAttribute('src');
        el.audio.load();
        if (state.audioUrl)
            URL.revokeObjectURL(state.audioUrl);
        state.audioUrl = null;
        state.audioFile = null;

        el.metaTitle.value = project.metadata?.title ?? '';
        el.metaArtist.value = project.metadata?.artist ?? '';
        el.metaCreator.value = project.metadata?.creator ?? 'Zanthous';
        el.metaDifficulty.value = project.metadata?.difficulty ?? 'Normal';
        el.timingBpm.value = String(clamp(finiteNumber(project.timing?.bpm, 120), 1, 1000));
        el.timingOffset.value = String(finiteNumber(project.timing?.offset, 0));
        setSnapSelector(project.timing?.snap ?? 4);

        const loadedTimingPoints = normaliseTimingPoints(project.timing?.points ?? []);
        if (controlTimingPoints(loadedTimingPoints).length) {
            setImportedTimingPoints(
                loadedTimingPoints,
                project.timing?.sourceName || 'Imported timing',
                project.timing?.ignoredLines ?? 0,
            );
        } else {
            state.timingPoints = [];
            state.timingControlPoints = [];
            state.timingSourceName = '';
            state.timingIgnoredLines = 0;
        }

        state.audioName = project.metadata?.audioName ?? '';
        el.audioName.textContent = state.audioUrl ? state.audioName : `${state.audioName || 'No audio'} (reselect audio)`;
        state.objects = project.objects.map(object => normaliseObject(object));
        state.selectedId = null;
        updateTimingUi();
        renderAll();
    }

    function normaliseObject(input, fallbackBeatLength = Number.NaN) {
        const type = ['flick', 'hold', 'slider'].includes(input.type) ? input.type : 'flick';
        const startTime = Math.max(0, finiteNumber(input.startTime, 0));
        const resolvedFallbackBeatLength = Number.isFinite(fallbackBeatLength)
            ? fallbackBeatLength
            : beatLength(startTime);
        const object = {
            id: typeof input.id === 'string' && input.id ? input.id : createId(),
            type,
            startTime,
            side: input.side === 'right' ? 'right' : 'left',
            angle: normaliseAngle(finiteNumber(input.angle, 0)),
        };

        if (type !== 'flick')
            object.duration = Math.max(1, finiteNumber(input.duration, resolvedFallbackBeatLength));
        if (type === 'slider') {
            object.arcAngle = clamp(finiteNumber(input.arcAngle, 90), -2880, 2880);
            if (Math.abs(object.arcAngle) < 1)
                object.arcAngle = 1;
            object.repeatCount = clamp(Math.round(finiteNumber(input.repeatCount, 0)), 0, 16);
        }
        return object;
    }

    async function runExport() {
        try {
            if (!state.audioFile)
                throw new Error('Load the song audio before exporting an .osz.');
            const result = await exportMapPackage(projectData());
            downloadBlob(result.blob, result.filename);
            setStatus('Exported a playable .osz containing the song and authored mode-0 Sticks map.');
        } catch (error) {
            setStatus(`Export failed: ${error.message}`, true);
        }
    }

    /**
     * Export boundary for the mapper. UI and project editing intentionally do not know about
     * osu!'s archive/beatmap encoding. Future format revisions can replace this function while
     * the mapper UI continues to consume the stable { blob, filename } result.
     */
    async function exportMapPackage(project, audioFile = state.audioFile) {
        if (!audioFile)
            throw new Error('No audio file is selected.');

        const audioFilename = safeArchiveFilename(audioFile.name || 'audio.mp3');
        const audioExtension = audioFilename.split('.').pop()?.toLowerCase();
        if (!SUPPORTED_AUDIO_EXTENSIONS.has(audioExtension))
            throw new Error('osu!lazer .osz imports support .mp3, .ogg, or .wav audio. Convert this song first.');

        const beatmapFilename = `${safeFilename(project.metadata.artist)} - ${safeFilename(project.metadata.title)} (${safeFilename(project.metadata.creator)}) [${safeFilename(project.metadata.difficulty)}].osu`;
        const projectFilename = `${safeFilename(project.metadata.artist)} - ${safeFilename(project.metadata.title)} [${safeFilename(project.metadata.difficulty)}].sticks.json`;
        const osuText = createOsuFile(project, audioFilename);
        const zipBytes = createStoredZip([
            { name: audioFilename, data: new Uint8Array(await audioFile.arrayBuffer()) },
            { name: beatmapFilename, data: encodeUtf8(osuText) },
            { name: projectFilename, data: encodeUtf8(JSON.stringify(project, null, 2)) },
        ]);
        const filename = `${safeFilename(project.metadata.artist)} - ${safeFilename(project.metadata.title)} (${safeFilename(project.metadata.creator)}).osz`;
        return {
            blob: new Blob([zipBytes], { type: 'application/zip' }),
            filename,
        };
    }

    function createOsuFile(project, audioFilename) {
        const bpm = clamp(finiteNumber(project.timing?.bpm, 120), 1, 1000);
        const offset = finiteNumber(project.timing?.offset, 0);
        const beat = 60000 / bpm;
        const importedTimingPoints = normaliseTimingPoints(project.timing?.points ?? []);
        const hasImportedTiming = controlTimingPoints(importedTimingPoints).length > 0;
        const timingRows = hasImportedTiming
            ? importedTimingPoints.map(point => point.raw)
            : [`${formatOsuNumber(offset)},${formatOsuNumber(beat)},4,2,0,100,1,0`];
        const exportTiming = {
            bpm,
            offset,
            snap: clamp(Math.round(finiteNumber(project.timing?.snap, 4)), 1, 64),
            points: importedTimingPoints,
        };
        const lines = [
            'osu file format v14',
            '',
            '[General]',
            `AudioFilename: ${audioFilename}`,
            'AudioLeadIn: 0',
            'PreviewTime: -1',
            'Countdown: 0',
            'SampleSet: Normal',
            'StackLeniency: 0.7',
            'Mode: 0',
            'LetterboxInBreaks: 0',
            'WidescreenStoryboard: 1',
            '',
            '[Editor]',
            `BeatDivisor: ${clamp(Math.round(finiteNumber(project.timing?.snap, 4)), 1, 64)}`,
            'GridSize: 4',
            'TimelineZoom: 1',
            '',
            '[Metadata]',
            `Title:${cleanOsuValue(project.metadata?.title || 'Untitled')}`,
            `TitleUnicode:${cleanOsuValue(project.metadata?.title || 'Untitled')}`,
            `Artist:${cleanOsuValue(project.metadata?.artist || 'Unknown artist')}`,
            `ArtistUnicode:${cleanOsuValue(project.metadata?.artist || 'Unknown artist')}`,
            `Creator:${cleanOsuValue(project.metadata?.creator || 'Zanthous')}`,
            `Version:${cleanOsuValue(project.metadata?.difficulty || 'Normal')}`,
            'Source:',
            `Tags:${PORTABLE_RULESET_TAG} sticks controller dual-stick`,
            'BeatmapID:0',
            'BeatmapSetID:-1',
            '',
            '[Difficulty]',
            'HPDrainRate:5',
            'CircleSize:5',
            'OverallDifficulty:5',
            'ApproachRate:5',
            'SliderMultiplier:1.4',
            'SliderTickRate:1',
            '',
            '[Events]',
            '',
            '[TimingPoints]',
            ...timingRows,
            '',
            '[HitObjects]',
        ];

        const sorted = [...project.objects]
            .map(object => normaliseObject(object, beatLengthWithTiming(finiteNumber(object.startTime, 0), exportTiming)))
            .sort((a, b) => a.startTime - b.startTime || a.side.localeCompare(b.side));
        for (const object of sorted)
            lines.push(encodeHitObject(object));

        return `${lines.join('\r\n')}\r\n`;
    }

    function encodeHitObject(object) {
        const position = exportPointAt(object.angle, object.side);
        const x = Math.round(position.x);
        const y = Math.round(position.y);
        const time = markerNumber(object.startTime);
        const side = object.side === 'right' ? 'r' : 'l';
        const angle = markerNumber(normaliseAngle(object.angle));
        let marker;

        switch (object.type) {
            case 'hold':
                marker = `sticks-v1~h~${side}~${angle}~${markerNumber(object.duration)}.wav`;
                break;

            case 'slider':
                marker = `sticks-v1~s~${side}~${angle}~${markerNumber(object.duration)}~${markerNumber(object.arcAngle)}~${clamp(Math.round(object.repeatCount ?? 0), 0, 16)}.wav`;
                break;

            default:
                marker = `sticks-v1~f~${side}~${angle}.wav`;
                break;
        }

        if (object.type === 'flick')
            return `${x},${y},${time},1,0,0:0:0:100:${marker}`;

        // Duration carriers improve stock lazer's map-length/statistics view. The marker remains
        // authoritative for every gameplay property after Sticks conversion.
        const endTime = markerNumber(object.startTime + Math.max(1, finiteNumber(object.duration, 1)));
        return `256,192,${time},8,0,${endTime},0:0:0:100:${marker}`;
    }

    function exportPointAt(angle, side) {
        const radians = angle * Math.PI / 180;
        const radius = side === 'left' ? EXPORT_OUTER_RADIUS : EXPORT_INNER_RADIUS;
        return {
            x: EXPORT_CENTRE_X + Math.cos(radians) * radius,
            y: EXPORT_CENTRE_Y + Math.sin(radians) * radius,
        };
    }

    function createStoredZip(entries) {
        const localParts = [];
        const centralParts = [];
        let localOffset = 0;
        const { time, date } = dosDateTime(new Date());

        for (const entry of entries) {
            const name = encodeUtf8(entry.name);
            const data = entry.data instanceof Uint8Array ? entry.data : new Uint8Array(entry.data);
            const crc = crc32(data);

            const localHeader = new Uint8Array(30 + name.length);
            const localView = new DataView(localHeader.buffer);
            writeUint32(localView, 0, 0x04034b50);
            writeUint16(localView, 4, 20);
            writeUint16(localView, 6, 0x0800);
            writeUint16(localView, 8, 0);
            writeUint16(localView, 10, time);
            writeUint16(localView, 12, date);
            writeUint32(localView, 14, crc);
            writeUint32(localView, 18, data.length);
            writeUint32(localView, 22, data.length);
            writeUint16(localView, 26, name.length);
            writeUint16(localView, 28, 0);
            localHeader.set(name, 30);
            localParts.push(localHeader, data);

            const centralHeader = new Uint8Array(46 + name.length);
            const centralView = new DataView(centralHeader.buffer);
            writeUint32(centralView, 0, 0x02014b50);
            writeUint16(centralView, 4, 20);
            writeUint16(centralView, 6, 20);
            writeUint16(centralView, 8, 0x0800);
            writeUint16(centralView, 10, 0);
            writeUint16(centralView, 12, time);
            writeUint16(centralView, 14, date);
            writeUint32(centralView, 16, crc);
            writeUint32(centralView, 20, data.length);
            writeUint32(centralView, 24, data.length);
            writeUint16(centralView, 28, name.length);
            writeUint16(centralView, 30, 0);
            writeUint16(centralView, 32, 0);
            writeUint16(centralView, 34, 0);
            writeUint16(centralView, 36, 0);
            writeUint32(centralView, 38, 0);
            writeUint32(centralView, 42, localOffset);
            centralHeader.set(name, 46);
            centralParts.push(centralHeader);

            localOffset += localHeader.length + data.length;
        }

        const centralDirectory = concatBytes(centralParts);
        const end = new Uint8Array(22);
        const endView = new DataView(end.buffer);
        writeUint32(endView, 0, 0x06054b50);
        writeUint16(endView, 4, 0);
        writeUint16(endView, 6, 0);
        writeUint16(endView, 8, entries.length);
        writeUint16(endView, 10, entries.length);
        writeUint32(endView, 12, centralDirectory.length);
        writeUint32(endView, 16, localOffset);
        writeUint16(endView, 20, 0);

        return concatBytes([...localParts, centralDirectory, end]);
    }

    const crcTable = (() => {
        const table = new Uint32Array(256);
        for (let i = 0; i < table.length; i++) {
            let value = i;
            for (let bit = 0; bit < 8; bit++)
                value = (value & 1) ? 0xedb88320 ^ (value >>> 1) : value >>> 1;
            table[i] = value >>> 0;
        }
        return table;
    })();

    function crc32(bytes) {
        let crc = 0xffffffff;
        for (const byte of bytes)
            crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
        return (crc ^ 0xffffffff) >>> 0;
    }

    function concatBytes(parts) {
        const length = parts.reduce((sum, part) => sum + part.length, 0);
        const result = new Uint8Array(length);
        let offset = 0;
        for (const part of parts) {
            result.set(part, offset);
            offset += part.length;
        }
        return result;
    }

    function dosDateTime(value) {
        const year = clamp(value.getFullYear(), 1980, 2107);
        return {
            time: (value.getHours() << 11) | (value.getMinutes() << 5) | Math.floor(value.getSeconds() / 2),
            date: ((year - 1980) << 9) | ((value.getMonth() + 1) << 5) | value.getDate(),
        };
    }

    function writeUint16(view, offset, value) {
        view.setUint16(offset, value, true);
    }

    function writeUint32(view, offset, value) {
        view.setUint32(offset, value >>> 0, true);
    }

    function encodeUtf8(value) {
        return new TextEncoder().encode(value);
    }

    // Exposed for automated/read-only validation without coupling tests to UI events.
    window.SticksMapperExport = Object.freeze({
        exportMapPackage,
        createOsuFile,
        createStoredZip,
        crc32,
        parseOsuFile,
        parseTimingPoints,
        normaliseTimingPoints,
        activeTimingPoint,
        beatLengthWithTiming,
        snapTimeWithTiming,
        adjacentSnapTimeWithTiming,
    });

    function cleanOsuValue(value) {
        return String(value).replace(/[\r\n:]/g, ' ').trim();
    }

    function formatOsuNumber(value) {
        return Number(value.toFixed(6)).toString();
    }

    function markerNumber(value) {
        return finiteNumber(value, 0).toFixed(3).replace(/\.?0+$/, '');
    }

    function safeArchiveFilename(value) {
        const pieces = String(value || 'audio.mp3').split(/[\\/]/);
        return safeFilename(pieces[pieces.length - 1]);
    }

    function downloadBlob(blob, filename) {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = filename;
        document.body.append(anchor);
        anchor.click();
        anchor.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    function arcPath(radius, startAngle, arcAngle) {
        const steps = Math.max(2, Math.ceil(Math.abs(arcAngle) / 8));
        let path = '';
        for (let i = 0; i <= steps; i++) {
            const point = pointAt(startAngle + arcAngle * i / steps, radius);
            path += `${i === 0 ? 'M' : 'L'} ${point.x.toFixed(3)} ${point.y.toFixed(3)} `;
        }
        return path.trim();
    }

    function pointAt(angle, radius) {
        const radians = angle * Math.PI / 180;
        return {
            x: CENTRE + Math.cos(radians) * radius,
            y: CENTRE + Math.sin(radians) * radius,
        };
    }

    function tangentSegment(angle, radius, halfLength) {
        const point = pointAt(angle, radius);
        const radians = (angle + 90) * Math.PI / 180;
        const dx = Math.cos(radians) * halfLength;
        const dy = Math.sin(radians) * halfLength;
        return { x1: point.x - dx, y1: point.y - dy, x2: point.x + dx, y2: point.y + dy };
    }

    function radiusFor(side) {
        return side === 'left' ? OUTER_RADIUS : INNER_RADIUS;
    }

    function svg(tag, attributes) {
        const element = document.createElementNS('http://www.w3.org/2000/svg', tag);
        for (const [name, value] of Object.entries(attributes ?? {}))
            element.setAttribute(name, String(value));
        return element;
    }

    function normaliseAngle(angle) {
        angle %= 360;
        return angle < 0 ? angle + 360 : angle;
    }

    function normaliseSignedAngle(angle) {
        angle = normaliseAngle(angle);
        return angle > 180 ? angle - 360 : angle;
    }

    function formatTime(milliseconds) {
        milliseconds = Math.max(0, finiteNumber(milliseconds, 0));
        const minutes = Math.floor(milliseconds / 60000);
        const seconds = Math.floor(milliseconds / 1000) % 60;
        const millis = Math.floor(milliseconds % 1000);
        return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}.${String(millis).padStart(3, '0')}`;
    }

    function formatSigned(value) {
        const rounded = round(finiteNumber(value, 0), 1);
        return rounded > 0 ? `+${rounded}` : String(rounded);
    }

    function finiteNumber(value, fallback) {
        if (value === '' || value == null)
            return fallback;
        const number = Number(value);
        return Number.isFinite(number) ? number : fallback;
    }

    function positiveModulo(value, modulus) {
        return ((value % modulus) + modulus) % modulus;
    }

    function clamp(value, minimum, maximum) {
        return Math.min(maximum, Math.max(minimum, value));
    }

    function round(value, decimals) {
        const scale = 10 ** decimals;
        return Math.round(value * scale) / scale;
    }

    function capitalise(value) {
        return value.charAt(0).toUpperCase() + value.slice(1);
    }

    function safeFilename(value) {
        return String(value || 'Untitled').replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_').trim() || 'Untitled';
    }

    function createId() {
        return globalThis.crypto?.randomUUID?.() ?? `sticks-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
    }

    function cssEscape(value) {
        return globalThis.CSS?.escape ? CSS.escape(value) : value.replace(/[^a-zA-Z0-9_-]/g, '\\$&');
    }

    function setStatus(message, error = false) {
        el.status.textContent = message;
        el.status.style.color = error ? 'var(--danger)' : '';
    }
})();
