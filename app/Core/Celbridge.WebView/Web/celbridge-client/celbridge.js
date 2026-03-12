// Celbridge: JavaScript SDK for communicating with the Celbridge .NET host.
// Provides promise-based async API with automatic request/response correlation.

import { RpcTransport } from './core/rpc-transport.js';
import { DocumentAPI } from './api/document-api.js';
import { DialogAPI } from './api/dialog-api.js';
import { InputAPI } from './api/input-api.js';
import { ThemeAPI } from './api/theme-api.js';
import { LocalizationAPI } from './api/localization-api.js';
import { CodePreviewAPI } from './api/code-preview-api.js';

/**
 * @typedef {import('./types.js').InitializeResult} InitializeResult
 * @typedef {import('./types.js').LogLevel} LogLevel
 */

/**
 * Celbridge SDK.
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
     * Code preview operations API.
     * Used by code preview panes to communicate with the host.
     * @type {CodePreviewAPI}
     */
    codePreview;

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
        this.codePreview = new CodePreviewAPI(this.#transport);
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
            await this.#loadLocalization(result.metadata.locale);
        }

        return result;
    }

    /**
     * Loads localization strings from the extension's localization folder.
     * Uses convention: localization/{locale}.json, falls back to en.json.
     * Silently skips if not running in a browser environment (e.g., tests).
     * @param {string} locale - The locale to load (e.g., "en", "fr").
     */
    async #loadLocalization(locale) {
        // Skip localization loading in non-browser environments (e.g., Node.js tests)
        if (typeof location === 'undefined' || typeof fetch === 'undefined') {
            return;
        }

        const { setStrings } = await import('./localization.js');
        const hostName = location.hostname;

        const tryFetch = async (loc) => {
            const url = `https://${hostName}/localization/${loc}.json`;
            try {
                const response = await fetch(url);
                if (response.ok) {
                    return await response.json();
                }
            } catch {
                // Ignore fetch errors
            }
            return null;
        };

        // Try requested locale, then fall back to English
        let strings = await tryFetch(locale);
        if (!strings && locale !== 'en') {
            strings = await tryFetch('en');
        }

        if (strings) {
            setStrings(strings);
        }
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
