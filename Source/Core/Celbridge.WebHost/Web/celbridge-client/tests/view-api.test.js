// @vitest-environment jsdom

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { ViewAPI } from '../api/view-api.js';

// Stands in for the per-view state store the host pushes snapshots into. Registering a handler replays the
// current snapshot, as the real store does once one has arrived.
function createViewState(snapshot = {}) {
    const handlers = [];

    return {
        current: snapshot,
        onChanged(handler) {
            handlers.push(handler);
            handler(this.current);
        },
        push(next) {
            this.current = next;
            for (const handler of handlers) {
                handler(next);
            }
        },
    };
}

// An element whose box the test drives, since jsdom lays nothing out and reports every box as zero. One
// entry per sample, the last one held for every sample after it. The settle loop reads the width and then
// the height once each per sample, so advancing on the height read keeps both dimensions of a sample on the
// same entry and counts the samples taken.
function createMeasuredElement(sizes) {
    const element = document.createElement('div');
    let sampleCount = 0;

    function sizeAt(index) {
        return sizes[Math.min(index, sizes.length - 1)];
    }

    Object.defineProperty(element, 'clientWidth', {
        get() {
            return sizeAt(sampleCount).width;
        },
    });
    Object.defineProperty(element, 'clientHeight', {
        get() {
            const height = sizeAt(sampleCount).height;
            sampleCount += 1;

            return height;
        },
    });

    return {
        element,
        get sampleCount() {
            return sampleCount;
        },
    };
}

// A box that never settles, for the give-up cases.
function createGrowingElement() {
    const element = document.createElement('div');
    let sampleCount = 0;

    Object.defineProperty(element, 'clientWidth', {
        get() {
            return 800 + sampleCount * 10;
        },
    });
    Object.defineProperty(element, 'clientHeight', {
        get() {
            sampleCount += 1;

            return 600;
        },
    });

    return {
        element,
        get sampleCount() {
            return sampleCount;
        },
    };
}

function setPageHidden(hidden) {
    Object.defineProperty(document, 'hidden', {
        configurable: true,
        get: () => hidden,
    });
}

// The console's terminal box: one xterm cell grid, held at a size the host reported.
const STABLE_SIZE = [
    { width: 800, height: 600 },
];

// Long enough to outlast a sampling interval, short enough to keep the suite quick.
const GIVE_UP_MS = 60;

let originalHidden;
let originalRequestAnimationFrame;

beforeEach(() => {
    originalHidden = Object.getOwnPropertyDescriptor(Document.prototype, 'hidden');
    setPageHidden(false);

    // jsdom's animation frames run on its own clock, which is slower than the settle loop needs and is not
    // driven at all while the page is hidden.
    originalRequestAnimationFrame = globalThis.requestAnimationFrame;
    globalThis.requestAnimationFrame = (callback) => setTimeout(() => callback(0), 0);
});

afterEach(() => {
    globalThis.requestAnimationFrame = originalRequestAnimationFrame;
    delete document.hidden;
    if (originalHidden) {
        Object.defineProperty(Document.prototype, 'hidden', originalHidden);
    }
});

describe('isSized', () => {
    it('is false before the host has reported a size', () => {
        const view = new ViewAPI(createViewState());

        expect(view.isSized).toBe(false);
    });

    it('follows the host state key', () => {
        const viewState = createViewState({ isSized: 'false' });
        const view = new ViewAPI(viewState);

        expect(view.isSized).toBe(false);

        viewState.push({ isSized: 'true' });

        expect(view.isSized).toBe(true);
    });
});

describe('sizeUnavailable', () => {
    it('is false while the page is on screen', () => {
        const view = new ViewAPI(createViewState());

        expect(view.sizeUnavailable).toBe(false);
    });

    it('is true for a hidden page the host cannot size', () => {
        setPageHidden(true);
        const view = new ViewAPI(createViewState({ canSizeUnarranged: 'false' }));

        expect(view.sizeUnavailable).toBe(true);
    });

    it('is false for a hidden page the host can size', () => {
        setPageHidden(true);
        const view = new ViewAPI(createViewState({ canSizeUnarranged: 'true' }));

        expect(view.sizeUnavailable).toBe(false);
    });
});

describe('onChanged', () => {
    it('reports the state the host has already pushed, then every change', () => {
        const viewState = createViewState({ isSized: 'false' });
        const view = new ViewAPI(viewState);
        const reported = [];

        view.onChanged(() => reported.push(view.isSized));
        viewState.push({ isSized: 'true' });

        expect(reported).toEqual([false, true]);
    });
});

describe('canMeasure', () => {
    it('refuses a box the host has not reported a size for', () => {
        const view = new ViewAPI(createViewState());
        const measured = createMeasuredElement(STABLE_SIZE);

        expect(view.canMeasure(measured.element)).toBe(false);
    });

    it('accepts a box on a surface the host has reported sized', () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const measured = createMeasuredElement(STABLE_SIZE);

        expect(view.canMeasure(measured.element)).toBe(true);
    });

    it('refuses an element with no box', () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const measured = createMeasuredElement([{ width: 0, height: 0 }]);

        expect(view.canMeasure(measured.element)).toBe(false);
    });

    it('refuses a box with a width but no height', () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const measured = createMeasuredElement([{ width: 800, height: 0 }]);

        expect(view.canMeasure(measured.element)).toBe(false);
    });

    it('refuses a missing element rather than throwing', () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));

        expect(view.canMeasure(null)).toBe(false);
    });
});

describe('waitForStableSize', () => {
    it('returns without waiting out a sample when no size is coming', async () => {
        setPageHidden(true);
        const view = new ViewAPI(createViewState({ canSizeUnarranged: 'false' }));
        const measured = createMeasuredElement(STABLE_SIZE);

        // Racing a macrotask pins the immediacy: a caller that took even one sampling interval here would
        // hold a hidden console's launch open for a size that is never arriving.
        const outcome = await Promise.race([
            view.waitForStableSize(measured.element, { timeoutMs: 5000 }).then(() => 'returned'),
            new Promise((resolve) => setTimeout(() => resolve('sampled'), 0)),
        ]);

        expect(outcome).toBe('returned');
        expect(measured.sampleCount).toBe(0);
    });

    it('resolves once two samples agree', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const measured = createMeasuredElement(STABLE_SIZE);

        await view.waitForStableSize(measured.element, { timeoutMs: 5000 });

        expect(measured.sampleCount).toBe(2);
    });

    it('keeps sampling while the width is still changing', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const measured = createMeasuredElement([
            { width: 200, height: 600 },
            { width: 500, height: 600 },
            { width: 800, height: 600 },
        ]);

        await view.waitForStableSize(measured.element, { timeoutMs: 5000 });

        expect(measured.sampleCount).toBe(4);
    });

    it('keeps sampling while only the height is still changing', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const measured = createMeasuredElement([
            { width: 800, height: 100 },
            { width: 800, height: 300 },
            { width: 800, height: 600 },
        ]);

        await view.waitForStableSize(measured.element, { timeoutMs: 5000 });

        expect(measured.sampleCount).toBe(4);
    });

    it('does not settle on a surface the host has not reported sized', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'false' }));
        const measured = createMeasuredElement(STABLE_SIZE);

        const started = Date.now();
        await view.waitForStableSize(measured.element, { timeoutMs: GIVE_UP_MS });

        // A stable placeholder box is exactly what an unarranged surface reports, so settling on it would
        // hand the caller the size the host has not vouched for.
        expect(Date.now() - started).toBeGreaterThanOrEqual(GIVE_UP_MS - 5);
        expect(measured.sampleCount).toBeGreaterThan(1);
    });

    it('does not settle on a box with no height', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const measured = createMeasuredElement([{ width: 800, height: 0 }]);

        const started = Date.now();
        await view.waitForStableSize(measured.element, { timeoutMs: GIVE_UP_MS });

        expect(Date.now() - started).toBeGreaterThanOrEqual(GIVE_UP_MS - 5);
    });

    it('gives up on a size that never settles, and stops sampling', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const growing = createGrowingElement();

        const started = Date.now();
        await view.waitForStableSize(growing.element, { timeoutMs: GIVE_UP_MS });
        const elapsed = Date.now() - started;
        const sampledWhenAbandoned = growing.sampleCount;

        await new Promise((resolve) => setTimeout(resolve, GIVE_UP_MS * 2));

        expect(elapsed).toBeGreaterThanOrEqual(GIVE_UP_MS - 5);
        expect(growing.sampleCount).toBe(sampledWhenAbandoned);
    });

    it('gives up rather than throwing for a missing element', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));

        await expect(view.waitForStableSize(null, { timeoutMs: GIVE_UP_MS })).resolves.toBeUndefined();
    });

    it('stops waiting when the page goes off screen mid-wait', async () => {
        const viewState = createViewState({ isSized: 'true', canSizeUnarranged: 'false' });
        const view = new ViewAPI(viewState);
        const element = document.createElement('div');
        Object.defineProperty(element, 'clientWidth', {
            get() {
                setPageHidden(true);
                return 0;
            },
        });
        Object.defineProperty(element, 'clientHeight', { get: () => 0 });

        const started = Date.now();
        await view.waitForStableSize(element, { timeoutMs: 5000 });

        expect(Date.now() - started).toBeLessThan(1000);
    });

    it('keeps sampling a hidden page the host can still size', async () => {
        setPageHidden(true);
        const view = new ViewAPI(createViewState({ isSized: 'true', canSizeUnarranged: 'true' }));
        const measured = createMeasuredElement(STABLE_SIZE);

        await view.waitForStableSize(measured.element, { timeoutMs: 5000 });

        expect(measured.sampleCount).toBe(2);
    });

    it('settles when a requested frame is never serviced', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const measured = createMeasuredElement(STABLE_SIZE);

        // What a page hidden after its frame was requested sees, and what an occluded surface sees for as
        // long as the platform withholds frames: the callback is parked, so only the sample's own timer can
        // release the wait.
        const parkedFrames = [];
        globalThis.requestAnimationFrame = (callback) => parkedFrames.push(callback);

        const started = Date.now();
        await view.waitForStableSize(measured.element, { timeoutMs: 800 });

        expect(measured.sampleCount).toBe(2);
        expect(parkedFrames.length).toBeGreaterThan(0);
        expect(Date.now() - started).toBeLessThan(500);
    });
});
