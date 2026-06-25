// App state: a read-only mirror of application-global host state (theme, and later locale, flags).
//
// The host is the sole writer and pushes a full snapshot to every connected WebView — on connect and on
// every change (appState/changed). The client never asks; registering the listener (in the constructor)
// is the whole subscription. Application-global values are shared by every WebView; per-view values
// (e.g. a document's writability) ride a separate view-state store. There is no per-key subscription or
// delta protocol — each update is the whole (small) state object. Only the latest snapshot is retained.
//
// One convenience: whenever the snapshot carries a `theme`, it is mirrored onto
// <html data-theme="dark|light"> so CSS-driven editors theme off the attribute with no JS of their own.

/**
 * @typedef {import('../types.js').WebViewTheme} WebViewTheme
 */

export class AppState {
    /** @type {Object<string, string>} */
    #latest = {};

    /** @type {Function[]} */
    #handlers = [];

    #transport;

    /**
     * @param {object} transport - The RpcTransport this WebView talks to the host over.
     */
    constructor(transport) {
        this.#transport = transport;
        this.#transport.addEventListener('appState/changed', (snapshot) => this.#apply(snapshot || {}));
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
     * Registers a handler called with the full snapshot whenever it changes. The host pushes the
     * current snapshot on connect, so a handler registered at startup also receives the initial values.
     * @param {(snapshot: Object<string, string>) => void} handler
     */
    onChanged(handler) {
        this.#handlers.push(handler);
    }

    /**
     * @param {Object<string, string>} snapshot
     */
    #apply(snapshot) {
        this.#latest = snapshot;
        applyDataTheme(snapshot.theme);
        for (const handler of this.#handlers) {
            try {
                handler(snapshot);
            } catch (error) {
                console.error('[AppState] Error in change handler:', error);
            }
        }
    }
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
