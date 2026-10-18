// The edit model. The document is the untouched source bitmap plus an ordered list of whole-image
// operations; the current image is the source with each one applied in turn. Undo and redo move a cursor
// along the list.

import { newCanvas } from './codec.js';

export function createHistory() {
    let operations = [];
    let cursor = 0;

    return {
        push(operation) {
            operations = operations.slice(0, cursor);
            operations.push(operation);
            cursor = operations.length;
        },
        undo() {
            if (cursor > 0) {
                cursor--;
                return true;
            }
            return false;
        },
        redo() {
            if (cursor < operations.length) {
                cursor++;
                return true;
            }
            return false;
        },
        canUndo: () => cursor > 0,
        canRedo: () => cursor < operations.length,
        applied: () => operations.slice(0, cursor),
        reset() {
            operations = [];
            cursor = 0;
        },
    };
}

// Rotations and flips as 2x2 matrices [a, b, c, d] acting on (x, y) with y pointing down.
const IDENTITY = [1, 0, 0, 1];
const ROTATE_RIGHT = [0, -1, 1, 0];
const FLIP_HORIZONTAL = [-1, 0, 0, 1];
const FLIP_VERTICAL = [1, 0, 0, -1];

function multiply(m, n) {
    return [
        m[0] * n[0] + m[1] * n[2], m[0] * n[1] + m[1] * n[3],
        m[2] * n[0] + m[3] * n[2], m[2] * n[1] + m[3] * n[3],
    ];
}

function sameMatrix(m, n) {
    return m.every((value, i) => value === n[i]);
}

function isOrientation(operation) {
    return operation.type === 'rotate' || operation.type === 'flip';
}

function matrixOf(operation) {
    if (operation.type === 'flip') {
        return operation.axis === 'horizontal' ? FLIP_HORIZONTAL : FLIP_VERTICAL;
    }

    let matrix = IDENTITY;
    for (let i = 0; i < operation.degrees / 90; i++) {
        matrix = multiply(ROTATE_RIGHT, matrix);
    }
    return matrix;
}

// The shortest equivalent of a run of rotations and flips: an optional horizontal flip followed by an
// optional rotation. A run that cancels out becomes nothing.
function canonicalOrientation(matrix) {
    for (const flip of [false, true]) {
        let candidate = flip ? FLIP_HORIZONTAL : IDENTITY;
        for (let quarter = 0; quarter < 4; quarter++) {
            if (sameMatrix(candidate, matrix)) {
                const operations = [];
                if (flip) {
                    operations.push({ type: 'flip', axis: 'horizontal' });
                }
                if (quarter > 0) {
                    operations.push({ type: 'rotate', degrees: quarter * 90 });
                }
                return operations;
            }
            candidate = multiply(ROTATE_RIGHT, candidate);
        }
    }

    return [];
}

// The operations with each run of rotations and flips merged, so an image turned back to its original
// geometry counts as having no edits. Undo still steps through the operations one at a time.
export function effectiveOperations(operations) {
    const result = [];
    let run = null;

    for (const operation of operations) {
        if (isOrientation(operation)) {
            run = multiply(matrixOf(operation), run || IDENTITY);
            continue;
        }
        if (run) {
            result.push(...canonicalOrientation(run));
            run = null;
        }
        result.push(operation);
    }
    if (run) {
        result.push(...canonicalOrientation(run));
    }

    return result;
}

export function sizeAfter(width, height, operations) {
    for (const operation of operations) {
        if (operation.type === 'crop' || operation.type === 'resize') {
            width = operation.width;
            height = operation.height;
        } else if (operation.type === 'rotate' && operation.degrees % 180 !== 0) {
            [width, height] = [height, width];
        }
    }

    return { width, height };
}

export function changesGeometry(operations) {
    return operations.some((operation) => operation.type !== 'resize');
}

function applyCrop(source, { x, y, width, height }) {
    const canvas = newCanvas(width, height);
    const context = canvas.getContext('2d');
    context.imageSmoothingEnabled = false;
    context.drawImage(source, x, y, width, height, 0, 0, width, height);
    return canvas;
}

function applyOrientation(source, operation) {
    const [a, b, c, d] = matrixOf(operation);
    const swap = a === 0;
    const width = swap ? source.height : source.width;
    const height = swap ? source.width : source.height;

    const canvas = newCanvas(width, height);
    const context = canvas.getContext('2d');
    context.imageSmoothingEnabled = false;
    // Maps the source centre onto the destination centre. setTransform takes (a, b, c, d) column-major.
    const e = width / 2 - (a * source.width / 2 + b * source.height / 2);
    const f = height / 2 - (c * source.width / 2 + d * source.height / 2);
    context.setTransform(a, c, b, d, e, f);
    context.drawImage(source, 0, 0);
    return canvas;
}

function drawScaled(source, width, height, smooth) {
    const canvas = newCanvas(width, height);
    const context = canvas.getContext('2d');
    context.imageSmoothingEnabled = smooth;
    context.imageSmoothingQuality = 'high';
    context.drawImage(source, 0, 0, width, height);
    return canvas;
}

function applyResize(source, { width, height, resampling }) {
    if (resampling === 'pixelated') {
        return drawScaled(source, width, height, false);
    }

    // Downscaling by more than 2x halves in steps first, so the result does not alias.
    let current = source;
    let w = source.width;
    let h = source.height;
    while (w > width * 2 || h > height * 2) {
        w = w > width * 2 ? Math.ceil(w / 2) : w;
        h = h > height * 2 ? Math.ceil(h / 2) : h;
        current = drawScaled(current, w, h, true);
    }

    return drawScaled(current, width, height, true);
}

export function applyOperation(source, operation) {
    switch (operation.type) {
        case 'crop':
            return applyCrop(source, operation);
        case 'resize':
            return applyResize(source, operation);
        case 'rotate':
        case 'flip':
            return applyOrientation(source, operation);
        default:
            throw new Error(`Unknown operation ${operation.type}.`);
    }
}

// Caches recent results by operation prefix, so an undo, a redo or one more operation reuses what was
// already computed rather than replaying the whole chain from the source.
export function createPipeline(source) {
    const MAX_ENTRIES = 6;
    const MAX_PIXELS = 160_000_000;
    const cache = new Map();

    const keyOf = (operations) => JSON.stringify(operations);

    function remember(key, canvas) {
        cache.delete(key);
        cache.set(key, canvas);
        let pixels = 0;
        for (const entry of cache.values()) {
            pixels += entry.width * entry.height;
        }
        for (const [oldest, entry] of cache) {
            if (cache.size <= 1 || (cache.size <= MAX_ENTRIES && pixels <= MAX_PIXELS)) {
                break;
            }
            pixels -= entry.width * entry.height;
            cache.delete(oldest);
        }
    }

    return {
        source,
        render(operations) {
            if (operations.length === 0) {
                return source;
            }

            let start = 0;
            let current = source;
            for (let length = operations.length; length > 0; length--) {
                const cached = cache.get(keyOf(operations.slice(0, length)));
                if (cached) {
                    start = length;
                    current = cached;
                    break;
                }
            }

            for (let i = start; i < operations.length; i++) {
                current = applyOperation(current, operations[i]);
            }
            if (start < operations.length) {
                remember(keyOf(operations), current);
            }

            return current;
        },
    };
}
