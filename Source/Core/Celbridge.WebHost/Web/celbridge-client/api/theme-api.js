// Theme API: Theme detection via browser's prefers-color-scheme.

/**
 * @typedef {import('../types.js').WebViewTheme} WebViewTheme
 */

/**
 * Theme events API.
 * Detects theme from the browser's prefers-color-scheme media query.
 */
export class ThemeAPI {
    /** @type {MediaQueryList} */
    #darkModeQuery;

    /** @type {Function[]} */
    #handlers = [];

    constructor() {
        // Check if matchMedia is available (not available in Node.js tests)
        if (typeof window !== 'undefined' && window.matchMedia) {
            this.#darkModeQuery = window.matchMedia('(prefers-color-scheme: dark)');
            this.#darkModeQuery.addEventListener('change', (e) => {
                const theme = e.matches ? 'Dark' : 'Light';
                for (const handler of this.#handlers) {
                    try {
                        handler(theme);
                    } catch (error) {
                        console.error('[ThemeAPI] Error in theme change handler:', error);
                    }
                }
            });
        }
    }

    /**
     * Gets the current theme based on the browser's prefers-color-scheme.
     * @returns {WebViewTheme} 'Light' or 'Dark'
     */
    get current() {
        if (this.#darkModeQuery) {
            return this.#darkModeQuery.matches ? 'Dark' : 'Light';
        }
        return 'Light';
    }

    /**
     * Gets whether the current theme is dark.
     * @returns {boolean}
     */
    get isDark() {
        return this.current === 'Dark';
    }

    /**
     * Registers a handler for theme change notifications.
     * @param {(theme: WebViewTheme) => void} handler - Called when the theme changes.
     */
    onChanged(handler) {
        this.#handlers.push(handler);
    }
}
