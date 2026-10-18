// Byte-level helpers shared by the container parsers, the metadata writers and the encoders.

// Decoded directly rather than through a data: URL, which the host's wrapped fetch rejects.
export async function base64ToBytes(base64) {
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

export function bytesToBase64(bytes) {
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

export function concatBytes(parts) {
    const total = parts.reduce((sum, part) => sum + part.length, 0);
    const result = new Uint8Array(total);
    let offset = 0;
    for (const part of parts) {
        result.set(part, offset);
        offset += part.length;
    }

    return result;
}

export function ascii(bytes, start, length) {
    let text = '';
    for (let i = start; i < start + length && i < bytes.length; i++) {
        text += String.fromCharCode(bytes[i]);
    }

    return text;
}

export function asciiBytes(text) {
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i++) {
        bytes[i] = text.charCodeAt(i) & 0xff;
    }

    return bytes;
}

export function startsWith(bytes, offset, text) {
    return ascii(bytes, offset, text.length) === text;
}

export function indexOfByte(bytes, value, from) {
    for (let i = from; i < bytes.length; i++) {
        if (bytes[i] === value) {
            return i;
        }
    }

    return -1;
}

export function viewOf(bytes) {
    return new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
}

export function bytesEqual(a, b) {
    if (a.length !== b.length) {
        return false;
    }
    for (let i = 0; i < a.length; i++) {
        if (a[i] !== b[i]) {
            return false;
        }
    }

    return true;
}

const utf8Decoder = new TextDecoder('utf-8');
const utf8Encoder = new TextEncoder();

export function decodeUtf8(bytes) {
    return utf8Decoder.decode(bytes);
}

export function encodeUtf8(text) {
    return utf8Encoder.encode(text);
}

async function pipeBytes(bytes, transform) {
    const stream = new Blob([bytes]).stream().pipeThrough(transform);
    return new Uint8Array(await new Response(stream).arrayBuffer());
}

// zlib streams, as PNG's iCCP, zTXt and compressed iTXt chunks carry them.
export function inflate(bytes) {
    return pipeBytes(bytes, new DecompressionStream('deflate'));
}

export function deflate(bytes) {
    return pipeBytes(bytes, new CompressionStream('deflate'));
}

let crcTable = null;

export function crc32(bytes, start = 0, end = bytes.length) {
    if (!crcTable) {
        crcTable = new Uint32Array(256);
        for (let n = 0; n < 256; n++) {
            let c = n;
            for (let k = 0; k < 8; k++) {
                c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
            }
            crcTable[n] = c >>> 0;
        }
    }

    let crc = 0xffffffff;
    for (let i = start; i < end; i++) {
        crc = crcTable[(crc ^ bytes[i]) & 0xff] ^ (crc >>> 8);
    }

    return (crc ^ 0xffffffff) >>> 0;
}

// The size of the bytes a base64 string encodes, without decoding it.
export function base64ByteLength(base64) {
    if (!base64) {
        return 0;
    }

    const padding = base64.endsWith('==') ? 2 : base64.endsWith('=') ? 1 : 0;
    return Math.floor((base64.length * 3) / 4) - padding;
}
