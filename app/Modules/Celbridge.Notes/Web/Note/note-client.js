// Note Client: Note-specific API for communicating with the Celbridge Note host.
// Uses the shared RPC transport from celbridge.js.

import { RpcTransport } from 'https://shared.celbridge/core/rpc-transport.js';

/**
 * Note Client for editor communication.
 */
class NoteClient {
    /** @type {RpcTransport} */
    #transport;

    constructor() {
        this.#transport = new RpcTransport();
    }

    /**
     * Registers a handler for navigation requests from the host.
     * @param {function(string): void} handler - The handler function receiving the heading id or text.
     */
    onNavigateToHeading(handler) {
        this.#transport.addEventListener('note/navigateToHeading', (params) => {
            handler(params.heading);
        });
    }

    /**
     * Registers a handler for TOC visibility toggle requests from the host.
     * @param {function(boolean): void} handler - The handler function receiving the visibility state.
     */
    onSetTocVisibility(handler) {
        this.#transport.addEventListener('note/setTocVisibility', (params) => {
            handler(params.visible);
        });
    }

    /**
     * Registers a handler for focus requests from the host.
     * @param {function(): void} handler - The handler function.
     */
    onFocus(handler) {
        this.#transport.addEventListener('note/focus', () => {
            handler();
        });
    }
}

// Export singleton instance
export const noteClient = new NoteClient();
