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
 * - data-loc-attr: the attribute to set (e.g. "title", "placeholder"). Defaults to textContent.
 */
export function applyLocalization(root = document) {
    root.querySelectorAll('[data-loc-key]').forEach(el => {
        const key = el.getAttribute('data-loc-key');
        const value = strings[key];
        if (value === undefined) return;

        const attr = el.getAttribute('data-loc-attr');
        if (attr) {
            el.setAttribute(attr, value);
        } else {
            el.textContent = value;
        }
    });
}
