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
     * Content is text for text documents or base64-encoded for binary documents.
     * @returns {Promise<LoadResult>}
     */
    async load() {
        return this.#transport.request('document/load', {});
    }

    /**
     * Saves the document content.
     * Content is text for text documents or base64-encoded for binary documents.
     * @param {string} content - The content to save.
     * @returns {Promise<SaveResult>}
     */
    async save(content) {
        return this.#transport.request('document/save', { content });
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
        this.#transport.notify('document/importComplete', { success, error });
    }

    /**
     * Notifies the host that the JavaScript client has fully initialized.
     * Call this after the editor is ready to receive RPC commands.
     */
    notifyClientReady() {
        this.#transport.notify('document/clientReady', {});
    }

    /**
     * Notifies the host that document content has been loaded and the editor is ready for edits.
     * Call this after content has been set in the editor (e.g., after setValue in Monaco).
     */
    notifyContentLoaded() {
        this.#transport.notify('document/contentLoaded', {});
    }

    /**
     * Registers a handler for state save requests from the host.
     * The handler should return the current editor state as a JSON string, or null
     * if the editor has no state to save.
     * @param {Function} handler - Called when the host requests the editor state. Should return a string or null.
     */
    onRequestState(handler) {
        this.#transport.setRequestHandler('document/requestState', handler);
    }

    /**
     * Registers a handler for state restore requests from the host.
     * The handler receives a previously saved state string and should restore the editor to that state.
     * @param {Function} handler - Called with the state string to restore.
     */
    onRestoreState(handler) {
        this.#transport.setRequestHandler('document/restoreState', (params) => {
            // The host sends the state string as a positional argument, which arrives as an array
            const state = Array.isArray(params) ? params[0] : params;
            handler(state);
        });
    }
}
