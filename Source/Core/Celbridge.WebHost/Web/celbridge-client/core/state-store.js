// State store: a read-only mirror of host state. The host is the sole writer and pushes a full snapshot to
// every connected WebView — on connect and on every change. The client never asks. Registering the change
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
    return new Store(transport, 'appState/changed', (snapshot) => {
        applyDataTheme(snapshot.theme);
        applyPageZoom(snapshot.rasterizationScale);
    });
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

// The web engine renders at its own rasterization scale, which on Windows folds in the accessibility text
// scale, so a CSS pixel here is larger than a device-independent pixel in the native chrome. devicePixelRatio
// carries the host's scale and the engine's extra factor together. Dividing out the host's scale leaves the
// factor the native-mirroring dimensions divide by (see celbridge-tokens.css).

// Outside this range the derived factor is not a plausible scale, so dimensions keep their declared sizes
// rather than trust it.
const MIN_PAGE_ZOOM = 0.5;
const MAX_PAGE_ZOOM = 4;

/** @type {number} */
let hostRasterizationScale = 0;

/** @type {MediaQueryList|null} */
let resolutionQuery = null;

/**
 * Mirrors the derived page zoom onto the root element as --cel-page-zoom.
 * @param {string|undefined} rasterizationScale - The host's own rasterization scale, from the snapshot.
 */
function applyPageZoom(rasterizationScale) {
    const scale = Number.parseFloat(rasterizationScale ?? '');
    if (Number.isFinite(scale) && scale > 0) {
        hostRasterizationScale = scale;
    }

    updatePageZoom();
    watchResolution();
}

function updatePageZoom() {
    if (typeof document === 'undefined' || !document.documentElement) {
        return;
    }

    document.documentElement.style.setProperty('--cel-page-zoom', String(derivePageZoom()));
}

/**
 * @returns {number} The factor the engine applied on top of the host's rasterization scale, or 1 when it
 *   cannot be derived.
 */
function derivePageZoom() {
    if (hostRasterizationScale <= 0 || typeof window === 'undefined') {
        return 1;
    }

    const pageZoom = window.devicePixelRatio / hostRasterizationScale;
    if (!Number.isFinite(pageZoom) || pageZoom < MIN_PAGE_ZOOM || pageZoom > MAX_PAGE_ZOOM) {
        return 1;
    }

    return pageZoom;
}

// devicePixelRatio changes when the text scale changes or the window moves to a monitor at another scale, and
// has no change event of its own. A resolution query re-armed at each new ratio stands in for one. A monitor
// move also changes the host's scale, so this can fire on a stale one. The snapshot that follows corrects it.
function watchResolution() {
    if (typeof window === 'undefined' || !window.matchMedia) {
        return;
    }

    if (resolutionQuery) {
        resolutionQuery.removeEventListener('change', onResolutionChanged);
    }

    resolutionQuery = window.matchMedia(`(resolution: ${window.devicePixelRatio}dppx)`);
    resolutionQuery.addEventListener('change', onResolutionChanged);
}

function onResolutionChanged() {
    updatePageZoom();
    watchResolution();
}
