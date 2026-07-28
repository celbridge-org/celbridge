// Shared localization module for Celbridge WebView-based editors.
// Receives a dictionary of localized strings from the C# host and provides:
// - setStrings(dict): stores the dictionary and applies it to the DOM
// - t(key, ...args): returns a localized string with {0}, {1}, ... placeholder substitution
// - applyLocalization(root): walks the DOM and applies strings via data-loc-key / data-loc-attr

const strings = {};

/**
 * Set the localization dictionary and apply to the DOM.
 * @param {Object<string, string>} dict - key/value pairs
 */
export function setStrings(dict) {
    Object.assign(strings, dict);
    applyLocalization();
}

/**
 * Look up a localized string by key, with optional placeholder substitution.
 * Falls back to the key itself if not found.
 * @param {string} key
 * @param {...*} args - values to substitute for {0}, {1}, etc.
 * @returns {string}
 */
export function t(key, ...args) {
    let value = strings[key] ?? key;
    for (let i = 0; i < args.length; i++) {
        value = value.replaceAll(`{${i}}`, args[i]);
    }
    return value;
}

/**
 * Walk the DOM and apply localized strings to elements marked with data-loc-key.
 * - data-loc-key: the resource key to look up
 * - data-loc-attr: the attribute(s) to set, defaulting to textContent. Accepts a comma-separated list to
 *   apply the one string to several attributes at once (e.g. "title,aria-label" for an icon button's
 *   tooltip and accessible name).
 * - data-loc-title: a separate key applied to the title attribute, so an element can
 *   carry both localized visible text (via data-loc-key) and a localized tooltip.
 *
 * When a key is missing from the dictionary, the raw key name is displayed
 * instead and a console warning is logged so missing entries are obvious.
 */
export function applyLocalization(root = document) {
    const missing = [];

    function resolve(key) {
        const value = strings[key];
        if (value === undefined) {
            missing.push(key);
        }

        return value ?? key;
    }

    root.querySelectorAll('[data-loc-key]').forEach(el => {
        const key = el.getAttribute('data-loc-key');
        const text = resolve(key);
        const attr = el.getAttribute('data-loc-attr');
        if (attr) {
            for (const name of attr.split(',')) {
                const trimmed = name.trim();
                if (trimmed) {
                    el.setAttribute(trimmed, text);
                }
            }
        } else {
            el.textContent = text;
        }
    });

    root.querySelectorAll('[data-loc-title]').forEach(el => {
        const titleKey = el.getAttribute('data-loc-title');
        el.setAttribute('title', resolve(titleKey));
    });

    if (missing.length > 0) {
        console.warn(`[Localization] Missing keys: ${missing.join(', ')}`);
    }
}
