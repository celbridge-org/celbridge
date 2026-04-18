// Celbridge: JavaScript client for communicating with the Celbridge .NET host.
// Provides promise-based async API with automatic request/response correlation.

import { RpcTransport } from './core/rpc-transport.js';
import { DocumentAPI } from './api/document-api.js';
import { DialogAPI } from './api/dialog-api.js';
import { InputAPI } from './api/input-api.js';
import { ThemeAPI } from './api/theme-api.js';
import { LocalizationAPI } from './api/localization-api.js';

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
     * Creates a new Celbridge instance.
     * @param {Object} [options] - Configuration options.
     * @param {Function} [options.postMessage] - Custom postMessage function (for testing).
     * @param {Function} [options.onMessage] - Custom message handler setup (for testing).
     * @param {number} [options.timeout] - Request timeout in milliseconds.
     */
    constructor(options = {}) {
        this.#transport = new RpcTransport(options);

        // Initialize sub-APIs
        this.document = new DocumentAPI(this.#transport);
        this.dialog = new DialogAPI(this.#transport);
        this.input = new InputAPI(this.#transport);
        this.theme = new ThemeAPI();
        this.localization = new LocalizationAPI(this.#transport);
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

        const result = await this.#transport.request('document/initialize', {
            protocolVersion: RpcTransport.protocolVersion
        });

        this.#transport.markInitialized();

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
     * @param {Object} handlers - Handler callbacks for document events.
     * @param {Function} [handlers.onContent] - Called with (content, metadata) after initialization.
     * @param {Function} [handlers.onRequestSave] - Called when the host requests a save.
     * @param {Function} [handlers.onExternalChange] - Called when the file changes externally.
     * @param {Function} [handlers.onRequestState] - Called when the host requests editor state. Should return a string or null.
     * @param {Function} [handlers.onRestoreState] - Called with a state string to restore.
     * @returns {Promise<InitializeResult>} - The initialization result with content and config.
     */
    async initializeDocument(handlers = {}) {
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
