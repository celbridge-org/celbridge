// Document API: Operations for loading, saving, and managing documents.

/**
 * @typedef {import('../types.js').DocumentMetadata} DocumentMetadata
 * @typedef {import('../types.js').LoadResult} LoadResult
 * @typedef {import('../types.js').SaveResult} SaveResult
 */

/**
 * Document operations API.
 */
export class DocumentAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     */
    constructor(transport) {
        this.#transport = transport;
    }

    /**
     * Loads the current document content from disk.
     * @param {Object} [options]
     * @param {boolean} [options.includeMetadata=false] - Whether to include metadata.
     * @returns {Promise<LoadResult>}
     */
    async load(options = {}) {
        return this.#transport.request('document/load', {
            includeMetadata: options.includeMetadata ?? false
        });
    }

    /**
     * Saves the document content.
     * @param {string} content - The content to save.
     * @returns {Promise<SaveResult>}
     */
    async save(content) {
        return this.#transport.request('document/save', { content });
    }

    /**
     * Saves binary document content (as base64).
     * @param {string} contentBase64 - The base64-encoded binary content.
     * @returns {Promise<SaveResult>}
     */
    async saveBinary(contentBase64) {
        return this.#transport.request('document/saveBinary', { contentBase64 });
    }

    /**
     * Loads binary document content from disk.
     * @param {Object} [options]
     * @param {boolean} [options.includeMetadata=false] - Whether to include metadata.
     * @returns {Promise<{contentBase64: string, metadata?: DocumentMetadata}>}
     */
    async loadBinary(options = {}) {
        return this.#transport.request('document/loadBinary', {
            includeMetadata: options.includeMetadata ?? false
        });
    }

    /**
     * Gets the document metadata.
     * @returns {Promise<DocumentMetadata>}
     */
    async getMetadata() {
        return this.#transport.request('document/getMetadata', {});
    }

    /**
     * Notifies the host that the document content has changed.
     * This is a notification (no response expected).
     */
    notifyChanged() {
        this.#transport.notify('document/changed', {});
    }

    /**
     * Registers a handler for external file change notifications.
     * @param {Function} handler - Called when the file changes externally.
     */
    onExternalChange(handler) {
        this.#transport.addEventListener('document/externalChange', handler);
    }

    /**
     * Registers a handler for save request notifications from the host.
     * The handler should get the current content and call document.save(content).
     * @param {Function} handler - Called when the host requests a save.
     */
    onRequestSave(handler) {
        this.#transport.addEventListener('document/requestSave', handler);
    }

    /**
     * Notifies the host that an import operation has completed.
     * Used by editors that load binary content (e.g., spreadsheets).
     * @param {boolean} success - Whether the import succeeded.
     * @param {string} [error] - Error message if import failed.
     */
    notifyImportComplete(success, error = null) {
        this.#transport.notify('import/complete', { success, error });
    }
}
