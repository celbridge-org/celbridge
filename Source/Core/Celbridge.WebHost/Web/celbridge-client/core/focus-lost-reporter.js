// Reports to the Celbridge host when the keyboard leaves the page. The host injects it into every web view at
// document start, so a page we did not author reports its losses too. The method names are the host's
// InputRpcMethods.FocusLost and LogRpcMethods.Log, which a host test checks this file against.
(function () {
    if (window.__celbridgeFocusLostInstalled) {
        return;
    }
    window.__celbridgeFocusLostInstalled = true;

    // Document-start injection reaches every frame, and only the top document's focus stands for
    // the surface.
    if (window.top !== window) {
        return;
    }

    // The native bridges the host reads focus signals from: chrome.webview on the WebView2 heads,
    // and the Uno WKWebView message handler on macOS, where chrome.webview is absent. Both surface
    // on the host as CoreWebView2.WebMessageReceived, which is where the view listens.
    function postToNativeBridge(envelope) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(envelope);
        } else if (window.webkit
            && window.webkit.messageHandlers
            && window.webkit.messageHandlers.unoWebView) {
            window.webkit.messageHandlers.unoWebView.postMessage(envelope);
        }
    }

    // Diagnostics prefer the page's own live transport, which the client exposes for injected
    // scripts, because a page can be left without a native bridge. Focus loss cannot use it: the
    // view reads the report from its own native message event.
    function postDiagnostic(envelope) {
        if (typeof globalThis.__hostSendMessage === 'function') {
            globalThis.__hostSendMessage(envelope);
            return;
        }

        postToNativeBridge(envelope);
    }

    function report(level, message) {
        postDiagnostic(JSON.stringify({
            jsonrpc: '2.0',
            method: 'host/log',
            params: { level: level, message: message + ' (' + window.location.pathname + ')' }
        }));
    }

    function log(message) {
        report('debug', message);
    }

    // Whether this page can still reach the host over the native message bus. A surface can be left
    // without one: the host removes the handler on every Unloaded to stop Uno registering a second
    // one, and relies on Uno's next Loaded to put it back, which does not always come.
    function hasNativeBridge() {
        if (window.chrome && window.chrome.webview) {
            return true;
        }

        return !!(window.webkit
            && window.webkit.messageHandlers
            && window.webkit.messageHandlers.unoWebView);
    }

    // Reported on first use rather than at install: this script runs at document start, before the
    // client has exposed the transport, so an absent bridge at that moment has no way to say so and
    // the sample would only ever contain the surfaces that are fine.
    var reportedBridgeState = false;

    function reportBridgeStateOnce() {
        if (reportedBridgeState) {
            return;
        }

        reportedBridgeState = true;

        var present = hasNativeBridge();
        report(
            present || document.hidden ? 'debug' : 'warn',
            'native message bridge ' + (present ? 'present' : 'absent'));
    }

    // Counted because a page can receive more than one focus event for a single gesture (the host
    // makes the view the first responder, and an editor's own DOM grant focuses an element after
    // it). Without the count the repeats read as the host logging the same event twice.
    var focusCount = 0;

    window.addEventListener('focus', function () {
        reportBridgeStateOnce();
        focusCount++;
        log('the page took the keyboard (focus event ' + focusCount + ')');
    });

    window.addEventListener('blur', function () {
        reportBridgeStateOnce();

        // Focus moving into an iframe of this same page also blurs the top window, and the document
        // still reports focus in that case, so settle on the next task before deciding it left.
        setTimeout(function () {
            if (document.hasFocus()) {
                log('the page blurred but still holds the keyboard');
                return;
            }

            // A hidden surface has no bridge by design: the host removes the handler when the
            // surface is unloaded, and whatever replaced it on screen claims focus itself. Only a
            // surface the user can still see is a departure the host needed to hear about.
            if (!hasNativeBridge()) {
                report(
                    document.hidden ? 'debug' : 'warn',
                    'focus loss not delivered, no native message bridge (surface '
                        + (document.hidden ? 'hidden' : 'visible') + ')');
                return;
            }

            postToNativeBridge(JSON.stringify({ jsonrpc: '2.0', method: 'input/focusLost' }));
        }, 0);
    });
})();
