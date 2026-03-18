// Code Preview API: Operations for code preview panes that display rendered content.
// Used by code preview renderers (e.g., Markdown, AsciiDoc) to communicate with the host.

/**
 * Code preview operations API.
 * Handles communication between code preview panes and the .NET host via JSON-RPC.
 */
export class CodePreviewAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /** @type {string} */
    #basePath = '';

    /** @type {Function|null} */
    #onSetBasePathHandler = null;

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
        this.#transport.addEventListener('codePreview/setBasePath', (params) => {
            this.#basePath = params.basePath || '';
            if (this.#onSetBasePathHandler) {
                this.#onSetBasePathHandler(this.#basePath);
            }
        });

        this.#transport.addEventListener('codePreview/update', (params) => {
            if (this.#onUpdateHandler) {
                this.#onUpdateHandler(params.content);
            }
        });

        this.#transport.addEventListener('codePreview/scroll', (params) => {
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
        this.#transport.notify('codePreview/openResource', { href });
    }

    /**
     * Notifies the host to open an external URL in the browser.
     * @param {string} href - The URL to open.
     */
    openExternal(href) {
        this.#transport.notify('codePreview/openExternal', { href });
    }

    /**
     * Notifies the host to sync the editor scroll position.
     * @param {number} scrollPercentage - The scroll position as a percentage (0-1).
     */
    syncToEditor(scrollPercentage) {
        this.#transport.notify('codePreview/syncToEditor', { scrollPercentage });
    }

    /**
     * Registers a handler for when the base path is set.
     * @param {Function} handler - Called with the base path string.
     */
    onSetBasePath(handler) {
        this.#onSetBasePathHandler = handler;
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
