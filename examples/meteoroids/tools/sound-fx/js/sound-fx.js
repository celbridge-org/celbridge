// Sound FX Editor document entry point. A Sound FX sound is a WAV rendered by jsfxr, carrying the settings
// that rendered it in an sfxr chunk. The document is those settings plus an undo history of them; every
// change re-renders the sound, redraws the waveform, and reports the change so the host auto-saves the new
// WAV. Any other WAV opens as a plain sound that can be played but not edited.
//
// The editor is declared binary, so the host hands it the document as base64 and saves the base64 it is given.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { ContentLoadedReason } from '/assets/celbridge-client/api/document-api.js';
import { t } from '/assets/celbridge-client/localization.js';
import { parseWav, writeWav } from './wav-file.js';
import { createWaveform } from './waveform.js';
import {
    DUTY_WAVE_TYPES, GENERATOR, GROUPS, PRESETS, SAMPLE_RATE, SAMPLE_SIZE, WAVE_TYPES,
    defaultValue, formatValue, isSigned, mutate, normalizeParams, paramsEqual, rangeOf, render, rollPreset,
    rollRandom,
} from './params.js';

// DOM elements
const app = document.getElementById('app');
const playButton = document.getElementById('play');
const playOnChangeButton = document.getElementById('play-on-change');
const undoButton = document.getElementById('undo');
const redoButton = document.getElementById('redo');
const notice = document.getElementById('notice');
const noticeText = document.getElementById('notice-text');
const noticeDismiss = document.getElementById('notice-dismiss');
const waveformCanvas = document.getElementById('waveform');
const infoDuration = document.getElementById('info-duration');
const infoFormat = document.getElementById('info-format');
const infoSize = document.getElementById('info-size');
const presetButtons = document.getElementById('preset-buttons');
const mutateButton = document.getElementById('mutate');
const randomButton = document.getElementById('random');
const startFromPresetButton = document.getElementById('start-from-preset');
const replacePanel = document.getElementById('replace-panel');
const replaceWarning = document.getElementById('replace-warning');
const replaceButtons = document.getElementById('replace-buttons');
const replaceCancelButton = document.getElementById('replace-cancel');
const settings = document.getElementById('settings');
const waveTypeControl = document.getElementById('wave-type');
const volumeRow = document.getElementById('volume-row');
const groupsElement = document.getElementById('groups');
const errorView = document.getElementById('error-view');
const errorText = document.getElementById('error-text');

const client = celbridge;

// State
let documentResource = '';
let initialized = false;
let writable = true;
let readOnly = true;

// 'sound-fx' for a sound with settings this editor understands, 'plain' for any other file, null until loaded.
let kind = null;
// 'newer' when the settings come from a newer version of the editor, which must not be downgraded.
let blocked = null;
// Why a plain file has nothing to play, as a localization key.
let audioError = null;

let params = null;
let originalParams = null;
let originalBytes = new Uint8Array(0);
let originalBase64 = '';
let originalAudio = null;
let fileFormat = null;

let currentBytes = new Uint8Array(0);
let currentFormat = null;
let audioBuffer = null;
let previewPending = false;

// Whether there are changes the host has not yet saved.
let dirty = false;

let playOnChange = true;
let showReplacedNotice = false;

const waveform = createWaveform(waveformCanvas);
const history = createHistory();

// Undo history: a list of params snapshots and the index of the current one. Snapshots are never mutated.
function createHistory() {
    let states = [];
    let index = -1;

    return {
        reset(state) {
            states = state ? [state] : [];
            index = states.length - 1;
        },
        push(state) {
            states = states.slice(0, index + 1);
            states.push(state);
            index = states.length - 1;
        },
        current: () => states[index],
        canUndo: () => index > 0,
        canRedo: () => index < states.length - 1,
        undo() {
            if (index > 0) {
                index--;
                return true;
            }
            return false;
        },
        redo() {
            if (index < states.length - 1) {
                index++;
                return true;
            }
            return false;
        },
    };
}

// Bytes and base64

// Decoded directly rather than through a data: URL, which the host's wrapped fetch rejects.
function base64ToBytes(base64) {
    if (!base64) {
        return new Uint8Array(0);
    }
    if (typeof Uint8Array.fromBase64 === 'function') {
        return Uint8Array.fromBase64(base64);
    }

    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
}

function bytesToBase64(bytes) {
    if (bytes.length === 0) {
        return Promise.resolve('');
    }

    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result.slice(reader.result.indexOf(',') + 1));
        reader.onerror = () => reject(reader.error);
        reader.readAsDataURL(new Blob([bytes]));
    });
}

function formatBytes(size) {
    if (size < 1024) {
        return t('SFX_Size_Bytes', size);
    }
    if (size < 1024 * 1024) {
        return t('SFX_Size_KB', (size / 1024).toFixed(1));
    }
    return t('SFX_Size_MB', (size / (1024 * 1024)).toFixed(1));
}

function fileName() {
    return documentResource.slice(documentResource.lastIndexOf('/') + 1).replace(/^project:/, '');
}

// Playback

let audioContext = null;
let playing = null;

function getAudioContext() {
    audioContext ??= new AudioContext();
    return audioContext;
}

function decodeAudio(bytes) {
    // decodeAudioData detaches the buffer it is given, so it gets a copy.
    return getAudioContext().decodeAudioData(bytes.slice().buffer);
}

function stop() {
    if (playing) {
        const { source } = playing;
        playing = null;
        try {
            source.stop();
        } catch {
            // Already stopped.
        }
    }
    waveform.setPlayhead(null);
}

// Plays the current sound from the start, cutting off one already playing.
async function play() {
    stop();
    if (!audioBuffer) {
        return;
    }

    const context = getAudioContext();
    if (context.state === 'suspended') {
        await context.resume();
    }

    const source = context.createBufferSource();
    source.buffer = audioBuffer;
    source.connect(context.destination);
    const session = { source, startTime: context.currentTime, duration: audioBuffer.duration };
    playing = session;
    source.onended = () => {
        if (playing === session) {
            playing = null;
            waveform.setPlayhead(null);
        }
    };
    source.start();

    const sweep = () => {
        if (playing !== session) {
            return;
        }
        const elapsed = context.currentTime - session.startTime;
        waveform.setPlayhead(Math.min(1, Math.max(0, elapsed / session.duration)));
        requestAnimationFrame(sweep);
    };
    requestAnimationFrame(sweep);
}

function playIfWanted() {
    if (playOnChange) {
        play();
    }
}

// Rendering

function showAudio(samples) {
    waveform.setSamples(samples ?? (audioBuffer ? audioBuffer.getChannelData(0) : null));
    updateInfo();
}

// Renders the current params into currentBytes and the audio buffer. Settings the file was opened with
// show the file's own audio, since re-rendering noise would come out slightly different.
function renderCurrent() {
    previewPending = false;
    stop();

    if (originalAudio && paramsEqual(params, originalParams)) {
        currentBytes = originalBytes;
        currentFormat = fileFormat;
        audioBuffer = originalAudio;
        showAudio();
        return;
    }

    const wave = render(params);
    currentBytes = writeWav(wave.wav, params, GENERATOR);
    currentFormat = { sampleRate: SAMPLE_RATE, bitsPerSample: SAMPLE_SIZE, channels: 1 };

    const samples = Float32Array.from(wave.buffer, (value) => Math.max(-1, Math.min(1, value)));
    audioBuffer = null;
    if (samples.length > 0) {
        audioBuffer = getAudioContext().createBuffer(1, samples.length, SAMPLE_RATE);
        audioBuffer.copyToChannel(samples, 0);
    }
    showAudio(samples);
}

function renderSafely() {
    try {
        renderCurrent();
        return true;
    } catch (e) {
        console.error('[Sound FX] Failed to render:', e);
        client.dialog.showNotification('error', t('SFX_Toast_RenderFailed'));
        return false;
    }
}

// Coalesces the renders a slider drag asks for into one per frame.
function schedulePreview() {
    if (previewPending) {
        return;
    }
    previewPending = true;
    requestAnimationFrame(() => {
        if (previewPending) {
            renderSafely();
        }
    });
}

// Editing

// Applies a change the user made and records it as one undo step.
function commit(next) {
    if (readOnly || kind !== 'sound-fx') {
        return;
    }

    params = next;
    if (!paramsEqual(next, history.current())) {
        history.push(next);
    }
    afterChange();
}

function afterChange() {
    showReplacedNotice = false;
    renderSafely();
    syncSettings();
    updateControls();
    reportChange();
    playIfWanted();
}

function undo() {
    if (!readOnly && kind === 'sound-fx' && history.undo()) {
        params = history.current();
        afterChange();
    }
}

function redo() {
    if (!readOnly && kind === 'sound-fx' && history.redo()) {
        params = history.current();
        afterChange();
    }
}

function rollPresetVariation(method) {
    commit(rollPreset(method, params?.sound_vol));
}

// Turns a plain file into a Sound FX sound, once the user has confirmed the replacement.
function startFromPreset(method) {
    if (readOnly || kind !== 'plain') {
        return;
    }

    closeReplacePanel();
    kind = 'sound-fx';
    blocked = null;
    audioError = null;
    originalParams = null;
    originalAudio = null;
    params = rollPreset(method);
    history.reset(params);
    afterChange();
}

// Settings UI

const sliders = new Map();

function createSlider(name, labelKey) {
    const row = document.createElement('div');
    row.className = 'slider';

    const id = `param-${name}`;
    const label = document.createElement('label');
    label.className = 'slider-label';
    label.htmlFor = id;
    label.dataset.locKey = labelKey;

    const track = document.createElement('div');
    track.className = 'slider-track';
    track.dataset.signed = String(isSigned(name));

    const { min, max } = name === 'sound_vol' ? { min: 0, max: 1 } : rangeOf(name);
    const input = document.createElement('input');
    input.type = 'range';
    input.id = id;
    input.min = String(min);
    input.max = String(max);
    input.step = '0.005';
    input.dataset.locTitle = 'SFX_Slider_Tooltip';
    track.append(input);

    const output = document.createElement('output');
    output.className = 'slider-value';
    output.htmlFor = id;

    // Dragging previews every position; releasing records the drag as one undo step.
    input.addEventListener('input', () => {
        if (readOnly || kind !== 'sound-fx') {
            return;
        }
        params = { ...params, [name]: Number(input.value) };
        output.textContent = formatValue(name, params[name]);
        schedulePreview();
    });
    input.addEventListener('change', () => commit({ ...params, [name]: Number(input.value) }));
    input.addEventListener('dblclick', () => commit({ ...params, [name]: defaultValue(name) }));

    row.append(label, track, output);
    sliders.set(name, { input, output });
    return row;
}

const groupElements = new Map();
const waveTypeButtons = [];

function buildSettings() {
    for (const { value, key } of WAVE_TYPES) {
        const button = document.createElement('button');
        button.type = 'button';
        button.setAttribute('role', 'radio');
        button.dataset.locKey = key;
        button.addEventListener('click', () => commit({ ...params, wave_type: value }));
        waveTypeButtons.push({ value, button });
        waveTypeControl.append(button);
    }

    volumeRow.append(createSlider('sound_vol', 'SFX_Volume'));

    for (const group of GROUPS) {
        const fieldset = document.createElement('fieldset');
        fieldset.className = 'group';
        const legend = document.createElement('legend');
        legend.dataset.locKey = group.key;
        fieldset.append(legend);
        for (const slider of group.sliders) {
            fieldset.append(createSlider(slider.param, slider.key));
        }
        groupElements.set(group.id, fieldset);
        groupsElement.append(fieldset);
    }

    for (const preset of PRESETS) {
        const button = document.createElement('button');
        button.type = 'button';
        button.dataset.edit = '';
        button.dataset.locKey = preset.key;
        button.dataset.preset = preset.id;
        button.addEventListener('click', () => rollPresetVariation(preset.method));
        presetButtons.append(button);

        const replaceButton = document.createElement('button');
        replaceButton.type = 'button';
        replaceButton.dataset.locKey = preset.key;
        replaceButton.addEventListener('click', () => startFromPreset(preset.method));
        replaceButtons.append(replaceButton);
    }
}

// Strings that take arguments, which data-loc-key cannot express.
function localizeDynamic() {
    for (const button of presetButtons.querySelectorAll('button')) {
        const preset = PRESETS.find((entry) => entry.id === button.dataset.preset);
        button.title = t('SFX_Preset_Tooltip', t(preset.key));
    }
    replaceWarning.textContent = t('SFX_Confirm_Replace', fileName());
}

function syncSettings() {
    if (kind !== 'sound-fx' || !params) {
        return;
    }

    for (const { value, button } of waveTypeButtons) {
        button.setAttribute('aria-checked', String(params.wave_type === value));
    }
    for (const [name, { input, output }] of sliders) {
        input.value = String(params[name]);
        output.textContent = formatValue(name, params[name]);
    }

    const duty = groupElements.get('duty');
    const inactive = !DUTY_WAVE_TYPES.includes(params.wave_type);
    duty.dataset.inactive = String(inactive);
    if (inactive) {
        duty.title = t('SFX_Duty_Tooltip');
    } else {
        duty.removeAttribute('title');
    }
}

// Replacing a plain file

function openReplacePanel() {
    if (readOnly || kind !== 'plain') {
        return;
    }
    replaceWarning.textContent = t('SFX_Confirm_Replace', fileName());
    replacePanel.hidden = false;
    startFromPresetButton.setAttribute('aria-expanded', 'true');
    replaceButtons.querySelector('button')?.focus();
}

function closeReplacePanel() {
    replacePanel.hidden = true;
    startFromPresetButton.setAttribute('aria-expanded', 'false');
}

// Read-only state, the notice bar and the info line

function updateControls() {
    const isSoundFx = kind === 'sound-fx';
    readOnly = !writable || blocked !== null || kind === null;

    for (const element of document.querySelectorAll('[data-sound-fx]')) {
        element.hidden = !isSoundFx;
    }
    document.getElementById('plain-bar').hidden = kind !== 'plain';
    if (kind !== 'plain' || readOnly) {
        closeReplacePanel();
    }

    for (const element of document.querySelectorAll('[data-edit]')) {
        element.disabled = readOnly;
    }
    for (const element of settings.querySelectorAll('input, button')) {
        element.disabled = readOnly;
    }
    undoButton.disabled = readOnly || !isSoundFx || !history.canUndo();
    redoButton.disabled = readOnly || !isSoundFx || !history.canRedo();
    playButton.disabled = !audioBuffer;

    if (initialized) {
        client.input.notifyEditAvailability({ canUndo: !undoButton.disabled, canRedo: !redoButton.disabled });
    }

    updateNotice();
}

function updateNotice() {
    let message = '';
    if (showReplacedNotice) {
        message = t('SFX_Notice_Replaced');
    } else if (blocked === 'newer') {
        message = t('SFX_Notice_Newer');
    } else if (kind === 'plain') {
        message = audioError ? t('SFX_Notice_Undecodable', t(audioError)) : t('SFX_Notice_Plain');
    } else if (!writable) {
        message = t('SFX_Notice_ReadOnly');
    }

    noticeText.textContent = message;
    notice.hidden = !message;
    noticeDismiss.hidden = !showReplacedNotice;
}

function describeFormat(format) {
    if (!format || !format.sampleRate) {
        return '';
    }

    const channels = format.channels === 1 ? t('SFX_Mono') : format.channels === 2 ? t('SFX_Stereo')
        : t('SFX_Channels', format.channels);
    return t('SFX_Status_Format', format.sampleRate, format.bitsPerSample, channels);
}

function updateInfo() {
    infoDuration.textContent = audioBuffer ? t('SFX_Status_Duration', audioBuffer.duration.toFixed(2)) : '';
    infoFormat.textContent = describeFormat(currentFormat);
    infoSize.textContent = kind ? formatBytes(currentBytes.length) : '';
}

// Loading

function showLoadError(key) {
    errorText.textContent = t(key);
    app.hidden = true;
    errorView.hidden = false;
}

async function loadContent(base64) {
    // A change on disk wins over edits not yet saved.
    dirty = false;
    previewPending = false;
    stop();
    closeReplacePanel();

    originalBase64 = base64 || '';
    originalBytes = base64ToBytes(originalBase64);
    currentBytes = originalBytes;
    kind = null;
    blocked = null;
    audioError = null;
    params = null;
    originalParams = null;
    originalAudio = null;
    audioBuffer = null;
    fileFormat = null;
    history.reset(null);

    if (originalBytes.length === 0) {
        kind = 'plain';
        audioError = 'SFX_Error_Empty';
    } else {
        const parsed = parseWav(originalBytes);
        if (!parsed.ok) {
            kind = 'plain';
            audioError = 'SFX_Error_NotWav';
        } else {
            fileFormat = parsed.fmt;
            if (parsed.settings.status === 'ok') {
                kind = 'sound-fx';
                params = normalizeParams(parsed.settings.params);
                originalParams = params;
                history.reset(params);
            } else {
                kind = 'plain';
                if (parsed.settings.status === 'newer') {
                    blocked = 'newer';
                }
            }

            try {
                originalAudio = await decodeAudio(originalBytes);
            } catch (e) {
                console.warn('[Sound FX] Failed to decode:', e);
                if (kind === 'plain') {
                    audioError = 'SFX_Error_Decode';
                }
            }
        }
    }

    currentFormat = fileFormat;
    audioBuffer = originalAudio;

    errorView.hidden = true;
    app.hidden = false;

    if (kind === 'sound-fx' && !originalAudio) {
        // The settings are good but the audio is not: show what they render. Nothing is saved until an edit.
        renderSafely();
    } else {
        showAudio();
    }
    syncSettings();
    updateControls();
}

// Saving

// The host saves about a second after the last reported change, by calling onRequestSave.
function reportChange() {
    if (readOnly) {
        return;
    }
    dirty = true;
    client.document.notifyChanged();
}

async function save() {
    if (!dirty || readOnly || kind !== 'sound-fx') {
        return;
    }

    try {
        if (previewPending) {
            renderCurrent();
        }

        // Back at the settings the file was opened with, the original bytes are saved, so the file ends
        // byte-identical to how it started.
        const unchanged = originalParams && paramsEqual(params, originalParams);
        await client.document.save(unchanged ? originalBase64 : await bytesToBase64(currentBytes));
        dirty = false;
    } catch (e) {
        console.error('[Sound FX] Failed to save:', e);
        await client.dialog.showNotification('error', t('SFX_Toast_SaveFailed', fileName()));
    }
}

// Wiring

buildSettings();

playButton.addEventListener('click', () => play());
playOnChangeButton.addEventListener('click', () => {
    playOnChange = !playOnChange;
    playOnChangeButton.setAttribute('aria-pressed', String(playOnChange));
});
undoButton.addEventListener('click', undo);
redoButton.addEventListener('click', redo);
mutateButton.addEventListener('click', () => commit(mutate(params)));
randomButton.addEventListener('click', () => commit(rollRandom(params?.sound_vol)));
startFromPresetButton.addEventListener('click', () => {
    if (replacePanel.hidden) {
        openReplacePanel();
    } else {
        closeReplacePanel();
    }
});
replaceCancelButton.addEventListener('click', closeReplacePanel);
noticeDismiss.addEventListener('click', () => {
    showReplacedNotice = false;
    updateNotice();
});

function isTextTarget(target) {
    if (!(target instanceof HTMLElement)) {
        return false;
    }
    if (target.isContentEditable || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT') {
        return true;
    }
    return target.tagName === 'INPUT' && !['range', 'checkbox', 'radio', 'button'].includes(target.type);
}

document.addEventListener('keydown', (event) => {
    if (event.defaultPrevented) {
        return;
    }

    const ctrl = event.ctrlKey || event.metaKey;
    const key = event.key.toLowerCase();

    if (ctrl && !event.shiftKey && key === 'z') {
        undo();
    } else if (ctrl && (key === 'y' || (event.shiftKey && key === 'z'))) {
        redo();
    } else if (key === ' ' && !ctrl && !event.altKey && !isTextTarget(event.target)) {
        if (!event.repeat) {
            play();
        }
    } else if (key === 'escape' && !replacePanel.hidden) {
        closeReplacePanel();
    } else {
        return;
    }

    event.preventDefault();
});

// Space on a focused button would otherwise click it as well as play.
document.addEventListener('keyup', (event) => {
    if (event.key === ' ' && !isTextTarget(event.target)) {
        event.preventDefault();
    }
});

new ResizeObserver(() => waveform.draw()).observe(waveformCanvas);

// The theme is mirrored onto html[data-theme] before this runs, so the redraw reads the new colours.
client.appState.onChanged(() => waveform.draw());

client.viewState.onChanged((viewState) => {
    if (viewState.writable) {
        writable = viewState.writable === 'Writable';
        updateControls();
    }
});

// The host's Undo and Redo verbs, from its menu or shortcuts, arrive here while the editor holds focus.
client.onNotification('input/performEdit', (edit) => {
    if (edit?.command === 'undo') {
        undo();
    } else if (edit?.command === 'redo') {
        redo();
    }
});

client.localization.onLanguageChanged(async (locale) => {
    try {
        await client.localization.loadStrings(locale);
        localizeDynamic();
        syncSettings();
        updateNotice();
        updateInfo();
    } catch (e) {
        console.warn('[Sound FX] Failed to reload localization:', e);
    }
});

try {
    await client.initializeDocument({
        onContent: async (content, documentMetadata) => {
            documentResource = documentMetadata?.resourceKey || '';
            localizeDynamic();
            try {
                await loadContent(content);
            } catch (e) {
                console.error('[Sound FX] Failed to read the file:', e);
                showLoadError('SFX_Error_Read');
            }
        },

        onRequestSave: save,

        onExternalChange: async () => {
            try {
                const { content } = await client.document.load();
                const hadUnsavedEdits = dirty || previewPending;
                await loadContent(content);
                if (hadUnsavedEdits) {
                    showReplacedNotice = true;
                    updateNotice();
                }
            } catch (e) {
                console.error('[Sound FX] Failed to reload content:', e);
            }
            client.document.notifyContentLoaded(ContentLoadedReason.ExternalReload);
        },

        onRenamed: (documentMetadata) => {
            documentResource = documentMetadata?.resourceKey || documentResource;
            localizeDynamic();
        },

        onRequestState: () => JSON.stringify({ playOnChange }),

        onRestoreState: (stateJson) => {
            try {
                const state = JSON.parse(stateJson);
                if (typeof state?.playOnChange === 'boolean') {
                    playOnChange = state.playOnChange;
                    playOnChangeButton.setAttribute('aria-pressed', String(playOnChange));
                }
            } catch {
                // Ignore malformed state. Play on change stays on.
            }
        },
    });
    initialized = true;
    updateControls();
} catch (e) {
    console.error('[Sound FX] Failed to initialize:', e);
}
