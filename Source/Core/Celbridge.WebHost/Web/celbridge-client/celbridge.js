// Celbridge: JavaScript client for communicating with the Celbridge .NET host.
// Provides promise-based async API with automatic request/response correlation.

import { RpcTransport } from './core/rpc-transport.js';
import { DocumentAPI } from './api/document-api.js';
import { DialogAPI } from './api/dialog-api.js';
import { InputAPI } from './api/input-api.js';
import { createAppStateStore, createViewStateStore } from './core/state-store.js';
import { LocalizationAPI } from './api/localization-api.js';
import { ToolsAPI } from './api/tools-api.js';

/**
 * @typedef {import('./types.js').InitializeResult} InitializeResult
 * @typedef {import('./types.js').LogLevel} LogLevel
 */

/**
 * Celbridge Client.
 * Main entry point for communicating with the Celbridge .NET host.
 */
export class Celbridge {
    /** @type {RpcTransport} */
    #transport;

    /**
     * Document operations API.
     * @type {DocumentAPI}
     */
    document;

    /**
     * Dialog operations API.
     * @type {DialogAPI}
     */
    dialog;

    /**
     * Input events API.
     * @type {InputAPI}
     */
    input;

    /**
     * Read-only mirror of application-global host state (theme, ...). Exposed as `cel.appState`:
     * read `cel.appState.current.theme`, react with `cel.appState.onChanged(...)`.
     * @type {import('./core/state-store.js').Store}
     */
    #appState;

    /**
     * Read-only mirror of this view's own host state (a document's writability, ...). Exposed as
     * `cel.viewState`: read `cel.viewState.current.writable`, react with `cel.viewState.onChanged(...)`.
     * @type {import('./core/state-store.js').Store}
     */
    #viewState;

    /**
     * Localization events API.
     * @type {LocalizationAPI}
     */
    localization;

    /**
     * Host capability proxy (`cel.*`) and raw tool dispatch (`list`, `call`).
     * Populated from the package's `[permissions] tools` allowlist, which the
     * client fetches over the bridge via `host/getContext`.
     * @type {ToolsAPI}
     */
    tools;

    /**
     * Map of secrets supplied by the bundled package's C# descriptor (e.g. a domain-locked
     * library's license key). Delivered over the bridge via `host/getContext`. Always empty
     * for non-bundled packages.
     * @type {Readonly<Object<string, string>>}
     */
    secrets;

    /**
     * Package-defined options parsed from the `[options]` table of the editor's
     * document manifest. Keys and values are opaque to the Celbridge host. The
     * editor decides how to interpret them.
     * @type {Readonly<Object<string, string>>}
     */
    options;

    /**
     * Set to `false` to opt out of exposing the `cel` proxy on `globalThis` during
     * `initialize()`. Useful for embedding scenarios where the host page already
     * uses the name.
     * @type {boolean}
     */
    #exposeCelGlobal;

    /**
     * Whether the capability context has been resolved — fetched over the bridge via `host/getContext`,
     * or short-circuited by a context provided to the constructor (used by tests and embedders). Until
     * resolved, tools/secrets/options are empty.
     * @type {boolean}
     */
    #contextResolved = false;

    /**
     * In-flight `host/getContext` fetch, so concurrent `ready()` calls share one round-trip.
     * @type {Promise<void>|null}
     */
    #readyPromise = null;

    /**
     * Creates a new Celbridge instance.
     * @param {Object} [options] - Configuration options.
     * @param {Function} [options.postMessage] - Custom postMessage function (for testing).
     * @param {Function} [options.onMessage] - Custom message handler setup (for testing).
     * @param {number} [options.timeout] - Request timeout in milliseconds.
     * @param {Object} [options.context] - Injected capability context (for tests and embedders).
     *   Normally the host delivers it over the bridge via `host/getContext`.
     * @param {boolean} [options.exposeCelGlobal=true] - When `true` (the default),
     *   `initialize()` assigns the `cel.*` proxy to `globalThis.cel` so extensions
     *   can call `cel.namespace.method(...)` without importing the client.
     */
    constructor(options = {}) {
        this.#transport = new RpcTransport(options);

        // Initialize sub-APIs
        this.document = new DocumentAPI(this.#transport);
        this.dialog = new DialogAPI(this.#transport);
        this.input = new InputAPI(this.#transport);
        this.#appState = createAppStateStore(this.#transport);
        this.#viewState = createViewStateStore(this.#transport);
        this.localization = new LocalizationAPI(this.#transport);
        this.#exposeCelGlobal = options.exposeCelGlobal !== false;

        // Release the active element when the host signals that focus moved to another panel. Wired
        // universally so any editor's WebView caret stops when a native panel takes focus on heads
        // where WebView and host focus are not integrated (Skia).
        this.#transport.addEventListener('input/releaseFocus', () => {
            blurActiveElement();
        });

        // At runtime the host delivers the capability context over the bridge, so it stays empty here until
        // ready() fetches it via host/getContext. A context provided up front via constructor options
        // short-circuits that fetch and is read here — used by tests and embedders.
        const context = options.context ? normalizeContext(options.context) : null;
        this.#contextResolved = context !== null;
        this.#applyContext(context ?? normalizeContext(null));
    }

    /**
     * Sets the tools/secrets/options sub-APIs from a normalized capability context.
     * @param {{ permittedTools: ReadonlyArray<string>, secrets: Readonly<Object<string,string>>, options: Readonly<Object<string,string>> }} context
     */
    #applyContext(context) {
        this.tools = new ToolsAPI(this.#transport, context.permittedTools);
        this.secrets = context.secrets;
        this.options = context.options;
    }

    /**
     * Resolves the capability context before tools/secrets/options are read. Fetches the context over the
     * bridge via host/getContext (resolves immediately if a context was already provided to the
     * constructor). Editors must await this before reading `tools`, `secrets`, or `options`. Idempotent and
     * safe to call concurrently.
     * @returns {Promise<void>}
     */
    async ready() {
        if (this.#contextResolved) {
            return;
        }
        if (this.#readyPromise) {
            return this.#readyPromise;
        }

        this.#readyPromise = (async () => {
            try {
                const raw = await this.#transport.request('host/getContext', {});
                this.#applyContext(normalizeContext(raw));
                this.#contextResolved = true;
            } catch (error) {
                // Clear the cached promise so a later ready() can retry instead of returning this
                // permanently-rejected one, which would leave tools/secrets/options empty for the page.
                this.#readyPromise = null;
                throw error;
            }
        })();

        return this.#readyPromise;
    }

    /**
     * Whether this page is running inside the Celbridge host (vs. opened standalone in a plain browser).
     * Derived synchronously from the presence of a host transport, so editors can read it immediately to
     * decide whether to do host integration or run in a standalone/dev mode.
     * @returns {boolean}
     */
    get isHosted() {
        return this.#transport.isHosted;
    }

    /**
     * Convenience accessor for the `cel.*` tool proxy. Equivalent to `client.tools.cel`.
     * Throws synchronously with `CelToolError` if accessed before `initialize()` resolves —
     * the proxy is built from descriptors fetched during initialization.
     * @returns {Object}
     */
    get cel() {
        return this.tools.cel;
    }

    /**
     * The application-global state store (theme, ...), shared by every WebView. Read the latest snapshot
     * via `cel.appState.current.theme`; react with `cel.appState.onChanged(snapshot => ...)`. The host
     * pushes the current snapshot on connect.
     * @returns {import('./core/state-store.js').Store}
     */
    get appState() {
        return this.#appState;
    }

    /**
     * This view's own state store (a document's writability, ...). Read the latest snapshot via
     * `cel.viewState.current.writable`; react with `cel.viewState.onChanged(snapshot => ...)`. The host
     * pushes the current snapshot on connect, so a handler registered after connect still sees it.
     * @returns {import('./core/state-store.js').Store}
     */
    get viewState() {
        return this.#viewState;
    }

    /**
     * Sets the logging level.
     * @param {LogLevel} level - The log level.
     */
    setLogLevel(level) {
        this.#transport.setLogLevel(level);
    }

    /**
     * Initializes the client and loads document content.
     * Automatically loads localization strings from the extension's localization folder.
     * Must be called before any other operations.
     * @returns {Promise<InitializeResult>} - The initialization result with content and config.
     */
    async initialize() {
        if (this.#transport.isInitialized) {
            throw new Error('Client already initialized');
        }

        // Resolve the capability context first (fetched over the bridge via host/getContext) so the cel.*
        // tool allowlist is populated before loadDescriptors runs. Without it, an editor that calls
        // initialize() directly rather than initializeDocument() would see an empty allowlist and no cel.*
        // tools. A no-op when a context was already provided to the constructor.
        if (!this.#contextResolved) {
            await this.ready();
        }

        const result = await this.#transport.request('document/initialize', {
            protocolVersion: RpcTransport.protocolVersion
        });

        this.#transport.markInitialized();

        // Load tool descriptors so the `cel.*` proxy becomes callable.
        // One round-trip to the host. Before this resolves, accessing `cel.*` throws.
        await this.tools.loadDescriptors();

        // Expose `cel` on globalThis for the "just call cel.namespace.method(...)"
        // agent experience. Opt out with `new Celbridge({ exposeCelGlobal: false })`.
        if (this.#exposeCelGlobal && typeof globalThis !== 'undefined') {
            globalThis.cel = this.tools.cel;
        }

        // Auto-load localization if locale is provided in metadata
        if (result.metadata?.locale) {
            await this.localization.loadStrings(result.metadata.locale);
        }

        return result;
    }

    /**
     * Initializes the client, loads content, registers handlers, and signals readiness.
     * This is the recommended way to set up a document editor — it ensures notifyContentLoaded()
     * is always called after content loading and handler registration complete.
     *
     * Editor state contract: the string returned by `onRequestState` must survive both
     * external-reload cycles (host calls `onRequestState` -> replaces content -> calls
     * `onRestoreState` with the same string) and session restore (host persists the string
     * as EditorStateJson and replays it on the next session). Editors define their
     * own schema for this string. The host treats it as opaque. Anything the editor needs
     * to reconstruct view state (scroll position, selection, pending unsaved edits, etc.)
     * must be encoded here.
     *
     * @param {Object} handlers - Handler callbacks for document events.
     * @param {Function} [handlers.onContent] - Called with (content, metadata) after initialization.
     * @param {Function} [handlers.onRequestSave] - Called when the host requests a save.
     * @param {Function} [handlers.onExternalChange] - Called when the file changes externally.
     * @param {Function} [handlers.onRequestState] - Called when the host requests editor state. Should return a string or null.
     *   The returned string must round-trip through `onRestoreState` with equivalent editor behavior.
     * @param {Function} [handlers.onRestoreState] - Called with a state string previously returned
     *   from `onRequestState` to restore editor view state.
     * @returns {Promise<InitializeResult>} - The initialization result with content and config.
     */
    async initializeDocument(handlers = {}) {
        // Resolve the capability context before the handshake (fetched over the bridge via host/getContext)
        // so the cel.* proxy, secrets, and options are ready. A no-op when a context was already provided to
        // the constructor.
        if (!this.#contextResolved) {
            await this.ready();
        }

        const result = await this.initialize();

        if (handlers.onContent) {
            await handlers.onContent(result.content, result.metadata);
        }
        if (handlers.onRequestSave) {
            this.document.onRequestSave(handlers.onRequestSave);
        }
        if (handlers.onExternalChange) {
            this.document.onExternalChange(handlers.onExternalChange);
        }
        if (handlers.onRequestState) {
            this.document.onRequestState(handlers.onRequestState);
        }
        if (handlers.onRestoreState) {
            this.document.onRestoreState(handlers.onRestoreState);
        }

        this.document.notifyContentLoaded();
        return result;
    }

    /**
     * Registers a handler for a custom host-to-client notification (fire-and-forget).
     * Use this when a package needs to receive RPC calls beyond the standard lifecycle —
     * e.g., editor-specific operations like navigate-to-location or apply-edits.
     * The handler receives the full params object.
     * @param {string} method - The RPC method name (e.g. 'editor/navigateToLocation').
     * @param {Function} handler - Called with the notification params object.
     */
    onNotification(method, handler) {
        this.#transport.addEventListener(method, handler);
    }

    /**
     * Registers a handler for a custom host-to-client request (the host expects a return value).
     * Use this for host-driven queries beyond the standard lifecycle — e.g. the host fetching the
     * editor's current selection for a clipboard operation. The handler's return value (or resolved
     * promise) is sent back to the host as the response.
     * @param {string} method - The RPC method name (e.g. 'editor/getSelectedText').
     * @param {Function} handler - Called with the request params; returns the response value.
     */
    onRequest(method, handler) {
        this.#transport.setRequestHandler(method, handler);
    }

    /**
     * Internal method to send requests (used by sub-modules).
     * @param {string} method - The method name.
     * @param {Object} params - The request parameters.
     * @returns {Promise<any>}
     */
    _request(method, params) {
        return this.#transport.request(method, params);
    }

    /**
     * Internal method to send notifications (used by sub-modules).
     * @param {string} method - The method name.
     * @param {Object} params - The notification parameters.
     */
    _notify(method, params) {
        this.#transport.notify(method, params);
    }
}

/**
 * Normalizes a raw capability context (from constructor options or the host/getContext
 * response) into frozen `permittedTools`/`secrets`/`options`. A null/empty input yields an
 * empty default (no tools, no secrets, no options).
 * @param {Object|null} raw
 * @returns {{ permittedTools: ReadonlyArray<string>, secrets: Readonly<Object<string, string>>, options: Readonly<Object<string, string>> }}
 */
function normalizeContext(raw) {
    const permittedTools = Array.isArray(raw?.permittedTools)
        ? Object.freeze([...raw.permittedTools])
        : Object.freeze([]);

    const secrets = readStringMap(raw?.secrets);
    const options = readStringMap(raw?.options);

    return {
        permittedTools,
        secrets: Object.freeze(secrets),
        options: Object.freeze(options)
    };
}

/**
 * Blurs the document's active element so an editor's caret stops when focus moves to another panel.
 * No-ops outside a browser (e.g. the test environment) or when nothing is focused.
 */
function blurActiveElement() {
    if (typeof document === 'undefined') {
        return;
    }
    const active = document.activeElement;
    if (active && active !== document.body && typeof active.blur === 'function') {
        active.blur();
    }
}

function readStringMap(source) {
    const result = {};
    if (source && typeof source === 'object') {
        for (const [key, value] of Object.entries(source)) {
            if (typeof value === 'string') {
                result[key] = value;
            }
        }
    }
    return result;
}

// Lazy singleton instance for typical usage
// Uses a proxy to defer instantiation until first property access,
// avoiding errors in non-browser test environments.
let _instance = null;

const celbridge = new Proxy({}, {
    get(_, prop) {
        if (!_instance) {
            _instance = new Celbridge();
        }
        const value = _instance[prop];
        return typeof value === 'function' ? value.bind(_instance) : value;
    }
});

// Default export is the singleton instance
export default celbridge;

// Re-export types for convenience
export * from './types.js';
