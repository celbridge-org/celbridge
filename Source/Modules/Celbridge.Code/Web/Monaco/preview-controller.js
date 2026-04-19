// Owns the preview iframe and the dynamically-imported preview module.
// The preview module contract is format-agnostic: initialize / render /
// setBasePath / setScrollPercentage / getScrollPercentage. Any ES module
// implementing the contract can be used — Markdown is just one consumer.

import { log } from './monaco-logger.js';

export class PreviewController {
    #iframe;
    #callbacks;
    #module = null;
    #modulePromise = null;
    #rendererUrl = null;
    #pendingBasePath = '';
    #pendingScrollPercentage = null;
    #hasRendered = false;

    constructor(iframeElement, callbacks) {
        this.#iframe = iframeElement;
        this.#callbacks = callbacks ?? {};
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

    setBasePath(basePath) {
        this.#pendingBasePath = basePath ?? '';
        if (this.#module) {
            this.#module.setBasePath(this.#pendingBasePath);
        }
    }

    render(content) {
        if (!this.#module) {
            return;
        }

        const isFirstRender = !this.#hasRendered;
        this.#module.render(content);
        this.#hasRendered = true;

        if (isFirstRender) {
            log('preview: first render complete');
        }

        if (this.#pendingScrollPercentage !== null) {
            const pending = this.#pendingScrollPercentage;
            log('preview: replaying pending scroll after render', pending);
            requestAnimationFrame(() => {
                this.#applyScrollPercentage(pending);
            });
        }
    }

    setScrollPercentage(percentage) {
        if (!this.#module || !this.#hasRendered) {
            log('preview: buffering scroll (module/render not ready)', percentage);
            this.#pendingScrollPercentage = percentage;
            return;
        }

        this.#applyScrollPercentage(percentage);
    }

    #applyScrollPercentage(percentage) {
        // The preview module's setScrollPercentage returns true only when it
        // could actually scroll. When it returns false, it has queued the
        // value internally (and will retry via its own ResizeObserver once
        // scrollHeight becomes available — happens when a restored-but-
        // inactive tab first becomes visible on project reload).
        const applied = this.#module.setScrollPercentage(percentage);
        this.#pendingScrollPercentage = null;
        log('preview: scroll ' + (applied ? 'applied' : 'deferred to module'), percentage);
    }

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

        return module;
    }
}
