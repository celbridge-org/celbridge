// In-page bridge for the webview_* MCP tool namespace. Injected by the
// Celbridge host into eligible WebViews. Unrelated to WebView2's built-in
// browser DevTools.
(function () {
    'use strict';

    // Bail out in cross-origin frames. The Celbridge WebView2 instance only
    // ever serves content from .celbridge virtual hosts (project.celbridge for
    // HTML viewers, {package}.celbridge for contribution editors), so any other
    // origin is an embedded iframe loading external content and the shim must
    // not activate there.
    var hostname = (typeof location !== 'undefined' && location && location.hostname) || '';
    if (hostname !== 'project.celbridge' && !/\.celbridge$/.test(hostname)) {
        return;
    }

    // Hang the namespace off a Symbol-keyed slot on globalThis so editor
    // package code that walks window.* never sees it.
    var bridgeKey = Symbol.for('__cel_webview_tools');
    if (globalThis[bridgeKey]) {
        return;
    }

    var handlers = Object.create(null);

    var bridge = {
        version: 1,
        registerHandler: function (name, handler) {
            handlers[name] = handler;
        },
        getHandler: function (name) {
            return handlers[name] || null;
        }
    };

    Object.defineProperty(globalThis, bridgeKey, {
        value: bridge,
        writable: false,
        configurable: false,
        enumerable: false
    });
})();
