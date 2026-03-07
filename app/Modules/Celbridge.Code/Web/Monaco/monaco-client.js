// Monaco Client: Monaco-specific API for communicating with the Celbridge Monaco host.
// Uses the shared RPC transport from celbridge.js.

import { RpcTransport } from 'https://shared.celbridge/celbridge-client/core/rpc-transport.js';

/**
 * Monaco Client for editor communication.
 */
class MonacoClient {
    /** @type {RpcTransport} */
    #transport;

    constructor() {
        this.#transport = new RpcTransport();
    }

    /**
     * Registers a handler for editor initialization requests from the host.
     * @param {function(string): void} handler - The handler function receiving the language.
     */
    onInitialize(handler) {
        this.#transport.addEventListener('editor/initialize', (params) => {
            handler(params.language);
        });
    }

    /**
     * Registers a handler for language change requests from the host.
     * @param {function(string): void} handler - The handler function receiving the language.
     */
    onSetLanguage(handler) {
        this.#transport.addEventListener('editor/setLanguage', (params) => {
            handler(params.language);
        });
    }

    /**
     * Registers a handler for navigation requests from the host.
     * @param {function(number, number): void} handler - The handler function receiving lineNumber and column.
     */
    onNavigateToLocation(handler) {
        this.#transport.addEventListener('editor/navigateToLocation', (params) => {
            handler(params.lineNumber, params.column);
        });
    }

    /**
     * Registers a handler for scroll-to-percentage requests from the host.
     * @param {function(number): void} handler - The handler function receiving the scroll percentage (0-1).
     */
    onScrollToPercentage(handler) {
        this.#transport.addEventListener('editor/scrollToPercentage', (params) => {
            handler(params.percentage);
        });
    }
}

// Export singleton instance
export const monacoClient = new MonacoClient();
