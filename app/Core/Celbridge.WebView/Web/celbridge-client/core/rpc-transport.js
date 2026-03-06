// RPC Transport: JSON-RPC 2.0 communication layer for Celbridge.
// Handles low-level message passing between JavaScript clients and the .NET host.

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

    /** @type {Object<string, Function[]>} */
    #eventHandlers = {};

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

        // Use provided postMessage or default to WebView2's
        this.#postMessage = options.postMessage ?? ((msg) => {
            if (window.chrome?.webview) {
                window.chrome.webview.postMessage(msg);
            }
        });

        // Set up message listener
        const setupListener = options.onMessage ?? ((handler) => {
            if (window.chrome?.webview) {
                window.chrome.webview.addEventListener('message', (event) => {
                    handler(event.data);
                });
            }
        });

        setupListener((data) => this.#handleMessage(data));
    }

    /**
     * Gets whether the transport has been initialized.
     * @returns {boolean}
     */
    get isInitialized() {
        return this.#initialized;
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
