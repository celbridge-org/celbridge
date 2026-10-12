// RIFF/WAVE reading and writing, including the sfxr chunk that carries a Sound FX sound's synth settings.
// A Sound FX file is a standard PCM WAV with one extra chunk after the audio:
//
//   RIFF WAVE
//     fmt   PCM, 1 channel, 44100 Hz, 16-bit
//     data  the rendered samples
//     sfxr  the synth settings, as UTF-8 JSON
//
// The chunk sits after data, so readers that expect audio at byte 44 still work, and every other reader
// skips a chunk it does not recognise. No DOM here: scripts/make-templates.mjs runs this under Node too.

export const SETTINGS_CHUNK_ID = 'sfxr';
export const SETTINGS_FORMAT = 'celbridge-sound-fx';
export const SETTINGS_VERSION = 1;

const utf8Decoder = new TextDecoder('utf-8', { fatal: true });
const utf8Encoder = new TextEncoder();

function ascii(bytes, start, length) {
    let text = '';
    for (let i = start; i < start + length && i < bytes.length; i++) {
        text += String.fromCharCode(bytes[i]);
    }

    return text;
}

function writeAscii(bytes, offset, text) {
    for (let i = 0; i < text.length; i++) {
        bytes[offset + i] = text.charCodeAt(i) & 0xff;
    }
}

function viewOf(bytes) {
    return new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
}

// Lists the file's chunks. Returns null when the bytes are not a RIFF WAVE file. A chunk whose declared
// size runs past the end of the file is cut to what is there, as players do with a truncated recording.
export function readChunks(bytes) {
    if (bytes.length < 12 || ascii(bytes, 0, 4) !== 'RIFF' || ascii(bytes, 8, 4) !== 'WAVE') {
        return null;
    }

    const view = viewOf(bytes);
    const chunks = [];
    let offset = 12;
    while (offset + 8 <= bytes.length) {
        const id = ascii(bytes, offset, 4);
        const declared = view.getUint32(offset + 4, true);
        const start = offset + 8;
        const size = Math.min(declared, bytes.length - start);
        chunks.push({ id, offset, start, size });
        // Chunks are padded to an even length.
        offset = start + declared + (declared & 1);
    }

    return chunks;
}

// Reads the synth settings. status is 'none' (no sfxr chunk), 'invalid' (a chunk that cannot be read),
// 'newer' (a version this editor does not know) or 'ok'.
function readSettings(bytes, chunk) {
    if (!chunk) {
        return { status: 'none' };
    }

    let settings;
    try {
        let end = chunk.start + chunk.size;
        // Tolerate trailing NUL padding written inside the chunk.
        while (end > chunk.start && bytes[end - 1] === 0) {
            end--;
        }
        settings = JSON.parse(utf8Decoder.decode(bytes.subarray(chunk.start, end)));
    } catch {
        return { status: 'invalid' };
    }

    const version = settings?.version;
    if (settings?.format !== SETTINGS_FORMAT || !Number.isInteger(version) || version < 1) {
        return { status: 'invalid' };
    }
    if (version > SETTINGS_VERSION) {
        return { status: 'newer', version };
    }
    if (!settings.params || typeof settings.params !== 'object' || Array.isArray(settings.params)) {
        return { status: 'invalid' };
    }

    return { status: 'ok', version, params: settings.params, generator: settings.generator };
}

// Parses a WAV file. Returns { ok: false } for anything that is not RIFF WAVE, otherwise the audio format
// (fmt may be null in a damaged file) and the synth settings.
export function parseWav(bytes) {
    const chunks = readChunks(bytes);
    if (!chunks) {
        return { ok: false };
    }

    let fmt = null;
    const fmtChunk = chunks.find((chunk) => chunk.id === 'fmt ');
    if (fmtChunk && fmtChunk.size >= 16) {
        const view = viewOf(bytes);
        fmt = {
            audioFormat: view.getUint16(fmtChunk.start, true),
            channels: view.getUint16(fmtChunk.start + 2, true),
            sampleRate: view.getUint32(fmtChunk.start + 4, true),
            bitsPerSample: view.getUint16(fmtChunk.start + 14, true),
        };
    }

    const dataChunk = chunks.find((chunk) => chunk.id === 'data');
    const settingsChunk = chunks.find((chunk) => chunk.id === SETTINGS_CHUNK_ID);

    return {
        ok: true,
        fmt,
        dataLength: dataChunk ? dataChunk.size : 0,
        settings: readSettings(bytes, settingsChunk),
    };
}

// The JSON the sfxr chunk holds.
export function settingsJson(params, generator) {
    return JSON.stringify({
        format: SETTINGS_FORMAT,
        version: SETTINGS_VERSION,
        generator,
        params,
    });
}

// Builds a Sound FX file from the WAV jsfxr rendered (RIFFWAVE's .wav byte array) and the settings that
// rendered it. Everything jsfxr wrote up to the end of its data chunk is kept as is, and the sfxr chunk is
// appended after it.
export function writeWav(renderedWav, params, generator) {
    const rendered = renderedWav instanceof Uint8Array ? renderedWav : Uint8Array.from(renderedWav);
    const chunks = readChunks(rendered);
    const dataChunk = chunks?.find((chunk) => chunk.id === 'data');
    if (!dataChunk) {
        throw new Error('The rendered sound has no data chunk.');
    }

    const audioEnd = dataChunk.start + dataChunk.size;
    const audioPadded = audioEnd + (dataChunk.size & 1);
    const json = utf8Encoder.encode(settingsJson(params, generator));
    const jsonPadded = json.length + (json.length & 1);

    const bytes = new Uint8Array(audioPadded + 8 + jsonPadded);
    bytes.set(rendered.subarray(0, audioEnd), 0);
    writeAscii(bytes, audioPadded, SETTINGS_CHUNK_ID);
    const view = viewOf(bytes);
    view.setUint32(audioPadded + 4, json.length, true);
    bytes.set(json, audioPadded + 8);
    view.setUint32(4, bytes.length - 8, true);

    return bytes;
}
