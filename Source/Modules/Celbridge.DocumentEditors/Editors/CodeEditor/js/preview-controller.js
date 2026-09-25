// Owns the preview iframe and the dynamically-imported preview module.
// The preview module contract is format-agnostic. Every module implements initialize, render,
// setBasePath and setScrollPercentage. getScrollPercentage, scrollToSourceLine, getTopSourceLine,
// beginFind and refresh are optional, and are called only where a module exports them. Any ES module
// implementing the contract can be used: Markdown renders the buffer it is handed, and HTML shows the
// file itself.
//
// Link clicks in the preview are handled here rather than by each module, so every preview follows the same
// rule.

import { log } from './logger.js';

/**
 * What a click on a link in the preview does.
 */
export const LinkAction = Object.freeze({
    // The link points to a place in the current page, so the preview scrolls there.
    Scroll: 'scroll',
    // The host resolves the link: a project file opens in Celbridge and anything else in the system browser.
    Route: 'route',
    // The page handles the link itself, so the click is not intercepted.
    Ignore: 'ignore'
});

/**
 * Decides what a click on a link does. A link to the current page scrolls there. A relative path or an
 * http or https URL goes to the host. Any other link, such as a blob: or data: URL, is left to the page,
 * because the host cannot resolve it.
 * @param {string} href - The link's href attribute, as written.
 * @param {string} documentUrl - The URL of the document the link is in.
 * @param {string} baseUrl - The URL the document resolves its links against.
 * @returns {{action: string, fragment?: string, href?: string}} The action, with the fragment to scroll
 * to or the href to route.
 */
export function resolveLinkClick(href, documentUrl, baseUrl) {
    const trimmedHref = href.trim();

    // A bare fragment always refers to this page, even when the document declares another base URL.
    if (trimmedHref.startsWith('#')) {
        return { action: LinkAction.Scroll, fragment: trimmedHref.substring(1) };
    }

    let resolvedUrl;
    try {
        resolvedUrl = new URL(trimmedHref, baseUrl);
    } catch {
        return { action: LinkAction.Ignore };
    }

    if (removeFragment(resolvedUrl.href) === removeFragment(documentUrl)) {
        return { action: LinkAction.Scroll, fragment: resolvedUrl.hash.substring(1) };
    }

    // The host resolves a relative path against the document's own folder, as a served page does.
    if (isRelativePath(trimmedHref)) {
        return { action: LinkAction.Route, href: trimmedHref };
    }

    if (resolvedUrl.protocol === 'http:' ||
        resolvedUrl.protocol === 'https:') {
        return { action: LinkAction.Route, href: resolvedUrl.href };
    }

    return { action: LinkAction.Ignore };
}

export class PreviewController {
    #iframe;
    #callbacks;
    #module = null;
    #modulePromise = null;
    #rendererUrl = null;
    #pendingBasePath = '';
    #pendingScrollPercentage = null;
    #pendingScrollSourceLine = null;
    #pendingContent = null;
    #pendingRefreshUrl = null;
    #hasRendered = false;

    constructor(iframeElement, callbacks) {
        this.#iframe = iframeElement;
        this.#callbacks = callbacks ?? {};

        // Each load replaces the frame's document, so the link handler is attached to every new document.
        this.#iframe?.addEventListener('load', () => this.#bindFrameDocument());
    }

    isActive() {
        return this.#module !== null;
    }

    async setRenderer(rendererUrl) {
        if (rendererUrl === this.#rendererUrl) {
            return;
        }

        if (!rendererUrl) {
            log('preview: renderer detached');
            this.#rendererUrl = null;
            this.#module = null;
            this.#modulePromise = null;
            this.#pendingRefreshUrl = null;
            if (this.#iframe) {
                this.#iframe.src = 'about:blank';
            }
            return;
        }

        log('preview: renderer attaching', rendererUrl);
        this.#module = null;
        this.#modulePromise = null;
        this.#hasRendered = false;
        this.#rendererUrl = rendererUrl;

        await this.#ensureModuleLoaded();
        log('preview: module loaded');
    }

    // Whether the loaded renderer can map its output back to source lines, which scroll sync needs.
    canScrollToSourceLine() {
        return this.#module !== null &&
            typeof this.#module.scrollToSourceLine === 'function';
    }

    // Whether the loaded renderer shows the saved file instead of rendering the editor buffer. Such a renderer
    // implements refresh.
    canRefresh() {
        return this.#module !== null &&
            typeof this.#module.refresh === 'function';
    }

    // Opens the preview's find bar. Returns false when the renderer is not loaded or offers no find, so
    // the caller can fall back to the editor's.
    beginFind() {
        return this.#module && typeof this.#module.beginFind === 'function'
            ? this.#module.beginFind()
            : false;
    }

    setBasePath(basePath) {
        this.#pendingBasePath = basePath ?? '';
        if (this.#module) {
            this.#module.setBasePath(this.#pendingBasePath);
        }
    }

    render(content) {
        // Always cache the latest content so the first render can replay
        // once the renderer module finishes loading. setRenderer() is typically
        // fire-and-forget, and callers pass content in via onInitialContent
        // before that promise resolves.
        this.#pendingContent = content;

        if (!this.#module) {
            return;
        }

        const isFirstRender = !this.#hasRendered;
        this.#module.render(content);
        this.#hasRendered = true;
        this.#pendingContent = null;

        if (isFirstRender) {
            log('preview: first render complete');
        }

        if (this.#pendingScrollSourceLine !== null) {
            const pending = this.#pendingScrollSourceLine;
            requestAnimationFrame(() => {
                this.#applyScrollSourceLine(pending.line, pending.fraction);
            });
        } else if (this.#pendingScrollPercentage !== null) {
            const pending = this.#pendingScrollPercentage;
            requestAnimationFrame(() => {
                this.#applyScrollPercentage(pending);
            });
        }
    }

    /**
     * Tells the preview to show the current contents of the file at the URL. A renderer that shows the saved
     * file reloads it. A renderer of the editor buffer does not implement refresh.
     * @param {string} url
     */
    refresh(url) {
        if (!this.#module) {
            this.#pendingRefreshUrl = url;
            return;
        }

        this.#applyRefresh(this.#module, url);
    }

    scrollToSourceLine(line, fraction = 0) {
        if (!this.#module ||
            !this.#hasRendered ||
            typeof this.#module.scrollToSourceLine !== 'function') {
            this.#pendingScrollSourceLine = { line, fraction };
            return;
        }

        this.#applyScrollSourceLine(line, fraction);
    }

    getTopSourceLine() {
        if (!this.#module || typeof this.#module.getTopSourceLine !== 'function') {
            return null;
        }
        return this.#module.getTopSourceLine();
    }

    setScrollPercentage(percentage) {
        if (!this.#module || !this.#hasRendered) {
            this.#pendingScrollPercentage = percentage;
            return;
        }

        this.#applyScrollPercentage(percentage);
    }

    getScrollPercentage() {
        if (!this.#module || typeof this.#module.getScrollPercentage !== 'function') {
            return 0;
        }
        return this.#module.getScrollPercentage();
    }

    #applyScrollPercentage(percentage) {
        // The preview module's setScrollPercentage returns true only when it
        // could actually scroll. When it returns false, it has queued the
        // value internally and will retry via its own ResizeObserver once
        // scrollHeight becomes available — happens when a restored-but-
        // inactive tab first becomes visible on project reload.
        this.#module.setScrollPercentage(percentage);
        this.#pendingScrollPercentage = null;
    }

    #applyScrollSourceLine(line, fraction) {
        this.#module.scrollToSourceLine(line, fraction);
        this.#pendingScrollSourceLine = null;
    }

    #applyRefresh(module, url) {
        if (typeof module.refresh === 'function') {
            module.refresh(url);
        }
    }

    #bindFrameDocument() {
        // Null if the page has navigated the frame to another origin. Links there are left alone.
        const frameDocument = this.#iframe.contentDocument;
        if (!frameDocument) {
            return;
        }

        frameDocument.addEventListener('click', this.#handleLinkClick);

        // Also cleared here, not only on click, in case the page stops the click from reaching this listener.
        for (const link of frameDocument.querySelectorAll('a[download][target], area[download][target]')) {
            link.removeAttribute('target');
        }
    }

    #handleLinkClick = (event) => {
        // The page handled the click itself.
        if (event.defaultPrevented) {
            return;
        }

        const link = event.target?.closest?.('a[href], area[href]');
        if (!link) {
            return;
        }

        // Downloads are not intercepted: the head saves them to the project's downloads folder. The target is
        // cleared so the download runs in this frame, because the editor opens no new windows.
        if (link.hasAttribute('download')) {
            link.removeAttribute('target');
            return;
        }

        const frameDocument = link.ownerDocument;
        const resolution = resolveLinkClick(link.getAttribute('href'), frameDocument.URL, frameDocument.baseURI);

        if (resolution.action === LinkAction.Scroll) {
            // UNO-BUG: on macOS, Uno cancels every same-document navigation. The host works around this for
            // the top-level page but not for frames, and the preview is a frame. Scrolling here instead of
            // leaving the link to the browser works the same way on every head.
            event.preventDefault();
            scrollToFragment(frameDocument, resolution.fragment);
            return;
        }

        if (resolution.action === LinkAction.Route) {
            event.preventDefault();
            this.#callbacks.onLinkClicked?.(resolution.href);
        }
    };

    async #ensureModuleLoaded() {
        if (this.#module) {
            return this.#module;
        }

        if (!this.#modulePromise) {
            this.#modulePromise = this.#loadModule();
        }

        this.#module = await this.#modulePromise;
        return this.#module;
    }

    async #loadModule() {
        if (!this.#iframe) {
            throw new Error('Preview iframe element is missing');
        }
        if (!this.#rendererUrl) {
            throw new Error('No preview renderer URL configured');
        }

        // The preview module owns its own iframe shell setup; this controller
        // stays generic across preview formats by just importing the URL.
        const module = await import(/* @vite-ignore */ this.#rendererUrl);

        await module.initialize(this.#iframe, this.#callbacks);

        if (this.#pendingBasePath) {
            module.setBasePath(this.#pendingBasePath);
        }

        // If render(content) was called before the module finished loading,
        // replay it now so the preview shows the content on first open.
        if (this.#pendingContent !== null) {
            const pending = this.#pendingContent;
            this.#pendingContent = null;
            this.#module = module;
            this.render(pending);
        }

        if (this.#pendingRefreshUrl !== null) {
            const pendingUrl = this.#pendingRefreshUrl;
            this.#pendingRefreshUrl = null;
            this.#applyRefresh(module, pendingUrl);
        }

        return module;
    }
}

function removeFragment(url) {
    const hashIndex = url.indexOf('#');
    if (hashIndex < 0) {
        return url;
    }

    return url.substring(0, hashIndex);
}

// A relative path has no scheme. An href that starts with two slashes names another host, so it is not a
// relative path.
function isRelativePath(href) {
    const hasScheme = /^[a-z][a-z0-9+.-]*:/i.test(href);
    const namesHost = /^[\\/]{2}/.test(href);

    return !hasScheme &&
        !namesHost;
}

// An empty fragment, or "top" when no element has that name, means the top of the page.
function scrollToFragment(frameDocument, fragment) {
    const target = findFragmentTarget(frameDocument, fragment);
    if (target) {
        target.scrollIntoView({ behavior: 'smooth' });
        return;
    }

    const decodedFragment = decodeFragment(fragment);
    if (decodedFragment === '' ||
        decodedFragment.toLowerCase() === 'top') {
        frameDocument.defaultView?.scrollTo({ top: 0, behavior: 'smooth' });
    }
}

// Finds the fragment's target the way a browser does: by id, then by anchor name, first as written and then
// percent-decoded.
function findFragmentTarget(frameDocument, fragment) {
    if (fragment === '') {
        return null;
    }

    const names = [
        fragment,
        decodeFragment(fragment)
    ];

    for (const name of names) {
        const element = frameDocument.getElementById(name) ?? findNamedAnchor(frameDocument, name);
        if (element) {
            return element;
        }
    }

    return null;
}

function findNamedAnchor(frameDocument, name) {
    for (const element of frameDocument.getElementsByName(name)) {
        if (element.localName === 'a') {
            return element;
        }
    }

    return null;
}

function decodeFragment(fragment) {
    try {
        return decodeURIComponent(fragment);
    } catch {
        return fragment;
    }
}
