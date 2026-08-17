// Test stub for the celbridge localization module. The real module is served by the file server at
// /assets/celbridge-client/...; vitest aliases that URL to this file so report-view.js can be
// imported under jsdom.
//
// It resolves against the editor's real en.json rather than echoing keys, so the tests assert the
// text a reader actually sees and a key missing an entry fails here too.

import strings from '../../localization/en.json';

export function t(key, ...args) {
    let value = strings[key] ?? key;
    for (let i = 0; i < args.length; i++) {
        value = value.replaceAll(`{${i}}`, args[i]);
    }

    return value;
}

export function setStrings() { }

export function applyLocalization() { }
