// RPC Transport: JSON-RPC 2.0 communication layer for Celbridge.
// Handles low-level message passing between JavaScript clients and the .NET host.
//
// Transport hierarchy: the host channel is normally a WebSocket to the loopback file server, learned two
// ways. A loopback page derives a same-origin ws(s):// URL from a connection token in its own URL. A
// synthetic-origin page (a faked origin that is not the loopback server) cannot derive it, so it reads the
// full ws:// URL the host provides -- from window.__hostChannelUrl (injected into the loadHTMLString page)
// or a __hostChannelUrl query parameter (on the virtual-host page). The other path is a WebView2
// native-message fallback (chrome.webview / webkit.messageHandlers), used by a page that loads this client
// over the native message channel rather than a WebSocket. (Tests inject their own postMessage/onMessage
// pair, which always wins.)

/**
 * @typedef {'none' | 'error' | 'warn' | 'debug'} LogLevel
 */

const PROTOCOL_VERSION = '1.0';
const DEFAULT_TIMEOUT_MS = 30000;

/**
 * JSON-RPC 2.0 Transport Layer.
 * Manages communication between JavaScript and the .NET host.
 */
export class RpcTransport {
    /** @type {Map<number, { resolve: Function, reject: Function, method: string, startTime: number }>} */
    #pendingRequests = new Map();

    /** @type {number} */
    #nextId = 1;

    /** @type {LogLevel} */
    #logLevel = 'error';

    /** @type {number} */
    #timeoutMs = DEFAULT_TIMEOUT_MS;

    /** @type {Function} */
    #postMessage;

    /** @type {boolean} */
    #initialized = false;

    /** @type {boolean} */
    #isHosted = false;

    /** @type {Object<string, Function[]>} */
    #eventHandlers = {};

    /** @type {Object<string, Function>} */
    #requestHandlers = {};

    /**
     * Gets the protocol version.
     * @returns {string}
     */
    static get protocolVersion() {
        return PROTOCOL_VERSION;
    }

    /**
     * Creates a new RpcTransport instance.
     * @param {Object} [options] - Configuration options.
     * @param {Function} [options.postMessage] - Custom postMessage function (for testing).
     * @param {Function} [options.onMessage] - Custom message handler setup (for testing).
     * @param {number} [options.timeout] - Request timeout in milliseconds.
     */
    constructor(options = {}) {
        this.#timeoutMs = options.timeout ?? DEFAULT_TIMEOUT_MS;

        // Tests inject their own transport via postMessage/onMessage. That always wins.
        if (options.postMessage || options.onMessage) {
            this.#postMessage = options.postMessage ?? defaultWebViewPostMessage;
            const setupListener = options.onMessage ?? defaultWebViewSetupListener;
            setupListener((data) => this.#handleMessage(data));
            this.#isHosted = true;
        } else {
            // The host selects the WebSocket transport in one of two ways. A synthetic-origin page
            // (served under its faked origin so its derived URL would not reach the loopback server) reads
            // the full host channel URL the host provides (a window global or a query parameter). A normal
            // loopback page instead carries a connection token in its URL and derives the same-origin
            // ws:// URL from it. Either way the WebSocket survives the WebView's view attachment. With
            // neither present, fall back to the WebView2 messaging transport if a native messaging bridge
            // is present. Otherwise this page is running standalone (outside the Celbridge host).
            const injectedHostChannelUrl = options.wsUrl ?? readInjectedHostChannelUrl();
            const hostToken = options.wsToken ?? readHostToken();
            if (injectedHostChannelUrl) {
                this.#setupWebSocketTransport(injectedHostChannelUrl);
                this.#isHosted = true;
            } else if (hostToken) {
                const scheme = (typeof location !== 'undefined' && location.protocol === 'https:') ? 'wss' : 'ws';
                const host = (typeof location !== 'undefined' && location.host) ? location.host : '127.0.0.1';
                this.#setupWebSocketTransport(`${scheme}://${host}/ws/host?token=${encodeURIComponent(hostToken)}`);
                this.#isHosted = true;
            } else {
                this.#postMessage = defaultWebViewPostMessage;
                defaultWebViewSetupListener((data) => this.#handleMessage(data));
                this.#isHosted = hasNativeWebViewBridge();
            }
        }

        // Expose the active transport's raw send so a client-independent injected script can reach the
        // host over whichever transport this page uses, rather than assuming chrome.webview. Pages that
        // never load this client fall back to chrome.webview.
        if (typeof globalThis !== 'undefined') {
            globalThis.__hostSendMessage = (json) => this.#postMessage(json);
        }
    }

    /**
     * Routes the bridge over a WebSocket on the loopback server at the given URL. The token in the URL
     * both routes the socket to this document's host channel and authenticates it. Outbound messages
     * are buffered until the socket opens. No automatic reconnection: the socket lives for the page's
     * lifetime.
     * @param {string} url - The full ws:// URL (same-origin-derived, or host-injected for synthetic-origin pages).
     */
    #setupWebSocketTransport(url) {
        const outboundQueue = [];
        const socket = new WebSocket(url);

        socket.addEventListener('open', () => {
            this.#log('debug', 'Host WebSocket connected');
            while (outboundQueue.length > 0 && socket.readyState === WebSocket.OPEN) {
                socket.send(outboundQueue.shift());
            }
        });
        socket.addEventListener('message', (event) => {
            this.#handleMessage(event.data);
        });
        socket.addEventListener('close', () => {
            this.#log('warn', 'Host WebSocket closed');
        });
        socket.addEventListener('error', (event) => {
            this.#log('error', 'Host WebSocket error', event);
        });

        this.#postMessage = (message) => {
            if (socket.readyState === WebSocket.OPEN) {
                socket.send(message);
            } else {
                // Buffer until the socket opens (still connecting), then flush on 'open'.
                outboundQueue.push(message);
            }
        };
    }

    /**
     * Gets whether the transport has been initialized.
     * @returns {boolean}
     */
    get isInitialized() {
        return this.#initialized;
    }

    /**
     * Whether a Celbridge host transport is present (a WebSocket bridge, or a native WebView messaging
     * bridge). False when the page is running standalone in a plain browser. Determined synchronously at
     * construction. The basis for `celbridge.isHosted`.
     * @returns {boolean}
     */
    get isHosted() {
        return this.#isHosted;
    }

    /**
     * Marks the transport as initialized.
     */
    markInitialized() {
        this.#initialized = true;
    }

    /**
     * Sets the logging level.
     * @param {LogLevel} level - The log level.
     */
    setLogLevel(level) {
        this.#logLevel = level;
    }

    /**
     * Sends a JSON-RPC request and waits for a response.
     * @template T
     * @param {string} method - The method name.
     * @param {Object} [params] - The request parameters.
     * @returns {Promise<T>} - The response result.
     */
    request(method, params = {}) {
        return new Promise((resolve, reject) => {
            const id = this.#nextId++;
            const startTime = Date.now();

            // Set up timeout
            const timeoutId = setTimeout(() => {
                if (this.#pendingRequests.has(id)) {
                    this.#pendingRequests.delete(id);
                    const error = new Error(`Request timeout: ${method} (${this.#timeoutMs}ms)`);
                    error.code = -32000;
                    this.#log('warn', `Request #${id} timed out: ${method}`);
                    reject(error);
                }
            }, this.#timeoutMs);

            this.#pendingRequests.set(id, {
                resolve: (result) => {
                    clearTimeout(timeoutId);
                    resolve(result);
                },
                reject: (error) => {
                    clearTimeout(timeoutId);
                    reject(error);
                },
                method,
                startTime
            });

            const message = {
                jsonrpc: '2.0',
                method,
                params,
                id
            };

            this.#log('debug', `-> request #${id}: ${method}`, params);
            this.#postMessage(JSON.stringify(message));
        });
    }

    /**
     * Sends a notification (no response expected).
     * @param {string} method - The method name.
     * @param {Object} [params] - The notification parameters.
     */
    notify(method, params = {}) {
        const message = {
            jsonrpc: '2.0',
            method,
            params
        };

        this.#log('debug', `-> notification: ${method}`, params);
        this.#postMessage(JSON.stringify(message));
    }

    /**
     * Registers an event handler for host notifications.
     * @param {string} method - The notification method name.
     * @param {Function} handler - The handler function.
     */
    addEventListener(method, handler) {
        if (!this.#eventHandlers[method]) {
            this.#eventHandlers[method] = [];
        }
        this.#eventHandlers[method].push(handler);
    }

    /**
     * Registers a handler for incoming RPC requests from the host.
     * Unlike event handlers (for notifications), request handlers return a value
     * that is sent back to the host as a response. Only one handler per method is supported.
     * @param {string} method - The request method name.
     * @param {Function} handler - The handler function. May return a value or a Promise.
     */
    setRequestHandler(method, handler) {
        this.#requestHandlers[method] = handler;
    }

    /**
     * Removes an event handler.
     * @param {string} method - The notification method name.
     * @param {Function} handler - The handler function to remove.
     */
    removeEventListener(method, handler) {
        const handlers = this.#eventHandlers[method];
        if (handlers) {
            const index = handlers.indexOf(handler);
            if (index !== -1) {
                handlers.splice(index, 1);
            }
        }
    }

    /**
     * Handles incoming messages from the host.
     * @param {string} data - The raw message data.
     */
    #handleMessage(data) {
        try {
            const message = typeof data === 'string' ? JSON.parse(data) : data;

            // Response (has id and either result or error)
            if ('id' in message && (('result' in message) || ('error' in message))) {
                this.#handleResponse(message);
                return;
            }

            // Request from host (has both method and id, expects a response)
            if ('method' in message && 'id' in message) {
                this.#handleRequest(message);
                return;
            }

            // Notification (has method but no id)
            if ('method' in message && !('id' in message)) {
                this.#handleNotification(message);
                return;
            }

            this.#log('warn', 'Unknown message format', message);
        } catch (error) {
            this.#log('error', 'Error parsing message', error);
        }
    }

    /**
     * Handles a response message.
     * @param {Object} message - The parsed message.
     */
    #handleResponse(message) {
        const pending = this.#pendingRequests.get(message.id);
        if (!pending) {
            this.#log('warn', `No pending request for id: ${message.id}`);
            return;
        }

        this.#pendingRequests.delete(message.id);
        const elapsed = Date.now() - pending.startTime;

        if ('error' in message) {
            this.#log('debug', `<- response #${message.id}: error (${elapsed}ms)`, message.error);
            const error = new Error(message.error.message);
            error.code = message.error.code;
            error.data = message.error.data;
            pending.reject(error);
        } else {
            this.#log('debug', `<- response #${message.id}: success (${elapsed}ms)`);
            pending.resolve(message.result);
        }
    }

    /**
     * Handles a notification message from the host.
     * @param {Object} message - The parsed message.
     */
    #handleNotification(message) {
        const { method, params } = message;
        this.#log('debug', `<- notification: ${method}`, params);

        const handlers = this.#eventHandlers[method];
        if (handlers) {
            for (const handler of handlers) {
                try {
                    handler(params);
                } catch (error) {
                    this.#log('error', `Error in notification handler for ${method}`, error);
                }
            }
        }
    }

    /**
     * Handles an incoming request from the host that expects a response.
     * @param {Object} message - The parsed message with method, id, and optional params.
     */
    async #handleRequest(message) {
        const { method, id, params } = message;
        this.#log('debug', `<- request #${id}: ${method}`, params);

        const handler = this.#requestHandlers[method];
        if (!handler) {
            const errorResponse = {
                jsonrpc: '2.0',
                id,
                error: { code: -32601, message: `Method not found: ${method}` }
            };
            this.#postMessage(JSON.stringify(errorResponse));
            return;
        }

        try {
            const result = await handler(params);
            const response = {
                jsonrpc: '2.0',
                id,
                result: result !== undefined ? result : null
            };
            this.#postMessage(JSON.stringify(response));
        } catch (error) {
            const errorResponse = {
                jsonrpc: '2.0',
                id,
                error: { code: -32000, message: error.message || 'Handler error' }
            };
            this.#postMessage(JSON.stringify(errorResponse));
        }
    }

    /**
     * Logs a message based on the current log level.
     * @param {LogLevel} level
     * @param {string} message
     * @param {any} [data]
     */
    #log(level, message, data) {
        const levels = { none: 0, error: 1, warn: 2, debug: 3 };
        if (levels[level] > levels[this.#logLevel]) {
            return;
        }

        const prefix = '[RpcTransport]';
        const fullMessage = `${prefix} ${message}`;

        switch (level) {
            case 'error':
                console.error(fullMessage, data !== undefined ? data : '');
                break;
            case 'warn':
                console.warn(fullMessage, data !== undefined ? data : '');
                break;
            case 'debug':
                console.log(fullMessage, data !== undefined ? data : '');
                break;
        }
    }
}

/**
 * Whether a native WebView messaging bridge is present — `chrome.webview` (WebView2) or the Uno Skia
 * `webkit.messageHandlers.unoWebView` (WKWebView). Absent in a plain browser, so this is how a page with
 * no WebSocket token tells "inside the Celbridge host" from "running standalone".
 * @returns {boolean}
 */
function hasNativeWebViewBridge() {
    if (typeof window === 'undefined') {
        return false;
    }
    return Boolean(window.chrome?.webview)
        || Boolean(window.webkit?.messageHandlers?.unoWebView);
}

/**
 * The WebView2 messaging send path (the fallback transport when no WebSocket token is present).
 * @param {string} message
 */
function defaultWebViewPostMessage(message) {
    if (window.chrome?.webview) {
        window.chrome.webview.postMessage(message);
    } else if (window.webkit?.messageHandlers?.unoWebView) {
        // macOS WKWebView (Uno Skia): chrome.webview is absent. Route JS->C# through the native
        // message handler, which surfaces on the host as CoreWebView2.WebMessageReceived (the same
        // event the chrome.webview path raises).
        window.webkit.messageHandlers.unoWebView.postMessage(message);
    }
}

/**
 * The WebView2 messaging receive path (the fallback transport when no WebSocket token is present).
 * @param {Function} handler
 */
function defaultWebViewSetupListener(handler) {
    if (window.chrome?.webview) {
        window.chrome.webview.addEventListener('message', (event) => {
            handler(event.data);
        });
    }

    // C#->JS dispatch entry point for heads where PostWebMessageAsString does not deliver (Uno Skia):
    // the host pushes messages by invoking this global via ExecuteScriptAsync. Heads that use
    // chrome.webview messaging never call it.
    if (typeof globalThis !== 'undefined') {
        globalThis.__hostReceiveMessage = (data) => handler(data);
    }
}

/**
 * Reads the host connection token the host embedded in the page URL, or null when absent (the host did
 * not select the WebSocket transport, or this is a non-browser test environment).
 * @returns {string|null}
 */
function readHostToken() {
    if (typeof location === 'undefined' || !location.search) {
        return null;
    }

    try {
        return new URLSearchParams(location.search).get('__hostToken');
    } catch {
        return null;
    }
}

/**
 * Reads the full host channel URL for a synthetic-origin page, whose faked origin means it cannot derive
 * the loopback ws:// URL from its own location. The macOS loadHTMLString page reads a host-injected global;
 * the virtual-host page (which cannot receive a document-start global on the Skia WebView2) reads it from a
 * query parameter on its own URL. Null when absent.
 * @returns {string|null}
 */
function readInjectedHostChannelUrl() {
    if (typeof globalThis !== 'undefined') {
        const url = globalThis.__hostChannelUrl;
        if (typeof url === 'string' && url.length > 0) {
            return url;
        }
    }

    if (typeof location !== 'undefined' && location.search) {
        try {
            return new URLSearchParams(location.search).get('__hostChannelUrl');
        } catch {
            return null;
        }
    }

    return null;
}
