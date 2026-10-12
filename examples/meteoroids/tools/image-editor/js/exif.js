// EXIF (a TIFF structure) and XMP adjustments. Edits are made in place wherever possible, so offsets inside
// the block (maker notes in particular hold absolute ones) stay valid and everything the editor does not
// touch is copied byte for byte.

import { decodeUtf8, encodeUtf8, viewOf } from './bytes.js';

const TAG_ORIENTATION = 0x0112;
const TAG_EXIF_IFD = 0x8769;
const TAG_GPS_IFD = 0x8825;
const TAG_INTEROP_IFD = 0xa005;
const TAG_PIXEL_X = 0xa002;
const TAG_PIXEL_Y = 0xa003;
const TAG_THUMBNAIL_OFFSET = 0x0201;
const TAG_THUMBNAIL_LENGTH = 0x0202;

const TYPE_SHORT = 3;
const TYPE_LONG = 4;
const TYPE_SIZES = { 1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 6: 1, 7: 1, 8: 2, 9: 4, 10: 8, 11: 4, 12: 8, 13: 4 };

function parseTiff(bytes) {
    if (bytes.length < 8) {
        throw new Error('EXIF block is too short.');
    }

    const order = String.fromCharCode(bytes[0], bytes[1]);
    if (order !== 'II' && order !== 'MM') {
        throw new Error('EXIF block has no byte order mark.');
    }

    const little = order === 'II';
    const view = viewOf(bytes);
    const u16 = (offset) => view.getUint16(offset, little);
    const u32 = (offset) => view.getUint32(offset, little);

    if (u16(2) !== 42) {
        throw new Error('EXIF block is not a TIFF structure.');
    }

    // Every byte range the structure references, so the thumbnail can be cut off the end when nothing else
    // lives beyond it.
    const extents = [];
    const visited = new Set();

    function readIfd(offset, owner) {
        if (visited.has(offset)) {
            throw new Error('EXIF IFDs form a loop.');
        }
        visited.add(offset);

        if (offset < 8 || offset + 2 > bytes.length) {
            throw new Error('EXIF IFD offset is out of range.');
        }

        const count = u16(offset);
        const end = offset + 2 + count * 12 + 4;
        if (end > bytes.length) {
            throw new Error('EXIF IFD runs past the end of the block.');
        }

        extents.push({ start: offset, end, owner });

        const entries = [];
        for (let i = 0; i < count; i++) {
            const at = offset + 2 + i * 12;
            const type = u16(at + 2);
            const valueCount = u32(at + 4);
            const size = (TYPE_SIZES[type] || 0) * valueCount;
            const entry = { tag: u16(at), type, count: valueCount, at };

            if (size > 4) {
                const valueOffset = u32(at + 8);
                if (valueOffset + size > bytes.length) {
                    throw new Error('EXIF value runs past the end of the block.');
                }
                extents.push({ start: valueOffset, end: valueOffset + size, owner });
            }

            entries.push(entry);
        }

        return { offset, entries, next: u32(offset + 2 + count * 12), nextAt: offset + 2 + count * 12 };
    }

    const find = (ifd, tag) => ifd?.entries.find((entry) => entry.tag === tag) || null;
    const pointer = (entry) => (entry ? u32(entry.at + 8) : 0);

    const ifd0 = readIfd(u32(4), 'main');
    const exifIfd = find(ifd0, TAG_EXIF_IFD) ? readIfd(pointer(find(ifd0, TAG_EXIF_IFD)), 'main') : null;
    if (find(ifd0, TAG_GPS_IFD)) {
        readIfd(pointer(find(ifd0, TAG_GPS_IFD)), 'main');
    }
    if (find(exifIfd, TAG_INTEROP_IFD)) {
        readIfd(pointer(find(exifIfd, TAG_INTEROP_IFD)), 'main');
    }

    let ifd1 = null;
    if (ifd0.next !== 0) {
        ifd1 = readIfd(ifd0.next, 'thumbnail');
        const offset = find(ifd1, TAG_THUMBNAIL_OFFSET);
        const length = find(ifd1, TAG_THUMBNAIL_LENGTH);
        if (offset && length) {
            const start = pointer(offset);
            const size = length.type === TYPE_SHORT ? u16(length.at + 8) : u32(length.at + 8);
            if (start + size > bytes.length) {
                throw new Error('EXIF thumbnail runs past the end of the block.');
            }
            extents.push({ start, end: start + size, owner: 'thumbnail' });
        }
    }

    return { little, view, ifd0, exifIfd, ifd1, extents, find, u16, u32 };
}

// Throws when the block cannot be walked, which is what makes it unadjustable.
export function validateExif(bytes) {
    parseTiff(bytes);
}

export function exifHasThumbnail(bytes) {
    try {
        return parseTiff(bytes).ifd1 !== null;
    } catch {
        return false;
    }
}

function writeInteger(tiff, entry, value) {
    const { view, little } = tiff;
    if (entry.type === TYPE_SHORT && value <= 0xffff) {
        view.setUint16(entry.at + 8, value, little);
        return;
    }

    // A dimension too large for a SHORT becomes a LONG, which still fits the entry's inline value field.
    view.setUint16(entry.at + 2, TYPE_LONG, little);
    view.setUint32(entry.at + 4, 1, little);
    view.setUint32(entry.at + 8, value, little);
}

// Orientation to normal, pixel dimensions to the saved size, and the thumbnail removed when it would no
// longer match the image. Throws for a block that cannot be walked.
export function adjustExif(bytes, { width, height, dropThumbnail }) {
    const copy = bytes.slice();
    const tiff = parseTiff(copy);

    const orientation = tiff.find(tiff.ifd0, TAG_ORIENTATION);
    if (orientation && orientation.type === TYPE_SHORT) {
        tiff.view.setUint16(orientation.at + 8, 1, tiff.little);
    }

    const pixelX = tiff.find(tiff.exifIfd, TAG_PIXEL_X);
    if (pixelX) {
        writeInteger(tiff, pixelX, width);
    }
    const pixelY = tiff.find(tiff.exifIfd, TAG_PIXEL_Y);
    if (pixelY) {
        writeInteger(tiff, pixelY, height);
    }

    if (!dropThumbnail || !tiff.ifd1) {
        return copy;
    }

    tiff.view.setUint32(tiff.ifd0.nextAt, 0, tiff.little);

    // The thumbnail usually sits at the end of the block. When it does, cut it off. When anything else lives
    // after it, unlinking it is enough.
    const mainEnd = Math.max(8, ...tiff.extents.filter((e) => e.owner === 'main').map((e) => e.end));
    const thumbnailStart = Math.min(...tiff.extents.filter((e) => e.owner === 'thumbnail').map((e) => e.start));
    if (thumbnailStart >= mainEnd) {
        return copy.slice(0, mainEnd);
    }

    return copy;
}

function setXmpValue(xmp, name, value) {
    const attribute = new RegExp(`(${name}\\s*=\\s*)(["'])[^"']*\\2`, 'g');
    const element = new RegExp(`(<${name}>)[^<]*(</${name}>)`, 'g');

    return xmp.replace(attribute, `$1$2${value}$2`).replace(element, `$1${value}$2`);
}

// The same facts in the XMP packet, where it states them. The rest of the packet is untouched.
export function adjustXmp(bytes, { width, height }) {
    const original = decodeUtf8(bytes);
    let xmp = setXmpValue(original, 'tiff:Orientation', 1);
    xmp = setXmpValue(xmp, 'exif:PixelXDimension', width);
    xmp = setXmpValue(xmp, 'exif:PixelYDimension', height);

    return xmp === original ? bytes : encodeUtf8(xmp);
}
