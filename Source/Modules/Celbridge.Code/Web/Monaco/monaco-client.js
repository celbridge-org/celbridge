// Monaco Client: thin RPC transport wrapper for Monaco editor communication.
// The host drives the editor via `codeEditor/*` requests; the editor notifies
// the host via `input/*` notifications. See monaco.js for the request handler
// registrations.

import { RpcTransport } from 'https://shared.celbridge/celbridge-client/core/rpc-transport.js';

class MonacoClient {
    /** @type {RpcTransport} */
    #transport;

    constructor() {
        this.#transport = new RpcTransport();
    }

    /**
     * Registers a handler for an incoming host request.
     * @param {string} wireName - The JSON-RPC method name (e.g. 'codeEditor/setLanguage').
     * @param {(params: Object) => void} handler - Receives the full params object.
     */
    onRequest(wireName, handler) {
        this.#transport.addEventListener(wireName, handler);
    }

    /**
     * Notifies the host that the preview pane scrolled.
     * Used for state preservation across external reloads and session restore.
     * @param {number} percentage - The preview scroll position (0.0 to 1.0).
     */
    notifyPreviewScrolled(percentage) {
        this.#transport.notify('input/previewScrollChanged', { scrollPercentage: percentage });
    }
}

export const monacoClient = new MonacoClient();
