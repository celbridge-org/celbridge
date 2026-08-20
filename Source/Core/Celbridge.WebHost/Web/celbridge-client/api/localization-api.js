// Localization API: String loading and lookup.
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
     * Loads localization strings for the given locale, falling back to en.json. Loads two sources into the
     * shared string table: the client's own built-in UI strings (controls the client provides, e.g. the find
     * bar) and the consuming extension's own localization folder. Silently skips if not running in a browser
     * environment (e.g., tests).
     * @param {string} locale - The locale to load (e.g., "en", "fr").
     */
    async loadStrings(locale) {
        if (typeof location === 'undefined' || typeof fetch === 'undefined') {
            return;
        }

        const { setStrings } = await import('../localization.js');

        // Both sources are fetched before either is applied. Applying them one at a time would walk the DOM
        // once holding only the client's strings, and report every one of the extension's keys as missing.
        const [clientStrings, extensionStrings] = await Promise.all([
            // The client's own UI strings, resolved relative to this module so they load from the client
            // folder regardless of which origin the editor is served from. Keeps client-provided controls
            // localized through the same {locale}.json workflow without each extension defining their keys.
            this.#fetchStrings((loc) => new URL(`../localization/${loc}.json`, import.meta.url).href, locale),

            // The extension's own strings, page-relative so the URL resolves to the package's localization
            // folder under whichever origin the editor is loaded from (loopback /package/{name}/ on every
            // head, or the legacy virtual host on the Windows heads still using it).
            this.#fetchStrings((loc) => `localization/${loc}.json`, locale)
        ]);

        // The extension's strings are spread last, so a key it defines wins over the client's.
        const strings = { ...clientStrings, ...extensionStrings };
        if (Object.keys(strings).length > 0) {
            setStrings(strings);
        }
    }

    /**
     * Registers a handler for the host changing the application language. The handler receives the new
     * locale and is responsible for reloading its own strings, usually by calling loadStrings with it.
     * The host's native chrome only picks up a language change on the next launch; a hosted page can
     * re-localize immediately.
     * @param {Function} handler - Called with the new locale, e.g. "fr".
     */
    onLanguageChanged(handler) {
        this.#transport.addEventListener('localization/languageChanged', (params) => handler(params?.locale));
    }

    async #fetchStrings(urlForLocale, locale) {
        const tryFetch = async (loc) => {
            try {
                const response = await fetch(urlForLocale(loc));
                if (response.ok) {
                    return await response.json();
                }
            } catch {
                // Ignore fetch errors
            }
            return null;
        };

        // Try the requested locale, then fall back to English.
        const strings = await tryFetch(locale);
        if (!strings && locale !== 'en') {
            return await tryFetch('en');
        }
        return strings;
    }
}
