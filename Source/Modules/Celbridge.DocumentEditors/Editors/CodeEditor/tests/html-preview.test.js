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
    const scrollListeners = [];

    return {
        scrollingElement: {
            scrollHeight: 1000,
            clientHeight: 200,
            scrollTop: 0
        },
        defaultView: {
            addEventListener(type, listener) {
                if (type === 'scroll') {
                    scrollListeners.push(listener);
                }
            }
        },
        // Scrolls the page as the reader does, which raises the page's scroll event.
        scrollPage(position) {
            this.scrollingElement.scrollTop = position;
            scrollListeners.forEach((listener) => listener());
        }
    };
}

// A stand-in for the preview frame. jsdom cannot load a page into a real one, and lays nothing out.
function createFrame() {
    const loadListeners = [];
    const attributes = new Map();
    let src = 'about:blank';

    return {
        // Every URL the frame's src was set to. A browser navigates on each one, even one the src already names.
        navigations: [],
        get src() {
            return src;
        },
        set src(url) {
            src = url;
            this.navigations.push(url);
        },
        clientHeight: 400,
        contentDocument: createPage(),
        addEventListener(type, listener) {
            if (type === 'load') {
                loadListeners.push(listener);
            }
        },
        setAttribute(name, value) {
            attributes.set(name, String(value));
        },
        removeAttribute(name) {
            attributes.delete(name);
        },
        getAttribute(name) {
            return attributes.has(name) ? attributes.get(name) : null;
        },
        hasAttribute(name) {
            return attributes.has(name);
        },
        // Replaces the document with a newly loaded page, as a navigation does, and fires the frame's load.
        loadPage() {
            this.contentDocument = createPage();
            loadListeners.forEach((listener) => listener());

            return this.contentDocument.scrollingElement;
        },
        // Hides the frame as Source mode does. The browser resets the page's scroll position when it loses its
        // layout.
        hide() {
            this.clientHeight = 0;
            this.contentDocument.scrollingElement.scrollTop = 0;
            resizeCallbacks.forEach((callback) => callback());
        },
        show() {
            this.clientHeight = 400;
            resizeCallbacks.forEach((callback) => callback());
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

    it('navigates the frame back to the file after the page has navigated it elsewhere', () => {
        previewModule.refresh(pageUrl);
        frame.loadPage();

        // The page navigates itself, and the new page loads. The frame's src still names the file.
        frame.loadPage();
        previewModule.refresh(pageUrl);

        expect(frame.navigations).toEqual([pageUrl, pageUrl]);
        expect(frame.getAttribute('aria-busy')).toBe('true');
    });

    it('marks the frame as the content frame once it shows the page', () => {
        expect(frame.hasAttribute('data-cel-content-frame')).toBe(false);

        previewModule.refresh(pageUrl);

        expect(frame.hasAttribute('data-cel-content-frame')).toBe(true);
    });

    it('marks the frame busy from each refresh until the page has loaded', () => {
        previewModule.refresh(pageUrl);
        expect(frame.getAttribute('aria-busy')).toBe('true');

        frame.loadPage();
        expect(frame.hasAttribute('aria-busy')).toBe(false);

        previewModule.refresh(pageUrl);
        expect(frame.getAttribute('aria-busy')).toBe('true');
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

    it('keeps the reader\'s place when the preview is hidden and shown again', () => {
        previewModule.refresh(pageUrl);
        frame.loadPage();
        frame.contentDocument.scrollPage(400);

        frame.hide();
        frame.show();

        expect(frame.contentDocument.scrollingElement.scrollTop).toBe(400);
    });

    it('keeps the reader\'s place across a reload while the preview is hidden', () => {
        previewModule.refresh(pageUrl);
        frame.loadPage();
        frame.contentDocument.scrollPage(400);
        frame.hide();

        previewModule.refresh(pageUrl);
        const reloadedPage = frame.loadPage();
        expect(reloadedPage.scrollTop).toBe(0);

        frame.show();
        expect(reloadedPage.scrollTop).toBe(400);
    });

    it('reports the last position the page showed while the preview is hidden', () => {
        previewModule.refresh(pageUrl);
        frame.loadPage();
        frame.contentDocument.scrollPage(400);

        frame.hide();

        expect(previewModule.getScrollPercentage()).toBe(0.5);
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
