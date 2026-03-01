// WebView Bridge: JSON-RPC 2.0 communication layer for Celbridge WebView2 editors.
// Provides promise-based async API with automatic request/response correlation.

/**
 * @typedef {Object} DocumentMetadata
 * @property {string} filePath - Full path to the file on disk.
 * @property {string} resourceKey - The resource key in the project.
 * @property {string} fileName - The file name only.
 */

/**
 * @typedef {Object} ThemeInfo
 * @property {string} name - Theme name (e.g., "Light", "Dark").
 * @property {boolean} isDark - Whether the theme is a dark theme.
 */

/**
 * @typedef {Object} InitializeResult
 * @property {string} content - The document content.
 * @property {DocumentMetadata} metadata - Document metadata.
 * @property {Object<string, string>} localization - Localized strings dictionary.
 * @property {ThemeInfo} theme - Current theme information.
 */

/**
 * @typedef {Object} LoadResult
 * @property {string} content - The document content from disk.
 * @property {DocumentMetadata} [metadata] - Optional metadata if requested.
 */

/**
 * @typedef {Object} SaveResult
 * @property {boolean} success - Whether the save succeeded.
 * @property {string} [error] - Error message if save failed.
 */

/**
 * @typedef {'none' | 'error' | 'warn' | 'debug'} LogLevel
 */

const PROTOCOL_VERSION = '1.0';
const DEFAULT_TIMEOUT_MS = 30000;

/**
 * JSON-RPC 2.0 WebView Bridge.
 * Manages communication between WebView JavaScript and the C# host.
 */
class WebViewBridge {
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
     * Theme events API.
     * @type {ThemeAPI}
     */
    theme;

    /**
     * Localization events API.
     * @type {LocalizationAPI}
     */
    localization;

    /**
     * Creates a new WebViewBridge instance.
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

        // Initialize sub-APIs
        this.document = new DocumentAPI(this);
        this.dialog = new DialogAPI(this);
        this.theme = new ThemeAPI(this);
        this.localization = new LocalizationAPI(this);
    }

    /**
     * Sets the logging level.
     * @param {LogLevel} level - The log level.
     */
    setLogLevel(level) {
        this.#logLevel = level;
    }

    /**
     * Initializes the bridge and loads document content.
     * Must be called before any other operations.
     * @returns {Promise<InitializeResult>} - The initialization result with content and config.
     */
    async initialize() {
        if (this.#initialized) {
            throw new Error('Bridge already initialized');
        }

        const result = await this.#sendRequest('bridge/initialize', {
            protocolVersion: PROTOCOL_VERSION
        });

        this.#initialized = true;
        return result;
    }

    /**
     * Sends a JSON-RPC request and waits for a response.
     * @template T
     * @param {string} method - The method name.
     * @param {Object} [params] - The request parameters.
     * @returns {Promise<T>} - The response result.
     */
    #sendRequest(method, params = {}) {
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

            this.#log('debug', `→ request #${id}: ${method}`, params);
            this.#postMessage(JSON.stringify(message));
        });
    }

    /**
     * Sends a notification (no response expected).
     * @param {string} method - The method name.
     * @param {Object} [params] - The notification parameters.
     */
    #sendNotification(method, params = {}) {
        const message = {
            jsonrpc: '2.0',
            method,
            params
        };
        // No id field for notifications

        this.#log('debug', `→ notification: ${method}`, params);
        this.#postMessage(JSON.stringify(message));
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
            this.#log('debug', `← response #${message.id}: error (${elapsed}ms)`, message.error);
            const error = new Error(message.error.message);
            error.code = message.error.code;
            error.data = message.error.data;
            pending.reject(error);
        } else {
            this.#log('debug', `← response #${message.id}: success (${elapsed}ms)`);
            pending.resolve(message.result);
        }
    }

    /**
     * Handles a notification message from the host.
     * @param {Object} message - The parsed message.
     */
    #handleNotification(message) {
        const { method, params } = message;
        this.#log('debug', `← notification: ${method}`, params);

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
     * Registers an event handler for host notifications.
     * @param {string} method - The notification method name.
     * @param {Function} handler - The handler function.
     */
    _addEventListener(method, handler) {
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
    _removeEventListener(method, handler) {
        const handlers = this.#eventHandlers[method];
        if (handlers) {
            const index = handlers.indexOf(handler);
            if (index !== -1) {
                handlers.splice(index, 1);
            }
        }
    }

    /**
     * Internal method to send requests (used by sub-APIs).
     * @param {string} method
     * @param {Object} params
     * @returns {Promise<any>}
     */
    _request(method, params) {
        return this.#sendRequest(method, params);
    }

    /**
     * Internal method to send notifications (used by sub-APIs).
     * @param {string} method
     * @param {Object} params
     */
    _notify(method, params) {
        this.#sendNotification(method, params);
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

        const prefix = '[WebViewBridge]';
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
 * Document operations API.
 */
class DocumentAPI {
    /** @type {WebViewBridge} */
    #bridge;

    /**
     * @param {WebViewBridge} bridge
     */
    constructor(bridge) {
        this.#bridge = bridge;
    }

    /**
     * Loads the current document content from disk.
     * @param {Object} [options]
     * @param {boolean} [options.includeMetadata=false] - Whether to include metadata.
     * @returns {Promise<LoadResult>}
     */
    async load(options = {}) {
        return this.#bridge._request('document/load', {
            includeMetadata: options.includeMetadata ?? false
        });
    }

    /**
     * Saves the document content.
     * @param {string} content - The content to save.
     * @returns {Promise<SaveResult>}
     */
    async save(content) {
        return this.#bridge._request('document/save', { content });
    }

    /**
     * Gets the document metadata.
     * @returns {Promise<DocumentMetadata>}
     */
    async getMetadata() {
        return this.#bridge._request('document/getMetadata', {});
    }

    /**
     * Notifies the host that the document content has changed.
     * This is a notification (no response expected).
     */
    notifyChanged() {
        this.#bridge._notify('document/changed', {});
    }

    /**
     * Registers a handler for external file change notifications.
     * @param {Function} handler - Called when the file changes externally.
     */
    onExternalChange(handler) {
        this.#bridge._addEventListener('document/externalChange', handler);
    }
}

/**
 * Dialog operations API.
 */
class DialogAPI {
    /** @type {WebViewBridge} */
    #bridge;

    /**
     * @param {WebViewBridge} bridge
     */
    constructor(bridge) {
        this.#bridge = bridge;
    }

    /**
     * Opens an image picker dialog.
     * @param {string[]} extensions - Allowed file extensions (e.g., ['.png', '.jpg']).
     * @returns {Promise<string|null>} - The selected path, or null if cancelled.
     */
    async pickImage(extensions) {
        const result = await this.#bridge._request('dialog/pickImage', { extensions });
        return result.path;
    }

    /**
     * Opens a file picker dialog.
     * @param {string[]} extensions - Allowed file extensions.
     * @returns {Promise<string|null>} - The selected path, or null if cancelled.
     */
    async pickFile(extensions) {
        const result = await this.#bridge._request('dialog/pickFile', { extensions });
        return result.path;
    }

    /**
     * Shows an alert dialog.
     * @param {string} title - The alert title.
     * @param {string} message - The alert message.
     * @returns {Promise<void>}
     */
    async alert(title, message) {
        await this.#bridge._request('dialog/alert', { title, message });
    }
}

/**
 * Theme events API.
 */
class ThemeAPI {
    /** @type {WebViewBridge} */
    #bridge;

    /**
     * @param {WebViewBridge} bridge
     */
    constructor(bridge) {
        this.#bridge = bridge;
    }

    /**
     * Registers a handler for theme change notifications.
     * @param {(theme: ThemeInfo) => void} handler - Called when the theme changes.
     */
    onChanged(handler) {
        this.#bridge._addEventListener('theme/changed', handler);
    }
}

/**
 * Localization events API.
 */
class LocalizationAPI {
    /** @type {WebViewBridge} */
    #bridge;

    /**
     * @param {WebViewBridge} bridge
     */
    constructor(bridge) {
        this.#bridge = bridge;
    }

    /**
     * Registers a handler for localization update notifications.
     * @param {(strings: Object<string, string>) => void} handler - Called when localization is updated.
     */
    onUpdated(handler) {
        this.#bridge._addEventListener('localization/updated', handler);
    }
}

// Export a singleton instance for typical browser usage.
// In Node.js test environment, create instances manually via the WebViewBridge class.
let _bridge = null;

/**
 * Gets the singleton bridge instance.
 * Creates the instance lazily to avoid errors in non-browser environments.
 * @returns {WebViewBridge}
 */
export function getBridge() {
    if (!_bridge) {
        _bridge = new WebViewBridge();
    }
    return _bridge;
}

// Also export the class for testing or custom instances
export { WebViewBridge };

