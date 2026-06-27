// Console Client: Console-specific API for communicating with the Celbridge Console host.
// Uses the shared RPC transport from celbridge.js.

import { RpcTransport } from '/assets/celbridge-client/core/rpc-transport.js';
import { createAppStateStore } from '/assets/celbridge-client/core/state-store.js';

/**
 * Console Client for console communication.
 */
class ConsoleClient {
    /** @type {RpcTransport} */
    #transport;

    /** @type {import('/assets/celbridge-client/core/state-store.js').Store} */
    #appState;

    constructor() {
        this.#transport = new RpcTransport();
        this.#appState = createAppStateStore(this.#transport);
    }

    /**
     * The application-global state store (theme, ...). Read `consoleClient.appState.current.theme`,
     * react with `consoleClient.appState.onChanged(...)`. The host pushes the snapshot on connect.
     * @returns {import('/assets/celbridge-client/core/state-store.js').Store}
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

    /**
     * Notifies the host that the console WebView received focus. On the Skia heads GotFocus does not fire
     * for clicks inside the WebView, so this DOM-focus signal is how the host learns the console is active.
     */
    notifyFocusReceived() {
        this.#transport.notify('input/focusReceived', {});
    }

    /**
     * Registers a handler for blur requests from the host (focus moved to another panel).
     * @param {function(): void} handler - The handler function.
     */
    onReleaseFocus(handler) {
        this.#transport.addEventListener('input/releaseFocus', () => {
            handler();
        });
    }

    /**
     * Registers a handler for edit intents routed from the host menu (selectAll, ...).
     * @param {function(string): void} handler - Called with the intent wire name.
     */
    onPerformEdit(handler) {
        this.#transport.addEventListener('input/performEdit', (params) => {
            handler(params?.command);
        });
    }

    /**
     * Registers a handler that returns the terminal's current selection text, fetched by the host for a
     * clipboard copy (host-mediated because the WebView's JS clipboard write is blocked on the Skia head).
     * @param {function(): string} handler - Returns the selected text.
     */
    onGetSelection(handler) {
        this.#transport.setRequestHandler('console/getSelection', () => handler());
    }

    /**
     * Reports which edit verbs the console can currently perform, so the host drives menu enable state.
     * @param {Object} availability - The current edit availability.
     */
    notifyEditAvailability(availability) {
        this.#transport.notify('input/editAvailabilityChanged', {
            canCopy: availability.canCopy === true,
            canCut: availability.canCut === true,
            canPaste: availability.canPaste === true,
            canSelectAll: availability.canSelectAll === true,
            canUndo: availability.canUndo === true,
            canRedo: availability.canRedo === true
        });
    }
}

// Export singleton instance
export const consoleClient = new ConsoleClient();
