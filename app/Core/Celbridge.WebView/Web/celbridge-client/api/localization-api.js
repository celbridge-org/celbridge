// Localization API: String loading, lookup, and language change events.
// Strings are auto-loaded during initialize() from the extension's
// localization folder (convention: localization/{locale}.json).

/**
 * Localization API.
 * Loads localized strings from the extension's localization folder and
 * provides key lookup with placeholder substitution.
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
     * Loads localization strings from the extension's localization folder.
     * Uses convention: localization/{locale}.json, falls back to en.json.
     * Silently skips if not running in a browser environment (e.g., tests).
     * @param {string} locale - The locale to load (e.g., "en", "fr").
     */
    async loadStrings(locale) {
        if (typeof location === 'undefined' || typeof fetch === 'undefined') {
            return;
        }

        const { setStrings } = await import('../localization.js');
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
