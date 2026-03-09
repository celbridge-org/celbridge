// Console Client: Console-specific API for communicating with the Celbridge Console host.
// Uses the shared RPC transport from celbridge.js.

import { RpcTransport } from 'https://shared.celbridge/celbridge-client/core/rpc-transport.js';

/**
 * Console Client for console communication.
 */
class ConsoleClient {
    /** @type {RpcTransport} */
    #transport;

    constructor() {
        this.#transport = new RpcTransport();
    }

    /**
     * Notifies the host that user typed input in the console.
     * @param {string} data - The input data.
     */
    sendInput(data) {
        this.#transport.notify('console/input', { data });
    }

    /**
     * Notifies the host that the console was resized.
     * @param {number} cols - Number of columns.
     * @param {number} rows - Number of rows.
     */
    sendResize(cols, rows) {
        this.#transport.notify('console/resize', { cols, rows });
    }

    /**
     * Registers a handler for console write operations from the host.
     * @param {function(string): void} handler - The handler function.
     */
    onWrite(handler) {
        this.#transport.addEventListener('console/write', (params) => {
            handler(params.text);
        });
    }

    /**
     * Registers a handler for focus requests from the host.
     * @param {function(): void} handler - The handler function.
     */
    onFocus(handler) {
        this.#transport.addEventListener('console/focus', () => {
            handler();
        });
    }

    /**
     * Registers a handler for theme changes from the host.
     * @param {function(string): void} handler - The handler function.
     */
    onSetTheme(handler) {
        this.#transport.addEventListener('console/setTheme', (params) => {
            handler(params.theme);
        });
    }

    /**
     * Registers a handler for command injection from the host.
     * @param {function(string): void} handler - The handler function.
     */
    onInjectCommand(handler) {
        this.#transport.addEventListener('console/injectCommand', (params) => {
            handler(params.command);
        });
    }
}

// Export singleton instance
export const consoleClient = new ConsoleClient();
