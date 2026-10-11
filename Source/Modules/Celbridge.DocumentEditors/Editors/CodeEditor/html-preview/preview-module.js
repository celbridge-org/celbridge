// HTML preview module.
// Shows the document by pointing the preview frame at the file's URL on the loopback server. The page loads
// exactly as it is served, so its scripts run, its relative assets resolve and it sees its real location.
// The frame shows the saved file, not the editor buffer. It loads it when the pipeline calls refresh: when the
// document opens or moves, and when the user reloads the preview.
//
// There is no scroll sync with the source, so scrollToSourceLine and getTopSourceLine are left out. Mapping
// rendered HTML back to source lines would require rewriting the markup, and the preview would then no
// longer be the page as served.
//
// Once the frame shows the page, it carries data-cel-content-frame, so the webview_* tools act on the page by
// default. While a page loads, the frame carries aria-busy, and the tools wait until it is cleared.
//
// If the WebView has no find bar of its own, the module installs the shared find bar in the page. It does
// this only when the user asks for find, so the page stays as served until then.

import celbridge from '/assets/celbridge-client/celbridge.js';
import { createFindBar } from '/assets/celbridge-client/ui/find-bar.js';

let iframeElement = null;

// The find bar installed in the page, and the document it was installed in. A reload replaces the document.
let findBar = null;
let findBarDocument = null;

// A scroll position waiting to be applied, kept across a reload or until the frame has a layout.
let pendingScrollPercentage = null;

// The page's scroll position the last time the frame had a layout. Hiding the frame resets the page's
// position, and a hidden frame reports none, so this is the position to return to.
let lastVisibleScrollPercentage = 0;

// True until the page has loaded, and again from each refresh until the new page loads. Until then the frame
// still holds the old document, and a scroll applied to it would be lost.
let isAwaitingDocument = true;

// requestedUrl is the address the last refresh asked for. replacedUrls holds the addresses that later refreshes
// replaced while they were still loading. WebKit can finish a replaced navigation and drop the newer one, which
// leaves the frame on an old address. A rename that arrives while the editor is loading causes this, because the
// page first loads the old address.
let requestedUrl = null;
let replacedUrls = [];

/**
 * Stores the frame the preview navigates. The page is loaded by refresh, so there is nothing to load yet.
 * @param {HTMLIFrameElement} iframe
 */
export function initialize(iframe) {
    iframeElement = iframe;

    iframe.addEventListener('load', () => {
        // The frame finished a navigation that a later refresh replaced, so it is asked for the current
        // address again, once.
        if (isShowingReplacedAddress()) {
            replacedUrls = [];
            iframe.src = requestedUrl;
            return;
        }

        isAwaitingDocument = false;
        iframe.removeAttribute('aria-busy');
        listenForScroll();
        applyPendingScrollAfterRender();
    });

    // The frame has no layout while the view mode hides the preview. The page's last position is held when the
    // frame is hidden, and applied once it is shown again.
    const resizeObserver = new ResizeObserver(() => {
        holdHiddenScrollPosition();
        applyPendingScrollAfterRender();
    });
    resizeObserver.observe(iframe);
}

/**
 * The preview shows the file rather than the buffer, so there is nothing to render.
 */
export function render() {}

/**
 * The page resolves its own relative URLs, so there is no base path to apply.
 */
export function setBasePath() {}

/**
 * Navigates the frame to the file at the URL, reloading it if the frame already shows it. The scroll
 * position is kept across the reload.
 * @param {string} url
 */
export function refresh(url) {
    if (pendingScrollPercentage === null &&
        !isAwaitingDocument) {
        pendingScrollPercentage = getVisibleScrollPercentage();
    }

    if (!isAwaitingDocument) {
        replacedUrls = [];
    } else if (requestedUrl !== null) {
        replacedUrls.push(requestedUrl);
    }
    requestedUrl = url;

    isAwaitingDocument = true;
    iframeElement.setAttribute('data-cel-content-frame', '');
    iframeElement.setAttribute('aria-busy', 'true');
    iframeElement.src = url;
}

/**
 * Opens a find bar in the page. Returns false if the WebView has a find bar of its own
 * (viewState.providesBuiltInFind) or the page cannot be reached. The caller then falls back to another find.
 * @returns {boolean}
 */
export function beginFind() {
    if (celbridge.viewState.current.providesBuiltInFind !== 'false') {
        return false;
    }

    // Null if the page has navigated the frame to another origin.
    const frameDocument = iframeElement?.contentDocument;
    if (!frameDocument?.body) {
        return false;
    }

    if (findBarDocument !== frameDocument) {
        findBar = createFindBar({
            document: frameDocument,
            searchRoot: frameDocument.body
        });
        findBarDocument = frameDocument;
    }

    return findBar.open();
}

/**
 * Scrolls the page to the given percentage (0-1). Returns false when the value was queued instead, to be
 * applied once the page has loaded and the frame has a layout.
 * @param {number} percentage
 * @returns {boolean}
 */
export function setScrollPercentage(percentage) {
    pendingScrollPercentage = percentage;

    return applyPendingScroll();
}

/**
 * The page's scroll position as a percentage (0-1). While a position is queued, returns that position.
 * @returns {number}
 */
export function getScrollPercentage() {
    if (pendingScrollPercentage !== null) {
        return pendingScrollPercentage;
    }

    return getVisibleScrollPercentage();
}

// The page's scroll position, or while the frame has no layout, the last position it had.
function getVisibleScrollPercentage() {
    if (!hasLayout()) {
        return lastVisibleScrollPercentage;
    }

    return readScrollPercentage();
}

// True when the frame shows an address that a later refresh replaced. False when the frame shows the address the
// last refresh asked for, a page that navigated itself elsewhere, or a page on another origin, whose document is out
// of reach.
function isShowingReplacedAddress() {
    if (replacedUrls.length === 0) {
        return false;
    }

    const shownUrl = iframeElement.contentDocument?.URL;
    if (!shownUrl) {
        return false;
    }

    return shownUrl !== resolveUrl(requestedUrl) &&
        replacedUrls.some((url) => resolveUrl(url) === shownUrl);
}

function resolveUrl(url) {
    return new URL(url, document.baseURI).href;
}

function hasLayout() {
    return iframeElement.clientHeight !== 0;
}

// Each load gives the page a new window, so the listener is added to every new one.
function listenForScroll() {
    const frameWindow = iframeElement.contentDocument?.defaultView;
    frameWindow?.addEventListener('scroll', recordScrollPosition, { passive: true });
}

function recordScrollPosition() {
    if (hasLayout()) {
        lastVisibleScrollPercentage = readScrollPercentage();
    }
}

// Once the frame has lost its layout, holds the page's last position to apply when the frame is shown again.
function holdHiddenScrollPosition() {
    if (hasLayout() ||
        pendingScrollPercentage !== null ||
        isAwaitingDocument) {
        return;
    }

    pendingScrollPercentage = lastVisibleScrollPercentage;
}

function readScrollPercentage() {
    const scrollingElement = getScrollingElement();
    if (!scrollingElement) {
        return 0;
    }

    const scrollRange = scrollingElement.scrollHeight - scrollingElement.clientHeight;
    if (scrollRange <= 0) {
        return 0;
    }

    return Math.max(0, Math.min(1, scrollingElement.scrollTop / scrollRange));
}

// WKWebView does not paint a newly loaded page that is scrolled before its first render with a layout. The
// frame stays blank until the reader scrolls it. Waiting one animation frame lets that first render happen.
function applyPendingScrollAfterRender() {
    requestAnimationFrame(() => applyPendingScroll());
}

function applyPendingScroll() {
    if (pendingScrollPercentage === null ||
        isAwaitingDocument ||
        !hasLayout()) {
        return false;
    }

    // Null if the page has navigated the frame to another origin, where the position cannot be applied.
    const scrollingElement = getScrollingElement();
    if (!scrollingElement) {
        pendingScrollPercentage = null;
        return false;
    }

    const percentage = Math.max(0, Math.min(1, pendingScrollPercentage));
    const scrollRange = scrollingElement.scrollHeight - scrollingElement.clientHeight;
    scrollingElement.scrollTop = Math.max(0, scrollRange) * percentage;
    lastVisibleScrollPercentage = percentage;
    pendingScrollPercentage = null;

    return true;
}

// The page's scrolling element. In a document without a doctype this is the body, not the root.
function getScrollingElement() {
    const frameDocument = iframeElement?.contentDocument;
    if (!frameDocument) {
        return null;
    }

    return frameDocument.scrollingElement ?? frameDocument.documentElement;
}
