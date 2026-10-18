// Metadata carried through saves and exports. On load the recognised blocks are lifted out of the file's
// container into a format-neutral record. On save they are adjusted to match the new pixels and spliced into
// the freshly encoded bytes, because the browser's encoders write none. Blocks the editor does not recognise
// are dropped, since they may describe pixel data that no longer exists.

import { ascii, asciiBytes, concatBytes, crc32, deflate, indexOfByte, inflate, startsWith, viewOf } from './bytes.js';
import { adjustExif, adjustXmp, validateExif } from './exif.js';
import { walkGif, walkPng, walkWebp } from './formats.js';

// The kinds named in the status bar and in export notices.
export const KIND_EXIF = 'EXIF';
export const KIND_ICC = 'ICC';
export const KIND_XMP = 'XMP';
export const KIND_IPTC = 'IPTC';
export const KIND_TEXT = 'Text';

const JPEG_EXIF_HEADER = 'Exif\0\0';
const JPEG_XMP_HEADER = 'http://ns.adobe.com/xap/1.0/\0';
const JPEG_XMP_EXTENSION_HEADER = 'http://ns.adobe.com/xmp/extension/\0';
const JPEG_ICC_HEADER = 'ICC_PROFILE\0';
const JPEG_IPTC_HEADER = 'Photoshop 3.0\0';
const JPEG_SEGMENT_MAX = 65533;
const JPEG_ICC_CHUNK = JPEG_SEGMENT_MAX - 14;
const PNG_XMP_KEYWORD = 'XML:com.adobe.xmp';
const PNG_COLOUR_CHUNKS = ['sRGB', 'gAMA', 'cHRM', 'cICP'];
const PNG_TEXT_CHUNKS = ['tEXt', 'zTXt', 'iTXt'];

// The kinds each format can hold. JPEG and GIF hold text only as plain comments.
const HOLDS = {
    jpeg: [KIND_EXIF, KIND_ICC, KIND_XMP, KIND_IPTC, KIND_TEXT],
    png: [KIND_EXIF, KIND_ICC, KIND_XMP, KIND_TEXT],
    webp: [KIND_EXIF, KIND_ICC, KIND_XMP],
    gif: [KIND_TEXT],
    bmp: [KIND_ICC],
};

export function emptyMetadata() {
    return {
        exif: null,
        exifMalformed: false,
        xmp: null,
        xmpExtended: [],
        icc: null,
        iccName: null,
        iptc: [],
        comments: [],
        pngChunks: [],
        resolution: null,
        webpLossless: false,
        problems: [],
    };
}

// Whether the pixels are in a colour space the preview has to convert from.
export function needsColourManagement(metadata) {
    return metadata.icc !== null || metadata.pngChunks.some((chunk) => ['gAMA', 'cHRM', 'cICP'].includes(chunk.type));
}

export function metadataKinds(metadata) {
    const kinds = [];
    if (metadata.exif) {
        kinds.push(KIND_EXIF);
    }
    if (metadata.icc) {
        kinds.push(KIND_ICC);
    }
    if (metadata.xmp) {
        kinds.push(KIND_XMP);
    }
    if (metadata.iptc.length > 0) {
        kinds.push(KIND_IPTC);
    }
    if (metadata.comments.length > 0 || metadata.pngChunks.some((chunk) => PNG_TEXT_CHUNKS.includes(chunk.type))) {
        kinds.push(KIND_TEXT);
    }

    return kinds;
}

// Extraction never throws: a block that cannot be read is recorded as a problem and left out, so a metadata
// fault never stops the image opening.
export async function extractMetadata(format, bytes) {
    const metadata = emptyMetadata();

    try {
        if (format === 'jpeg') {
            extractJpeg(bytes, metadata);
        } else if (format === 'png') {
            await extractPng(bytes, metadata);
        } else if (format === 'webp') {
            extractWebp(bytes, metadata);
        } else if (format === 'gif') {
            extractGif(bytes, metadata);
        } else if (format === 'bmp') {
            extractBmp(bytes, metadata);
        }
    } catch (e) {
        metadata.problems.push(e.message);
    }

    if (metadata.exif) {
        try {
            validateExif(metadata.exif);
        } catch (e) {
            metadata.exifMalformed = true;
            metadata.problems.push(e.message);
        }
    }

    return metadata;
}

function* jpegSegments(bytes) {
    const view = viewOf(bytes);
    let pos = 2;
    while (pos + 4 <= bytes.length) {
        if (bytes[pos] !== 0xff) {
            return;
        }

        const marker = bytes[pos + 1];
        if (marker === 0xff) {
            pos++;
            continue;
        }
        if (marker === 0x01 || (marker >= 0xd0 && marker <= 0xd8)) {
            pos += 2;
            continue;
        }
        if (marker === 0xda || marker === 0xd9) {
            yield { marker, start: pos, end: pos, rest: true };
            return;
        }

        const length = view.getUint16(pos + 2);
        const end = pos + 2 + length;
        yield { marker, start: pos, end, payload: bytes.subarray(pos + 4, Math.min(end, bytes.length)) };
        pos = end;
    }
}

function extractJpeg(bytes, metadata) {
    const iccChunks = [];

    for (const segment of jpegSegments(bytes)) {
        const { marker, payload } = segment;
        if (segment.rest) {
            break;
        }

        if (marker === 0xe1 && startsWith(payload, 0, JPEG_EXIF_HEADER) && !metadata.exif) {
            metadata.exif = payload.slice(JPEG_EXIF_HEADER.length);
        } else if (marker === 0xe1 && startsWith(payload, 0, JPEG_XMP_HEADER) && !metadata.xmp) {
            metadata.xmp = payload.slice(JPEG_XMP_HEADER.length);
        } else if (marker === 0xe1 && startsWith(payload, 0, JPEG_XMP_EXTENSION_HEADER)) {
            metadata.xmpExtended.push(payload.slice());
        } else if (marker === 0xe2 && startsWith(payload, 0, JPEG_ICC_HEADER) && payload.length > 14) {
            iccChunks.push({ sequence: payload[12], data: payload.subarray(14) });
        } else if (marker === 0xed && startsWith(payload, 0, JPEG_IPTC_HEADER)) {
            metadata.iptc.push(payload.slice());
        } else if (marker === 0xfe) {
            metadata.comments.push(payload.slice());
        }
    }

    if (iccChunks.length > 0) {
        iccChunks.sort((a, b) => a.sequence - b.sequence);
        metadata.icc = concatBytes(iccChunks.map((chunk) => chunk.data));
    }
}

function readPngText(type, data) {
    const nul = indexOfByte(data, 0, 0);
    const keyword = ascii(data, 0, nul);
    return { type, keyword, nul };
}

async function extractPng(bytes, metadata) {
    const pending = [];

    walkPng(bytes, (type, data) => {
        if (type === 'eXIf' && !metadata.exif) {
            metadata.exif = data.slice();
        } else if (type === 'iCCP') {
            const nul = indexOfByte(data, 0, 0);
            metadata.iccName = ascii(data, 0, nul);
            pending.push(inflate(data.subarray(nul + 2)).then((icc) => { metadata.icc = icc; }));
        } else if (type === 'pHYs' && data.length >= 9) {
            const view = viewOf(data);
            metadata.resolution = { x: view.getUint32(0), y: view.getUint32(4), unit: data[8] };
        } else if (type === 'iTXt' && readPngText(type, data).keyword === PNG_XMP_KEYWORD) {
            pending.push(readITxt(data).then((text) => { metadata.xmp = text; }));
        } else if (PNG_COLOUR_CHUNKS.includes(type) || PNG_TEXT_CHUNKS.includes(type) || type === 'tIME') {
            metadata.pngChunks.push({ type, data: data.slice() });
        }
    });

    const results = await Promise.allSettled(pending);
    for (const result of results) {
        if (result.status === 'rejected') {
            metadata.problems.push(result.reason?.message || String(result.reason));
        }
    }
}

// iTXt: keyword, NUL, compression flag, method, language tag, NUL, translated keyword, NUL, text.
async function readITxt(data) {
    let pos = indexOfByte(data, 0, 0) + 1;
    const compressed = data[pos] === 1;
    pos = indexOfByte(data, 0, pos + 2) + 1;
    pos = indexOfByte(data, 0, pos) + 1;
    const text = data.subarray(pos);

    return compressed ? inflate(text) : text.slice();
}

function extractWebp(bytes, metadata) {
    walkWebp(bytes, (fourcc, data) => {
        if (fourcc === 'ICCP') {
            metadata.icc = data.slice();
        } else if (fourcc === 'EXIF') {
            // Some writers keep the JPEG segment header on the front.
            metadata.exif = startsWith(data, 0, JPEG_EXIF_HEADER) ? data.slice(6) : data.slice();
        } else if (fourcc === 'XMP ') {
            metadata.xmp = data.slice();
        } else if (fourcc === 'VP8L') {
            metadata.webpLossless = true;
        }
    });
}

function extractGif(bytes, metadata) {
    walkGif(bytes, {
        extension: (label, data) => {
            if (label === 0xfe && data.length > 0) {
                metadata.comments.push(data.slice());
            }
        },
    });
}

const BMP_PROFILE_EMBEDDED = 0x4d424544;

function extractBmp(bytes, metadata) {
    const view = viewOf(bytes);
    const headerSize = view.getUint32(14, true);

    if (headerSize >= 40 && bytes.length >= 14 + 40) {
        const x = view.getInt32(14 + 24, true);
        const y = view.getInt32(14 + 28, true);
        if (x > 0 && y > 0) {
            metadata.resolution = { x, y, unit: 1 };
        }
    }

    if (headerSize >= 124 && bytes.length >= 14 + 124 && view.getUint32(14 + 56, true) === BMP_PROFILE_EMBEDDED) {
        const offset = view.getUint32(14 + 112, true);
        const size = view.getUint32(14 + 116, true);
        if (size > 0 && 14 + offset + size <= bytes.length) {
            metadata.icc = bytes.slice(14 + offset, 14 + offset + size);
        }
    }
}

function isPngComment(chunk) {
    return chunk.type === 'tEXt' && readPngText(chunk.type, chunk.data).keyword === 'Comment';
}

// The comments a format with only plain comments can take: JPEG and GIF comments, and PNG text stored under
// the Comment keyword.
function plainComments(metadata) {
    const comments = [...metadata.comments];
    for (const chunk of metadata.pngChunks) {
        if (isPngComment(chunk)) {
            comments.push(chunk.data.slice(readPngText(chunk.type, chunk.data).nul + 1));
        }
    }

    return comments;
}

// PNG text a plain comment cannot represent.
function hasOtherPngText(metadata) {
    return metadata.pngChunks.some((chunk) => PNG_TEXT_CHUNKS.includes(chunk.type) && !isPngComment(chunk));
}

// A copy of the record whose pixel-describing facts match the image about to be written. `geometryChanged`
// is any crop, rotate or flip; `edited` is any operation at all.
export function adjustMetadata(metadata, { width, height, geometryChanged, edited, now = new Date() }) {
    const adjusted = { ...metadata, problems: [], droppedMalformedExif: false };

    if (metadata.exif) {
        if (metadata.exifMalformed) {
            // It cannot be made true of the new pixels, so it only survives a save that changed no pixels.
            if (edited) {
                adjusted.exif = null;
                adjusted.droppedMalformedExif = true;
            }
        } else {
            try {
                adjusted.exif = adjustExif(metadata.exif, { width, height, dropThumbnail: geometryChanged });
            } catch {
                adjusted.exif = edited ? null : metadata.exif;
                adjusted.droppedMalformedExif = edited;
            }
        }
    }

    if (metadata.xmp) {
        adjusted.xmp = adjustXmp(metadata.xmp, { width, height });
    }

    adjusted.pngChunks = metadata.pngChunks.map((chunk) => (chunk.type === 'tIME' ? { type: 'tIME', data: pngTime(now) } : chunk));

    return adjusted;
}

function pngTime(date) {
    const data = new Uint8Array(7);
    const view = viewOf(data);
    view.setUint16(0, date.getUTCFullYear());
    data[2] = date.getUTCMonth() + 1;
    data[3] = date.getUTCDate();
    data[4] = date.getUTCHours();
    data[5] = date.getUTCMinutes();
    data[6] = date.getUTCSeconds();
    return data;
}

// Splices the record into encoded bytes of the given format. Returns the bytes and the kinds of metadata the
// format could not hold. GIF comments are written by the GIF encoder and BMP metadata by the BMP encoder, so
// their bytes pass through.
export async function writeMetadata(format, encoded, metadata) {
    const dropped = new Set(metadataKinds(metadata).filter((kind) => !HOLDS[format].includes(kind)));
    if ((format === 'jpeg' || format === 'gif') && hasOtherPngText(metadata)) {
        dropped.add(KIND_TEXT);
    }

    let bytes = encoded;
    if (format === 'jpeg') {
        bytes = writeJpeg(encoded, metadata, dropped);
    } else if (format === 'png') {
        bytes = await writePng(encoded, metadata);
    } else if (format === 'webp') {
        bytes = writeWebp(encoded, metadata);
    }

    return { bytes, dropped: [...dropped] };
}

// The comments the GIF encoder writes.
export function gifComments(metadata) {
    return plainComments(metadata);
}

function jpegSegment(marker, payload) {
    const segment = new Uint8Array(4 + payload.length);
    segment[0] = 0xff;
    segment[1] = marker;
    viewOf(segment).setUint16(2, payload.length + 2);
    segment.set(payload, 4);
    return segment;
}

function writeJpeg(encoded, metadata, dropped) {
    const ours = [];

    if (metadata.exif) {
        const payload = concatBytes([asciiBytes(JPEG_EXIF_HEADER), metadata.exif]);
        if (payload.length <= JPEG_SEGMENT_MAX) {
            ours.push(jpegSegment(0xe1, payload));
        } else {
            dropped.add(KIND_EXIF);
        }
    }

    if (metadata.xmp) {
        const payload = concatBytes([asciiBytes(JPEG_XMP_HEADER), metadata.xmp]);
        if (payload.length <= JPEG_SEGMENT_MAX) {
            ours.push(jpegSegment(0xe1, payload));
            // Extended XMP is tied to the main packet, and only a JPEG can carry it.
            for (const extension of metadata.xmpExtended) {
                ours.push(jpegSegment(0xe1, extension));
            }
        } else {
            dropped.add(KIND_XMP);
        }
    }

    if (metadata.icc) {
        const count = Math.ceil(metadata.icc.length / JPEG_ICC_CHUNK);
        if (count <= 255) {
            for (let i = 0; i < count; i++) {
                const chunk = metadata.icc.subarray(i * JPEG_ICC_CHUNK, (i + 1) * JPEG_ICC_CHUNK);
                ours.push(jpegSegment(0xe2, concatBytes([asciiBytes(JPEG_ICC_HEADER), new Uint8Array([i + 1, count]), chunk])));
            }
        } else {
            dropped.add(KIND_ICC);
        }
    }

    for (const iptc of metadata.iptc) {
        ours.push(jpegSegment(0xed, iptc));
    }

    for (const comment of plainComments(metadata)) {
        ours.push(jpegSegment(0xfe, comment.subarray(0, JPEG_SEGMENT_MAX)));
    }

    // SOI, the encoder's JFIF header, the preserved segments, then the encoder's own tables and scan data.
    // Metadata the encoder wrote itself (the browser tags its output with an sRGB profile) is replaced, so the
    // file carries what the original carried and nothing else.
    const head = [];
    const tail = [];
    for (const segment of jpegSegments(encoded)) {
        if (segment.rest) {
            tail.push(encoded.subarray(segment.start));
            break;
        }
        const bytes = encoded.subarray(segment.start, segment.end);
        if (segment.marker === 0xe0) {
            head.push(bytes);
        } else if (!(segment.marker >= 0xe1 && segment.marker <= 0xef) && segment.marker !== 0xfe) {
            tail.push(bytes);
        }
    }

    return concatBytes([encoded.subarray(0, 2), ...head, ...ours, ...tail]);
}

function pngChunk(type, data) {
    const chunk = new Uint8Array(12 + data.length);
    const view = viewOf(chunk);
    view.setUint32(0, data.length);
    chunk.set(asciiBytes(type), 4);
    chunk.set(data, 8);
    view.setUint32(8 + data.length, crc32(chunk, 4, 8 + data.length));
    return chunk;
}

async function writePng(encoded, metadata) {
    const ours = [];

    if (metadata.icc) {
        const name = asciiBytes((metadata.iccName || 'ICC Profile').slice(0, 79));
        ours.push(pngChunk('iCCP', concatBytes([name, new Uint8Array([0, 0]), await deflate(metadata.icc)])));
    }
    for (const chunk of metadata.pngChunks) {
        if (PNG_COLOUR_CHUNKS.includes(chunk.type)) {
            ours.push(pngChunk(chunk.type, chunk.data));
        }
    }

    if (metadata.resolution) {
        const data = new Uint8Array(9);
        viewOf(data).setUint32(0, metadata.resolution.x);
        viewOf(data).setUint32(4, metadata.resolution.y);
        data[8] = metadata.resolution.unit;
        ours.push(pngChunk('pHYs', data));
    }

    if (metadata.exif) {
        ours.push(pngChunk('eXIf', metadata.exif));
    }

    if (metadata.xmp) {
        ours.push(pngChunk('iTXt', concatBytes([asciiBytes(PNG_XMP_KEYWORD), new Uint8Array([0, 0, 0, 0, 0]), metadata.xmp])));
    }

    for (const chunk of metadata.pngChunks) {
        if (PNG_TEXT_CHUNKS.includes(chunk.type) || chunk.type === 'tIME') {
            ours.push(pngChunk(chunk.type, chunk.data));
        }
    }

    // Comments from a JPEG or GIF become Comment text.
    for (const comment of metadata.comments) {
        ours.push(pngChunk('tEXt', concatBytes([asciiBytes('Comment\0'), comment])));
    }

    if (ours.length === 0) {
        return encoded;
    }

    // The encoder's critical chunks and transparency are kept, in order, with the preserved chunks placed
    // straight after IHDR so they precede PLTE and IDAT as the PNG spec requires.
    const chunks = [];
    let pos = 8;
    const view = viewOf(encoded);
    while (pos + 12 <= encoded.length) {
        const length = view.getUint32(pos);
        const type = ascii(encoded, pos + 4, 4);
        const whole = encoded.subarray(pos, pos + 12 + length);
        const critical = type.charCodeAt(0) < 0x61;
        if (critical || type === 'tRNS') {
            chunks.push(whole);
            if (type === 'IHDR') {
                chunks.push(...ours);
            }
        }
        pos += 12 + length;
        if (type === 'IEND') {
            break;
        }
    }

    return concatBytes([encoded.subarray(0, 8), ...chunks]);
}

function webpChunk(fourcc, data) {
    const padded = data.length + (data.length & 1);
    const chunk = new Uint8Array(8 + padded);
    chunk.set(asciiBytes(fourcc), 0);
    viewOf(chunk).setUint32(4, data.length, true);
    chunk.set(data, 8);
    return chunk;
}

// The browser tags its WebP output with an sRGB profile, so the file is rebuilt from the encoder's image
// chunks and the preserved metadata alone.
function writeWebp(encoded, metadata) {
    const image = [];
    let width = 0;
    let height = 0;
    let alpha = false;

    walkWebp(encoded, (fourcc, data) => {
        if (fourcc === 'VP8X') {
            alpha = (data[0] & 0x10) !== 0;
            width = 1 + (data[4] | (data[5] << 8) | (data[6] << 16));
            height = 1 + (data[7] | (data[8] << 8) | (data[9] << 16));
        } else if (fourcc === 'ALPH') {
            alpha = true;
            image.push(webpChunk(fourcc, data));
        } else if (fourcc === 'VP8L') {
            const bits = viewOf(data).getUint32(1, true);
            width = 1 + (bits & 0x3fff);
            height = 1 + ((bits >>> 14) & 0x3fff);
            alpha = alpha || ((bits >>> 28) & 1) === 1;
            image.push(webpChunk(fourcc, data));
        } else if (fourcc === 'VP8 ') {
            if (!width) {
                width = viewOf(data).getUint16(6, true) & 0x3fff;
                height = viewOf(data).getUint16(8, true) & 0x3fff;
            }
            image.push(webpChunk(fourcc, data));
        }
    });

    const extended = metadata.icc || metadata.exif || metadata.xmp || image.length > 1;
    if (!extended) {
        return riff(image);
    }

    const header = new Uint8Array(10);
    header[0] = (metadata.icc ? 0x20 : 0) | (alpha ? 0x10 : 0) | (metadata.exif ? 0x08 : 0) | (metadata.xmp ? 0x04 : 0);
    const w = width - 1;
    const h = height - 1;
    header.set([w & 0xff, (w >> 8) & 0xff, (w >> 16) & 0xff, h & 0xff, (h >> 8) & 0xff, (h >> 16) & 0xff], 4);

    const chunks = [webpChunk('VP8X', header)];
    if (metadata.icc) {
        chunks.push(webpChunk('ICCP', metadata.icc));
    }
    chunks.push(...image);
    if (metadata.exif) {
        chunks.push(webpChunk('EXIF', metadata.exif));
    }
    if (metadata.xmp) {
        chunks.push(webpChunk('XMP ', metadata.xmp));
    }

    return riff(chunks);
}

function riff(chunks) {
    const body = concatBytes(chunks);
    const header = new Uint8Array(12);
    header.set(asciiBytes('RIFF'), 0);
    viewOf(header).setUint32(4, 4 + body.length, true);
    header.set(asciiBytes('WEBP'), 8);

    return concatBytes([header, body]);
}

