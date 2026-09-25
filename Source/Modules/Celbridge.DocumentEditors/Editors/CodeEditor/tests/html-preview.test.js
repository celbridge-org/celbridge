import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';

const pageUrl = '/project/docs/page.html';

// jsdom has no ResizeObserver, so a test fires the observer by hand when it changes the frame's size.
let resizeCallbacks = [];

class FakeResizeObserver {
    constructor(callback) {
        resizeCallbacks.push(callback);
    }

    observe() {}

    disconnect() {}
}

// A freshly loaded page: a scroll range of 800, scrolled to the top.
function createPage() {
    return {
        scrollingElement: {
            scrollHeight: 1000,
            clientHeight: 200,
            scrollTop: 0
        }
    };
}

// A stand-in for the preview frame. jsdom cannot load a page into a real one, and lays nothing out.
function createFrame() {
    const loadListeners = [];

    return {
        src: 'about:blank',
        clientHeight: 400,
        contentDocument: createPage(),
        addEventListener(type, listener) {
            if (type === 'load') {
                loadListeners.push(listener);
            }
        },
        // Replaces the document with a newly loaded page, as a navigation does, and fires the frame's load.
        loadPage() {
            this.contentDocument = createPage();
            loadListeners.forEach((listener) => listener());

            return this.contentDocument.scrollingElement;
        }
    };
}

describe('HTML preview module', () => {
    let previewModule;
    let frame;

    beforeEach(async () => {
        resizeCallbacks = [];
        vi.stubGlobal('ResizeObserver', FakeResizeObserver);

        // The module keeps its state at module level, so each test imports a fresh copy.
        vi.resetModules();
        previewModule = await import('../html-preview/preview-module.js');

        frame = createFrame();
        previewModule.initialize(frame);
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('navigates the frame to the file when refreshed', () => {
        previewModule.refresh(pageUrl);

        expect(frame.src).toBe(pageUrl);
    });

    it('keeps the reader\'s place across a reload', () => {
        previewModule.refresh(pageUrl);
        frame.loadPage().scrollTop = 400;

        previewModule.refresh(pageUrl);
        const reloadedPage = frame.loadPage();

        expect(reloadedPage.scrollTop).toBe(400);
    });

    it('holds a restored position until the page has loaded', () => {
        previewModule.refresh(pageUrl);

        expect(previewModule.setScrollPercentage(0.25)).toBe(false);
        expect(previewModule.getScrollPercentage()).toBe(0.25);

        const page = frame.loadPage();

        expect(page.scrollTop).toBe(200);
    });

    it('holds a restored position while the preview is hidden, and applies it once shown', () => {
        previewModule.refresh(pageUrl);
        frame.clientHeight = 0;
        const page = frame.loadPage();

        previewModule.setScrollPercentage(0.5);
        expect(page.scrollTop).toBe(0);

        frame.clientHeight = 400;
        resizeCallbacks.forEach((callback) => callback());

        expect(page.scrollTop).toBe(400);
    });
});
