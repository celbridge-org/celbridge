// Test stub for the celbridge localization module. The real module is served
// by the WebView host at https://shared.celbridge/...; vitest aliases that URL
// to this file so toolbar.js can be imported under jsdom without the live host
// environment. The stub is intentionally trivial: tests only need a callable
// `t()` that returns something stable.

export function t(key) {
    return key;
}

export function setStrings() {}

export function applyLocalization() {}
