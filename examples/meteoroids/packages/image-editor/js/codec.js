// Decoding files into bitmaps and encoding canvases back into file bytes. PNG, JPEG and WebP use the
// browser's encoders; GIF and BMP are written here, since the browser has no encoder for either.

import { encodeGif } from '../lib/gif-encoder.js';
import { FORMATS } from './formats.js';
import { viewOf } from './bytes.js';

// The source bitmap keeps the file's own pixel values (no colour-space conversion), so the profile carried
// through a save still describes them. The preview decodes a second, colour-managed bitmap when the file
// declares a colour space. EXIF orientation is applied either way, so photos come in upright.
export function decodeBitmap(bytes, mime, { colourManaged }) {
    return createImageBitmap(new Blob([bytes], { type: mime }), {
        colorSpaceConversion: colourManaged ? 'default' : 'none',
        imageOrientation: 'from-image',
    });
}

export function newCanvas(width, height) {
    return new OffscreenCanvas(width, height);
}

// Reads pixels in horizontal strips, so a very large image never needs one allocation for the whole of it.
function forEachStrip(canvas, visit) {
    const context = canvas.getContext('2d', { willReadFrequently: true });
    const stripHeight = Math.max(1, Math.floor(4_000_000 / Math.max(1, canvas.width)));
    for (let y = 0; y < canvas.height; y += stripHeight) {
        const height = Math.min(stripHeight, canvas.height - y);
        if (visit(context.getImageData(0, y, canvas.width, height).data) === false) {
            return;
        }
    }
}

// 'none', 'binary' (some pixels fully transparent, the rest opaque) or 'partial'.
export function transparencyOf(canvas) {
    let result = 'none';
    forEachStrip(canvas, (data) => {
        for (let i = 3; i < data.length; i += 4) {
            const a = data[i];
            if (a === 255) {
                continue;
            }
            if (a === 0) {
                result = 'binary';
            } else {
                result = 'partial';
                return false;
            }
        }
        return true;
    });

    return result;
}

function flattened(canvas, background) {
    const flat = newCanvas(canvas.width, canvas.height);
    const context = flat.getContext('2d');
    context.fillStyle = background;
    context.fillRect(0, 0, flat.width, flat.height);
    context.drawImage(canvas, 0, 0);
    return flat;
}

function pixelsOf(canvas) {
    return canvas.getContext('2d', { willReadFrequently: true }).getImageData(0, 0, canvas.width, canvas.height);
}

function hexToRgb(hex) {
    const value = parseInt(hex.slice(1), 16);
    return [(value >> 16) & 0xff, (value >> 8) & 0xff, value & 0xff];
}

// Encodes the canvas as the given format. `quality` is 1-100; `background` is a #rrggbb colour for pixels
// the format cannot keep transparent.
export async function encodeCanvas(canvas, format, { quality = 90, lossless = false, background = '#ffffff', comments = [], icc = null, resolution = null } = {}) {
    if (format === 'png') {
        return blobBytes(await canvas.convertToBlob({ type: FORMATS.png.mime }));
    }

    if (format === 'jpeg') {
        const blob = await flattened(canvas, background).convertToBlob({ type: FORMATS.jpeg.mime, quality: quality / 100 });
        return blobBytes(blob);
    }

    if (format === 'webp') {
        // The browser encodes WebP losslessly at quality 1, so lossy quality stops just short of it.
        const value = lossless ? 1 : Math.min(quality, 99) / 100;
        return blobBytes(await canvas.convertToBlob({ type: FORMATS.webp.mime, quality: value }));
    }

    if (format === 'gif') {
        return encodeGif(pixelsOf(canvas), { background: hexToRgb(background), comments });
    }

    if (format === 'bmp') {
        return encodeBmp(pixelsOf(canvas), { icc, resolution });
    }

    throw new Error(`Cannot encode ${format}.`);
}

async function blobBytes(blob) {
    return new Uint8Array(await blob.arrayBuffer());
}

const BMP_FILE_HEADER = 14;
const BMP_V5_HEADER = 124;
const BMP_SRGB = 0x73524742;
const BMP_PROFILE_EMBEDDED = 0x4d424544;
const BMP_DEFAULT_PPM = 2835;

// A 32-bit BGRA bitmap with a BITMAPV5HEADER, which carries the alpha mask and, when there is one, an embedded
// ICC profile after the pixel data.
function encodeBmp({ width, height, data }, { icc, resolution }) {
    const pixelBytes = width * height * 4;
    const profileBytes = icc ? icc.length : 0;
    const pixelOffset = BMP_FILE_HEADER + BMP_V5_HEADER;
    const bytes = new Uint8Array(pixelOffset + pixelBytes + profileBytes);
    const view = viewOf(bytes);

    bytes[0] = 0x42;
    bytes[1] = 0x4d;
    view.setUint32(2, bytes.length, true);
    view.setUint32(10, pixelOffset, true);

    const h = BMP_FILE_HEADER;
    const ppmX = resolution?.unit === 1 ? resolution.x : BMP_DEFAULT_PPM;
    const ppmY = resolution?.unit === 1 ? resolution.y : BMP_DEFAULT_PPM;
    view.setUint32(h, BMP_V5_HEADER, true);
    view.setInt32(h + 4, width, true);
    view.setInt32(h + 8, height, true);
    view.setUint16(h + 12, 1, true);
    view.setUint16(h + 14, 32, true);
    view.setUint32(h + 16, 3, true);
    view.setUint32(h + 20, pixelBytes, true);
    view.setInt32(h + 24, ppmX, true);
    view.setInt32(h + 28, ppmY, true);
    view.setUint32(h + 40, 0x00ff0000, true);
    view.setUint32(h + 44, 0x0000ff00, true);
    view.setUint32(h + 48, 0x000000ff, true);
    view.setUint32(h + 52, 0xff000000, true);
    view.setUint32(h + 56, icc ? BMP_PROFILE_EMBEDDED : BMP_SRGB, true);
    view.setUint32(h + 108, 4, true);
    if (icc) {
        view.setUint32(h + 112, BMP_V5_HEADER + pixelBytes, true);
        view.setUint32(h + 116, icc.length, true);
        bytes.set(icc, pixelOffset + pixelBytes);
    }

    // Rows run bottom-up.
    const rowBytes = width * 4;
    for (let y = 0; y < height; y++) {
        const source = (height - 1 - y) * rowBytes;
        const target = pixelOffset + y * rowBytes;
        for (let x = 0; x < rowBytes; x += 4) {
            bytes[target + x] = data[source + x + 2];
            bytes[target + x + 1] = data[source + x + 1];
            bytes[target + x + 2] = data[source + x];
            bytes[target + x + 3] = data[source + x + 3];
        }
    }

    return bytes;
}
