// A single-frame GIF89a encoder: palette selection, LZW compression and the file structure.
//
// The palette is exact when the image has few enough colours (sprites and icons usually do). Otherwise it is
// chosen by median cut over a 6-bit-per-channel histogram, and each pixel takes its nearest palette colour.
// Fully transparent pixels become the transparent index. Partially transparent pixels are flattened onto the
// background colour, since a GIF pixel is either opaque or not there.

const MAX_COLOURS = 256;

export function encodeGif(imageData, { background = [255, 255, 255], comments = [] } = {}) {
    const { width, height, data } = imageData;
    const pixelCount = width * height;

    // Flatten to opaque RGB, noting which pixels are fully transparent.
    const rgb = new Uint32Array(pixelCount);
    const transparent = new Uint8Array(pixelCount);
    let hasTransparency = false;
    for (let i = 0; i < pixelCount; i++) {
        const a = data[i * 4 + 3];
        if (a === 0) {
            transparent[i] = 1;
            hasTransparency = true;
            continue;
        }

        let r = data[i * 4];
        let g = data[i * 4 + 1];
        let b = data[i * 4 + 2];
        if (a < 255) {
            const k = a / 255;
            r = Math.round(r * k + background[0] * (1 - k));
            g = Math.round(g * k + background[1] * (1 - k));
            b = Math.round(b * k + background[2] * (1 - k));
        }
        rgb[i] = (r << 16) | (g << 8) | b;
    }

    const limit = hasTransparency ? MAX_COLOURS - 1 : MAX_COLOURS;
    const { palette, indexOf } = exactPalette(rgb, transparent, limit) || medianCutPalette(rgb, transparent, limit);

    const transparentIndex = hasTransparency ? palette.length : -1;
    const colourCount = palette.length + (hasTransparency ? 1 : 0);
    const bits = Math.max(1, Math.ceil(Math.log2(Math.max(2, colourCount))));

    const indices = new Uint8Array(pixelCount);
    for (let i = 0; i < pixelCount; i++) {
        indices[i] = transparent[i] ? transparentIndex : indexOf(rgb[i]);
    }

    const out = new ByteWriter();
    out.ascii('GIF89a');
    out.u16(width);
    out.u16(height);
    out.byte(0x80 | ((bits - 1) << 4) | (bits - 1));
    out.byte(0);
    out.byte(0);

    for (let i = 0; i < 1 << bits; i++) {
        const colour = i < palette.length ? palette[i] : 0;
        out.byte((colour >> 16) & 0xff);
        out.byte((colour >> 8) & 0xff);
        out.byte(colour & 0xff);
    }

    for (const comment of comments) {
        out.byte(0x21);
        out.byte(0xfe);
        out.subBlocks(comment);
    }

    if (hasTransparency) {
        out.bytes([0x21, 0xf9, 0x04, 0x01, 0x00, 0x00, transparentIndex, 0x00]);
    }

    out.byte(0x2c);
    out.u16(0);
    out.u16(0);
    out.u16(width);
    out.u16(height);
    out.byte(0);

    const minCodeSize = Math.max(2, bits);
    out.byte(minCodeSize);
    out.subBlocks(lzwEncode(indices, minCodeSize));
    out.byte(0x3b);

    return out.result();
}

function exactPalette(rgb, transparent, limit) {
    const lookup = new Map();
    const palette = [];
    for (let i = 0; i < rgb.length; i++) {
        if (transparent[i] || lookup.has(rgb[i])) {
            continue;
        }
        if (palette.length === limit) {
            return null;
        }
        lookup.set(rgb[i], palette.length);
        palette.push(rgb[i]);
    }

    if (palette.length === 0) {
        palette.push(0);
    }

    return { palette, indexOf: (colour) => lookup.get(colour) ?? 0 };
}

function bucketOf(colour) {
    return (((colour >> 18) & 0x3f) << 12) | (((colour >> 10) & 0x3f) << 6) | ((colour >> 2) & 0x3f);
}

function medianCutPalette(rgb, transparent, limit) {
    const counts = new Uint32Array(1 << 18);
    const sums = new Float64Array((1 << 18) * 3);
    for (let i = 0; i < rgb.length; i++) {
        if (transparent[i]) {
            continue;
        }
        const colour = rgb[i];
        const bucket = bucketOf(colour);
        counts[bucket]++;
        sums[bucket * 3] += (colour >> 16) & 0xff;
        sums[bucket * 3 + 1] += (colour >> 8) & 0xff;
        sums[bucket * 3 + 2] += colour & 0xff;
    }

    const buckets = [];
    for (let bucket = 0; bucket < counts.length; bucket++) {
        if (counts[bucket] > 0) {
            buckets.push(bucket);
        }
    }

    const channel = (bucket, c) => (bucket >> (12 - c * 6)) & 0x3f;

    const makeBox = (items) => {
        const min = [63, 63, 63];
        const max = [0, 0, 0];
        let total = 0;
        for (const bucket of items) {
            for (let c = 0; c < 3; c++) {
                const value = channel(bucket, c);
                min[c] = Math.min(min[c], value);
                max[c] = Math.max(max[c], value);
            }
            total += counts[bucket];
        }
        const ranges = [max[0] - min[0], max[1] - min[1], max[2] - min[2]];
        const axis = ranges.indexOf(Math.max(...ranges));
        return { items, axis, range: ranges[axis], total };
    };

    const boxes = [makeBox(buckets)];
    while (boxes.length < limit) {
        // Split the box with the most pixels spread over the widest range.
        let best = -1;
        let bestScore = 0;
        boxes.forEach((box, index) => {
            const score = box.range * box.total;
            if (box.items.length > 1 && score > bestScore) {
                best = index;
                bestScore = score;
            }
        });
        if (best < 0) {
            break;
        }

        const box = boxes[best];
        box.items.sort((a, b) => channel(a, box.axis) - channel(b, box.axis));

        let running = 0;
        let split = 1;
        for (let i = 0; i < box.items.length - 1; i++) {
            running += counts[box.items[i]];
            if (running >= box.total / 2) {
                split = i + 1;
                break;
            }
            split = i + 1;
        }

        boxes.splice(best, 1, makeBox(box.items.slice(0, split)), makeBox(box.items.slice(split)));
    }

    const palette = boxes.map((box) => {
        let r = 0;
        let g = 0;
        let b = 0;
        for (const bucket of box.items) {
            r += sums[bucket * 3];
            g += sums[bucket * 3 + 1];
            b += sums[bucket * 3 + 2];
        }
        return (Math.round(r / box.total) << 16) | (Math.round(g / box.total) << 8) | Math.round(b / box.total);
    });

    if (palette.length === 0) {
        palette.push(0);
    }

    const nearest = new Int16Array(1 << 18).fill(-1);
    const indexOf = (colour) => {
        const bucket = bucketOf(colour);
        if (nearest[bucket] >= 0) {
            return nearest[bucket];
        }

        const r = (colour >> 16) & 0xff;
        const g = (colour >> 8) & 0xff;
        const b = colour & 0xff;
        let best = 0;
        let bestDistance = Infinity;
        for (let i = 0; i < palette.length; i++) {
            const dr = ((palette[i] >> 16) & 0xff) - r;
            const dg = ((palette[i] >> 8) & 0xff) - g;
            const db = (palette[i] & 0xff) - b;
            const distance = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
            if (distance < bestDistance) {
                bestDistance = distance;
                best = i;
            }
        }
        nearest[bucket] = best;
        return best;
    };

    return { palette, indexOf };
}

// Variable-length LZW as GIF specifies it, with a clear code emitted whenever the 4096-entry table fills.
function lzwEncode(indices, minCodeSize) {
    const clearCode = 1 << minCodeSize;
    const endCode = clearCode + 1;

    // Table keyed by (prefix code << 8 | next index). Entries are valid only in the current generation, so a
    // clear is a counter bump rather than a refill.
    const codes = new Int32Array(4096 * 256);
    const generations = new Int32Array(4096 * 256);
    let generation = 1;

    const out = new ByteWriter();
    let accumulator = 0;
    let accumulatorBits = 0;
    let codeSize = minCodeSize + 1;
    let nextCode = endCode + 1;

    const emit = (code) => {
        accumulator |= code << accumulatorBits;
        accumulatorBits += codeSize;
        while (accumulatorBits >= 8) {
            out.byte(accumulator & 0xff);
            accumulator >>>= 8;
            accumulatorBits -= 8;
        }
    };

    emit(clearCode);
    if (indices.length === 0) {
        emit(endCode);
        if (accumulatorBits > 0) {
            out.byte(accumulator & 0xff);
        }
        return out.result();
    }

    let prefix = indices[0];
    for (let i = 1; i < indices.length; i++) {
        const k = indices[i];
        const key = (prefix << 8) | k;
        if (generations[key] === generation) {
            prefix = codes[key];
            continue;
        }

        emit(prefix);
        if (nextCode === 4096) {
            emit(clearCode);
            generation++;
            nextCode = endCode + 1;
            codeSize = minCodeSize + 1;
        } else {
            if (nextCode >= 1 << codeSize) {
                codeSize++;
            }
            codes[key] = nextCode++;
            generations[key] = generation;
        }
        prefix = k;
    }

    emit(prefix);
    emit(endCode);
    if (accumulatorBits > 0) {
        out.byte(accumulator & 0xff);
    }

    return out.result();
}

class ByteWriter {
    #chunks = [];
    #current = new Uint8Array(65536);
    #length = 0;

    byte(value) {
        if (this.#length === this.#current.length) {
            this.#chunks.push(this.#current);
            this.#current = new Uint8Array(65536);
            this.#length = 0;
        }
        this.#current[this.#length++] = value;
    }

    bytes(values) {
        for (const value of values) {
            this.byte(value);
        }
    }

    u16(value) {
        this.byte(value & 0xff);
        this.byte((value >> 8) & 0xff);
    }

    ascii(text) {
        for (let i = 0; i < text.length; i++) {
            this.byte(text.charCodeAt(i));
        }
    }

    subBlocks(data) {
        for (let i = 0; i < data.length; i += 255) {
            const size = Math.min(255, data.length - i);
            this.byte(size);
            for (let j = 0; j < size; j++) {
                this.byte(data[i + j]);
            }
        }
        this.byte(0);
    }

    result() {
        const chunks = [...this.#chunks, this.#current.subarray(0, this.#length)];
        const total = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
        const result = new Uint8Array(total);
        let offset = 0;
        for (const chunk of chunks) {
            result.set(chunk, offset);
            offset += chunk.length;
        }
        return result;
    }
}
