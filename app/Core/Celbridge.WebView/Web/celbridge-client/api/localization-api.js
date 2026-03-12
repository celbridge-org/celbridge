// Localization API: Language change events.
// Localization strings are auto-loaded during initialize() from the extension's
// localization folder (convention: localization/{locale}.json).

/**
 * Localization events API.
 * Used for dynamic language changes at runtime.
 */
export class LocalizationAPI {
    /** @type {import('../core/rpc-transport.js').RpcTransport} */
    #transport;

    /**
     * @param {import('../core/rpc-transport.js').RpcTransport} transport
     */
    constructor(transport) {
        this.#transport = transport;
    }

    /**
     * Registers a handler for language change notifications.
     * Called when the application language changes at runtime.
     * The handler receives the new locale string (e.g., "en", "fr").
     * Extensions should reload their localization strings when this fires.
     * @param {(locale: string) => void} handler - Called when language changes.
     */
    onLanguageChanged(handler) {
        this.#transport.addEventListener('localization/languageChanged', handler);
    }
}
