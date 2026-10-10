import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';

const pageUrl = '/project/docs/page.html';

// jsdom has no ResizeObserver, so a test fires the observer by hand when it changes the frame's size.
let resizeCallbacks = [];

// Animation frame callbacks waiting for the next render. A test runs them with renderFrame.
let animationFrameCallbacks = [];

function renderFrame() {
    const callbacks = animationFrameCallbacks;
    animationFrameCallbacks = [];
    callbacks.forEach((callback) => callback());
}

class FakeResizeObserver {
    constructor(callback) {
        resizeCallbacks.push(callback);
    }

    observe() {}

    disconnect() {}
}

// A freshly loaded page at the address, with a scroll range of 800, scrolled to the top.
function createPage(url) {
    const scrollListeners = [];

    return {
        URL: url,
        body: {},
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

// An address as the frame's document reports it.
function absoluteUrl(url) {
    return new URL(url, document.baseURI).href;
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
        // Replaces the document with a newly loaded page, as a navigation does, and fires the frame's load. The
        // page's address is the one the frame's src names, unless the test gives another.
        loadPage(url = absoluteUrl(src)) {
            this.contentDocument = createPage(url);
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
    let celbridge;
    let createdFindBars;

    beforeEach(async () => {
        resizeCallbacks = [];
        animationFrameCallbacks = [];
        vi.stubGlobal('ResizeObserver', FakeResizeObserver);
        vi.stubGlobal('requestAnimationFrame', (callback) => animationFrameCallbacks.push(callback));

        // The module keeps its state at module level, so each test imports a fresh copy. The test imports the
        // stubs again too, so it uses the same instances as the module.
        vi.resetModules();
        previewModule = await import('../html-preview/preview-module.js');
        celbridge = (await import('/assets/celbridge-client/celbridge.js')).default;
        createdFindBars = (await import('/assets/celbridge-client/ui/find-bar.js')).__createdFindBars;

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

    it('asks again for the current address when the frame finishes a navigation a refresh replaced', () => {
        const oldUrl = '/project/docs/old.html';
        previewModule.refresh(oldUrl);
        previewModule.refresh(pageUrl);

        frame.loadPage(absoluteUrl(oldUrl));

        expect(frame.navigations).toEqual([oldUrl, pageUrl, pageUrl]);
        expect(frame.getAttribute('aria-busy')).toBe('true');

        frame.loadPage();

        expect(frame.hasAttribute('aria-busy')).toBe(false);
    });

    it('asks again for the current address only once', () => {
        const oldUrl = '/project/docs/old.html';
        previewModule.refresh(oldUrl);
        previewModule.refresh(pageUrl);

        frame.loadPage(absoluteUrl(oldUrl));
        frame.loadPage(absoluteUrl(oldUrl));

        expect(frame.navigations).toEqual([oldUrl, pageUrl, pageUrl]);
        expect(frame.hasAttribute('aria-busy')).toBe(false);
    });

    it('leaves a page that navigated itself away while it loaded', () => {
        previewModule.refresh('/project/docs/old.html');
        previewModule.refresh(pageUrl);

        frame.loadPage(absoluteUrl('/project/docs/elsewhere.html'));

        expect(frame.navigations).toEqual(['/project/docs/old.html', pageUrl]);
        expect(frame.hasAttribute('aria-busy')).toBe(false);
    });

    it('asks again for the current address when the frame finishes the earliest of several replaced navigations', () => {
        const oldUrl = '/project/docs/old.html';
        const middleUrl = '/project/docs/middle.html';
        previewModule.refresh(oldUrl);
        previewModule.refresh(middleUrl);
        previewModule.refresh(pageUrl);

        frame.loadPage(absoluteUrl(oldUrl));

        expect(frame.navigations).toEqual([oldUrl, middleUrl, pageUrl, pageUrl]);
        expect(frame.getAttribute('aria-busy')).toBe('true');
    });

    it('does not count a refresh made after the page loaded as replacing it', () => {
        const oldUrl = '/project/docs/old.html';
        previewModule.refresh(oldUrl);
        frame.loadPage();
        previewModule.refresh(pageUrl);

        frame.loadPage(absoluteUrl(oldUrl));

        expect(frame.navigations).toEqual([oldUrl, pageUrl]);
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
        renderFrame();

        expect(reloadedPage.scrollTop).toBe(400);
    });

    it('waits for a newly loaded page to render before scrolling it', () => {
        previewModule.refresh(pageUrl);
        frame.loadPage().scrollTop = 400;

        previewModule.refresh(pageUrl);
        const reloadedPage = frame.loadPage();
        expect(reloadedPage.scrollTop).toBe(0);
        expect(previewModule.getScrollPercentage()).toBe(0.5);

        renderFrame();
        expect(reloadedPage.scrollTop).toBe(400);
    });

    it('holds a restored position until the page has loaded', () => {
        previewModule.refresh(pageUrl);

        expect(previewModule.setScrollPercentage(0.25)).toBe(false);
        expect(previewModule.getScrollPercentage()).toBe(0.25);

        const page = frame.loadPage();
        renderFrame();

        expect(page.scrollTop).toBe(200);
    });

    it('keeps the reader\'s place when the preview is hidden and shown again', () => {
        previewModule.refresh(pageUrl);
        frame.loadPage();
        frame.contentDocument.scrollPage(400);

        frame.hide();
        frame.show();
        renderFrame();

        expect(frame.contentDocument.scrollingElement.scrollTop).toBe(400);
    });

    it('keeps the reader\'s place across a reload while the preview is hidden', () => {
        previewModule.refresh(pageUrl);
        frame.loadPage();
        frame.contentDocument.scrollPage(400);
        frame.hide();

        previewModule.refresh(pageUrl);
        const reloadedPage = frame.loadPage();
        renderFrame();
        expect(reloadedPage.scrollTop).toBe(0);

        frame.show();
        renderFrame();
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
        renderFrame();

        expect(page.scrollTop).toBe(400);
    });

    it('leaves find to a WebView that has a find bar of its own', () => {
        celbridge.viewState.current.providesBuiltInFind = 'true';
        previewModule.refresh(pageUrl);
        frame.loadPage();

        expect(previewModule.beginFind()).toBe(false);
        expect(createdFindBars).toHaveLength(0);
    });

    it('installs a find bar in the page only once find is asked for', () => {
        celbridge.viewState.current.providesBuiltInFind = 'false';
        previewModule.refresh(pageUrl);
        frame.loadPage();
        expect(createdFindBars).toHaveLength(0);

        expect(previewModule.beginFind()).toBe(true);
        expect(createdFindBars).toHaveLength(1);
        expect(createdFindBars[0].options.document).toBe(frame.contentDocument);
        expect(createdFindBars[0].options.searchRoot).toBe(frame.contentDocument.body);

        previewModule.beginFind();
        expect(createdFindBars).toHaveLength(1);
        expect(createdFindBars[0].openCount).toBe(2);
    });

    it('installs the find bar again in a reloaded page', () => {
        celbridge.viewState.current.providesBuiltInFind = 'false';
        previewModule.refresh(pageUrl);
        frame.loadPage();
        previewModule.beginFind();

        previewModule.refresh(pageUrl);
        frame.loadPage();
        previewModule.beginFind();

        expect(createdFindBars).toHaveLength(2);
        expect(createdFindBars[1].options.document).toBe(frame.contentDocument);
    });
});
