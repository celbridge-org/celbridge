// The image formats the editor knows, how a file's own bytes identify one, and the container facts the editor
// needs before decoding: whether an image is animated, and which image an icon file should open as.

import { ascii, concatBytes, startsWith, viewOf } from './bytes.js';

export const FORMATS = {
    png: { id: 'png', label: 'PNG', mime: 'image/png', extension: '.png', writable: true },
    jpeg: { id: 'jpeg', label: 'JPEG', mime: 'image/jpeg', extension: '.jpg', writable: true, quality: true },
    webp: { id: 'webp', label: 'WebP', mime: 'image/webp', extension: '.webp', writable: true, quality: true, lossless: true },
    gif: { id: 'gif', label: 'GIF', mime: 'image/gif', extension: '.gif', writable: true },
    bmp: { id: 'bmp', label: 'BMP', mime: 'image/bmp', extension: '.bmp', writable: true },
    ico: { id: 'ico', label: 'ICO', mime: 'image/x-icon', extension: '.ico', writable: false },
};

const EXTENSIONS = {
    '.png': 'png',
    '.jpg': 'jpeg',
    '.jpeg': 'jpeg',
    '.webp': 'webp',
    '.gif': 'gif',
    '.bmp': 'bmp',
    '.ico': 'ico',
};

export function extensionOf(resourceKey) {
    const name = resourceKey.slice(resourceKey.lastIndexOf('/') + 1);
    const dot = name.lastIndexOf('.');

    return dot < 0 ? '' : name.slice(dot).toLowerCase();
}

export function formatForExtension(extension) {
    return EXTENSIONS[extension.toLowerCase()] || null;
}

// The format the bytes actually hold, whatever the file is named.
export function detectFormat(bytes) {
    if (bytes.length >= 8 && bytes[0] === 0x89 && startsWith(bytes, 1, 'PNG\r\n\x1a\n')) {
        return 'png';
    }
    if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) {
        return 'jpeg';
    }
    if (bytes.length >= 6 && (startsWith(bytes, 0, 'GIF87a') || startsWith(bytes, 0, 'GIF89a'))) {
        return 'gif';
    }
    if (bytes.length >= 12 && startsWith(bytes, 0, 'RIFF') && startsWith(bytes, 8, 'WEBP')) {
        return 'webp';
    }
    if (bytes.length >= 26 && startsWith(bytes, 0, 'BM')) {
        return 'bmp';
    }
    if (bytes.length >= 6 && bytes[0] === 0 && bytes[1] === 0 && bytes[2] === 1 && bytes[3] === 0) {
        return 'ico';
    }

    return null;
}

// Walks a GIF's blocks after the header and colour table. The visitor sees each extension (by label, with
// its sub-block data joined) and each image descriptor.
export function walkGif(bytes, visitor) {
    if (bytes.length < 13) {
        return;
    }

    let pos = 13;
    const packed = bytes[10];
    if (packed & 0x80) {
        pos += 3 * (1 << ((packed & 0x07) + 1));
    }

    const readSubBlocks = () => {
        const parts = [];
        while (pos < bytes.length) {
            const size = bytes[pos++];
            if (size === 0) {
                break;
            }
            parts.push(bytes.subarray(pos, pos + size));
            pos += size;
        }
        return concatBytes(parts);
    };

    while (pos < bytes.length) {
        const introducer = bytes[pos++];
        if (introducer === 0x21) {
            const label = bytes[pos++];
            const data = readSubBlocks();
            if (visitor.extension?.(label, data) === false) {
                return;
            }
        } else if (introducer === 0x2c) {
            const imagePacked = bytes[pos + 8];
            pos += 9;
            if (imagePacked & 0x80) {
                pos += 3 * (1 << ((imagePacked & 0x07) + 1));
            }
            pos += 1;
            readSubBlocks();
            if (visitor.image?.() === false) {
                return;
            }
        } else {
            return;
        }
    }
}

// Walks the RIFF chunks of a WebP file.
export function walkWebp(bytes, visitor) {
    const view = viewOf(bytes);
    let pos = 12;
    while (pos + 8 <= bytes.length) {
        const fourcc = ascii(bytes, pos, 4);
        const size = view.getUint32(pos + 4, true);
        const data = bytes.subarray(pos + 8, Math.min(bytes.length, pos + 8 + size));
        if (visitor(fourcc, data) === false) {
            return;
        }
        pos += 8 + size + (size & 1);
    }
}

// Walks the chunks of a PNG file up to and including IEND.
export function walkPng(bytes, visitor) {
    const view = viewOf(bytes);
    let pos = 8;
    while (pos + 12 <= bytes.length) {
        const length = view.getUint32(pos);
        const type = ascii(bytes, pos + 4, 4);
        const data = bytes.subarray(pos + 8, Math.min(bytes.length, pos + 8 + length));
        if (visitor(type, data) === false || type === 'IEND') {
            return;
        }
        pos += 12 + length;
    }
}

// An animated image opens as its first frame, and saving it would drop the rest.
export function isAnimated(format, bytes) {
    if (format === 'gif') {
        let frames = 0;
        walkGif(bytes, { image: () => ++frames < 2 });
        return frames > 1;
    }

    if (format === 'webp') {
        let animated = false;
        walkWebp(bytes, (fourcc, data) => {
            if (fourcc === 'VP8X' && data.length > 0 && data[0] & 0x02) {
                animated = true;
            }
            if (fourcc === 'ANIM' || fourcc === 'ANMF') {
                animated = true;
            }
            return !animated;
        });
        return animated;
    }

    if (format === 'png') {
        let animated = false;
        walkPng(bytes, (type, data) => {
            if (type === 'acTL' && data.length >= 4) {
                animated = viewOf(data).getUint32(0) > 1;
            }
            return type !== 'IDAT';
        });
        return animated;
    }

    return false;
}

// An icon file holds several images. The largest is rebuilt as an icon of one image, which the browser
// decodes with the icon's own transparency mask applied.
export function extractLargestIcon(bytes) {
    const view = viewOf(bytes);
    const count = view.getUint16(4, true);
    let best = null;

    for (let i = 0; i < count; i++) {
        const entry = 6 + i * 16;
        if (entry + 16 > bytes.length) {
            break;
        }

        const size = view.getUint32(entry + 8, true);
        const offset = view.getUint32(entry + 12, true);
        if (offset + size > bytes.length || size === 0) {
            continue;
        }

        let width = bytes[entry] || 256;
        let height = bytes[entry + 1] || 256;

        // A PNG-compressed entry can be larger than the directory's one-byte fields can say.
        if (size >= 24 && bytes[offset] === 0x89 && startsWith(bytes, offset + 12, 'IHDR')) {
            width = view.getUint32(offset + 16);
            height = view.getUint32(offset + 20);
        }

        const depth = view.getUint16(entry + 6, true);
        const score = width * height * 64 + depth;
        if (!best || score > best.score) {
            best = { entry, size, offset, score };
        }
    }

    if (!best) {
        return null;
    }

    const single = new Uint8Array(22 + best.size);
    single.set([0, 0, 1, 0, 1, 0]);
    single.set(bytes.subarray(best.entry, best.entry + 12), 6);
    viewOf(single).setUint32(18, 22, true);
    single.set(bytes.subarray(best.offset, best.offset + best.size), 22);

    return single;
}
