// Owns the preview iframe and the dynamically-imported preview module.
// The preview module contract is format-agnostic: initialize / render /
// setBasePath / setScrollPercentage / getScrollPercentage /
// scrollToSourceLine / getTopSourceLine. Any ES module implementing the
// contract can be used — Markdown is just one consumer.

import { log } from './logger.js';

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

        return module;
    }
}
