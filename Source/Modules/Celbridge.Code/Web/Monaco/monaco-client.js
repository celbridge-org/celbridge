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
     * @param {function(Object): void} handler - The handler function receiving the editor options.
     */
    onInitialize(handler) {
        this.#transport.addEventListener('codeEditor/initialize', (params) => {
            handler(params);
        });
    }

    /**
     * Registers a handler for language change requests from the host.
     * @param {function(string): void} handler - The handler function receiving the language.
     */
    onSetLanguage(handler) {
        this.#transport.addEventListener('codeEditor/setLanguage', (params) => {
            handler(params.language);
        });
    }

    /**
     * Registers a handler for navigation requests from the host.
     * @param {function(number, number): void} handler - The handler function receiving lineNumber and column.
     */
    onNavigateToLocation(handler) {
        this.#transport.addEventListener('codeEditor/navigateToLocation', (params) => {
            handler(params.lineNumber, params.column, params.endLineNumber || 0, params.endColumn || 0);
        });
    }

    /**
     * Registers a handler for scroll-to-percentage requests from the host.
     * @param {function(number): void} handler - The handler function receiving the scroll percentage (0-1).
     */
    onScrollToPercentage(handler) {
        this.#transport.addEventListener('codeEditor/scrollToPercentage', (params) => {
            handler(params.percentage);
        });
    }

    /**
     * Registers a handler for insert-text requests from the host.
     * @param {function(string): void} handler - The handler function receiving the text to insert.
     */
    onInsertText(handler) {
        this.#transport.addEventListener('codeEditor/insertText', (params) => {
            handler(params.text);
        });
    }

    /**
     * Registers a handler for apply-edits requests from the host.
     * @param {function(Array): void} handler - The handler function receiving the edits array.
     */
    onApplyEdits(handler) {
        this.#transport.addEventListener('codeEditor/applyEdits', (params) => {
            handler(params.edits);
        });
    }

    /**
     * Registers a handler for applying a customization script from the host.
     * @param {function(string): void} handler - The handler function receiving the script URL.
     */
    onApplyCustomization(handler) {
        this.#transport.addEventListener('codeEditor/applyCustomization', (params) => {
            handler(params.scriptUrl);
        });
    }
}

// Export singleton instance
export const monacoClient = new MonacoClient();
