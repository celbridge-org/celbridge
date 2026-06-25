// Console Client: Console-specific API for communicating with the Celbridge Console host.
// Uses the shared RPC transport from celbridge.js.

import { RpcTransport } from '/assets/celbridge-client/core/rpc-transport.js';
import { AppState } from '/assets/celbridge-client/core/app-state.js';

/**
 * Console Client for console communication.
 */
class ConsoleClient {
    /** @type {RpcTransport} */
    #transport;

    /** @type {AppState} */
    #appState;

    constructor() {
        this.#transport = new RpcTransport();
        this.#appState = new AppState(this.#transport);
    }

    /**
     * The application-global state store (theme, ...). Read `consoleClient.appState.current.theme`,
     * react with `consoleClient.appState.onChanged(...)`. The host pushes the snapshot on connect.
     * @returns {AppState}
     */
    get appState() {
        return this.#appState;
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
