// @vitest-environment jsdom

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { ViewAPI } from '../api/view-api.js';

// Stands in for the per-view state store the host pushes snapshots into.
function createViewState(snapshot = {}) {
    return { current: snapshot };
}

// An element whose box the test drives, since jsdom lays nothing out and reports every box as zero. One
// entry per sample, the last one held for every sample after it. The settle loop reads the width once per
// sample, so counting those reads counts the samples it took.
function createMeasuredElement(sizes) {
    const element = document.createElement('div');
    let readCount = 0;

    function sizeAt(index) {
        return sizes[Math.min(index, sizes.length - 1)];
    }

    Object.defineProperty(element, 'clientWidth', {
        get() {
            const width = sizeAt(readCount).width;
            readCount += 1;

            return width;
        },
    });
    Object.defineProperty(element, 'clientHeight', {
        get() {
            return sizeAt(Math.max(readCount - 1, 0)).height;
        },
    });

    return {
        element,
        get readCount() {
            return readCount;
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

let originalRequestAnimationFrame;

beforeEach(() => {
    setPageHidden(false);

    // jsdom's animation frames run on its own clock, which is slower than the settle loop needs and is not
    // driven at all while the page is hidden.
    originalRequestAnimationFrame = globalThis.requestAnimationFrame;
    globalThis.requestAnimationFrame = (callback) => setTimeout(() => callback(0), 0);
});

afterEach(() => {
    globalThis.requestAnimationFrame = originalRequestAnimationFrame;
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

        viewState.current = { isSized: 'true' };

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
});

describe('waitForStableSize', () => {
    it('returns immediately when no size is coming', async () => {
        setPageHidden(true);
        const view = new ViewAPI(createViewState({ canSizeUnarranged: 'false' }));
        const measured = createMeasuredElement(STABLE_SIZE);

        await view.waitForStableSize(measured.element, { timeoutMs: 5000 });

        expect(measured.readCount).toBe(0);
    });

    it('resolves once two samples agree', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const measured = createMeasuredElement(STABLE_SIZE);

        await view.waitForStableSize(measured.element, { timeoutMs: 5000 });

        expect(measured.readCount).toBe(2);
    });

    it('keeps sampling while the box is still changing', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        const measured = createMeasuredElement([
            { width: 200, height: 600 },
            { width: 500, height: 600 },
            { width: 800, height: 600 },
        ]);

        await view.waitForStableSize(measured.element, { timeoutMs: 5000 });

        expect(measured.readCount).toBe(4);
    });

    it('gives up on a size that never settles', async () => {
        const view = new ViewAPI(createViewState({ isSized: 'true' }));
        let width = 100;
        const element = document.createElement('div');
        Object.defineProperty(element, 'clientWidth', {
            get() {
                width += 10;
                return width;
            },
        });
        Object.defineProperty(element, 'clientHeight', { get: () => 600 });

        const start = Date.now();
        await view.waitForStableSize(element, { timeoutMs: 30 });

        expect(Date.now() - start).toBeGreaterThanOrEqual(25);
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

        const start = Date.now();
        await view.waitForStableSize(element, { timeoutMs: 5000 });

        expect(Date.now() - start).toBeLessThan(1000);
    });

    it('waits for a hidden page the host can still size', async () => {
        setPageHidden(true);
        const view = new ViewAPI(createViewState({ isSized: 'true', canSizeUnarranged: 'true' }));
        const measured = createMeasuredElement(STABLE_SIZE);

        await view.waitForStableSize(measured.element, { timeoutMs: 5000 });

        expect(measured.readCount).toBe(2);
    });
});
