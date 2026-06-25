// State store: a read-only mirror of host state. The host is the sole writer and pushes a full snapshot to
// every connected WebView — on connect and on every change. The client never asks; registering the change
// listener (in the constructor) is the whole subscription. There is no per-key subscription or delta
// protocol — each update is the whole (small) state object. Only the latest snapshot is retained.
//
// One primitive used at two scopes, structurally identical and distinguished only by the RPC method each
// rides: one app-global store shared by every WebView (theme, and later locale, flags) and one store per
// document view carrying that view's own state (a document's writability). The host seeds each store before
// the view connects, so a late subscriber still sees the current value: onChanged replays the latest
// snapshot to the handler on registration.

/**
 * @typedef {import('../types.js').WebViewTheme} WebViewTheme
 */

export class Store {
    /** @type {Object<string, string>} */
    #latest = {};

    /** @type {boolean} */
    #hasSnapshot = false;

    /** @type {Function[]} */
    #handlers = [];

    #transport;

    /** @type {((snapshot: Object<string, string>) => void) | undefined} */
    #onApply;

    /**
     * @param {object} transport - The RpcTransport this WebView talks to the host over.
     * @param {string} changedMethod - The host-to-client notification this store mirrors (e.g. 'appState/changed').
     * @param {(snapshot: Object<string, string>) => void} [onApply] - Optional side effect run on each
     *   snapshot before handlers (used by the app store to mirror the theme onto html[data-theme]).
     */
    constructor(transport, changedMethod, onApply) {
        this.#transport = transport;
        this.#onApply = onApply;
        this.#transport.addEventListener(changedMethod, (snapshot) => this.#apply(snapshot || {}));
    }

    /**
     * The latest full snapshot, e.g. `{ theme: 'Dark' }`. Empty until the first snapshot arrives. Read
     * keys directly: `cel.appState.current.theme`.
     * @returns {Object<string, string>}
     */
    get current() {
        return this.#latest;
    }

    /**
     * Registers a handler called with the full snapshot whenever it changes. If a snapshot has already
     * arrived, the handler is invoked immediately with it, so a handler registered after the host's
     * connect-time push still sees the current value. Either way the handler runs once per snapshot.
     * @param {(snapshot: Object<string, string>) => void} handler
     */
    onChanged(handler) {
        this.#handlers.push(handler);
        if (this.#hasSnapshot) {
            this.#invoke(handler, this.#latest);
        }
    }

    /**
     * @param {Object<string, string>} snapshot
     */
    #apply(snapshot) {
        this.#latest = snapshot;
        this.#hasSnapshot = true;
        if (this.#onApply) {
            this.#onApply(snapshot);
        }
        for (const handler of this.#handlers) {
            this.#invoke(handler, snapshot);
        }
    }

    /**
     * @param {Function} handler
     * @param {Object<string, string>} snapshot
     */
    #invoke(handler, snapshot) {
        try {
            handler(snapshot);
        } catch (error) {
            console.error('[Store] Error in change handler:', error);
        }
    }
}

/**
 * Creates the app-global state store, shared by every WebView. Mirrors the theme onto html[data-theme] so
 * attribute-keyed editor CSS follows it with no JS of its own.
 * @param {object} transport
 * @returns {Store}
 */
export function createAppStateStore(transport) {
    return new Store(transport, 'appState/changed', (snapshot) => applyDataTheme(snapshot.theme));
}

/**
 * Creates a per-view state store (e.g. a document's writability).
 * @param {object} transport
 * @returns {Store}
 */
export function createViewStateStore(transport) {
    return new Store(transport, 'viewState/changed');
}

/**
 * Mirrors the theme onto <html data-theme> so attribute-keyed editor CSS follows it.
 * @param {WebViewTheme|undefined} theme
 */
function applyDataTheme(theme) {
    if ((theme === 'Dark' || theme === 'Light') && typeof document !== 'undefined' && document.documentElement) {
        document.documentElement.dataset.theme = theme === 'Dark' ? 'dark' : 'light';
    }
}
