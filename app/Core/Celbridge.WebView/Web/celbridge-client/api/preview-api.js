// Preview API: Operations for preview panes that display rendered content.
// Used by preview renderers (e.g., Markdown, AsciiDoc) to communicate with the host.

/**
 * Preview operations API.
 * Handles communication between preview panes and the .NET host via JSON-RPC.
 */
export class PreviewAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /** @type {string} */
    #basePath = '';

    /** @type {Function|null} */
    #onSetContextHandler = null;

    /** @type {Function|null} */
    #onUpdateHandler = null;

    /** @type {Function|null} */
    #onScrollHandler = null;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     */
    constructor(transport) {
        this.#transport = transport;

        // Register handlers for host notifications
        this.#transport.addEventListener('preview/setContext', (params) => {
            this.#basePath = params.basePath || '';
            if (this.#onSetContextHandler) {
                this.#onSetContextHandler(this.#basePath);
            }
        });

        this.#transport.addEventListener('preview/update', (params) => {
            if (this.#onUpdateHandler) {
                this.#onUpdateHandler(params.content);
            }
        });

        this.#transport.addEventListener('preview/scroll', (params) => {
            if (this.#onScrollHandler) {
                this.#onScrollHandler(params.scrollPercentage);
            }
        });
    }

    /**
     * Gets the current base path for resolving relative resources.
     * @returns {string}
     */
    get basePath() {
        return this.#basePath;
    }

    /**
     * Notifies the host to open a local resource (e.g., a linked document).
     * @param {string} href - The relative path to the resource.
     */
    openResource(href) {
        this.#transport.notify('preview/openResource', { href });
    }

    /**
     * Notifies the host to open an external URL in the browser.
     * @param {string} href - The URL to open.
     */
    openExternal(href) {
        this.#transport.notify('preview/openExternal', { href });
    }

    /**
     * Notifies the host to sync the editor scroll position.
     * @param {number} scrollPercentage - The scroll position as a percentage (0-1).
     */
    syncToEditor(scrollPercentage) {
        this.#transport.notify('preview/syncToEditor', { scrollPercentage });
    }

    /**
     * Registers a handler for when the document context (base path) is set.
     * @param {Function} handler - Called with the base path string.
     */
    onSetContext(handler) {
        this.#onSetContextHandler = handler;
    }

    /**
     * Registers a handler for preview content updates.
     * @param {Function} handler - Called with the content string.
     */
    onUpdate(handler) {
        this.#onUpdateHandler = handler;
    }

    /**
     * Registers a handler for scroll position changes.
     * @param {Function} handler - Called with the scroll percentage (0-1).
     */
    onScroll(handler) {
        this.#onScrollHandler = handler;
    }
}
