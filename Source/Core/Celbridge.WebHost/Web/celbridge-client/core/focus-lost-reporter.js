// Reports to the Celbridge host when the keyboard leaves the page. The host injects this script into every web
// view at document start, so third-party pages report their focus losses too. The method names mirror the
// host's InputRpcMethods.FocusLost and LogRpcMethods.Log, and a host test checks that they match.
(function () {
    if (window.__celbridgeFocusLostInstalled) {
        return;
    }
    window.__celbridgeFocusLostInstalled = true;

    // Document-start injection reaches every frame. Only the top document's focus counts for the
    // surface.
    if (window.top !== window) {
        return;
    }

    // Posts to the native bridge the host reads focus signals from. The WebView2 heads expose
    // chrome.webview. macOS has no chrome.webview, so it uses the Uno WKWebView message handler.
    // Both bridges raise CoreWebView2.WebMessageReceived on the host, where the view listens.
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
    // scripts, because a page can be left without a native bridge. The focus-loss report cannot use
    // the live transport, because the view reads that report from its own native message event.
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

    // Whether this page can still reach the host over the native message bus. A surface can lose
    // its bus. The host removes the message handler on every Unloaded, which stops Uno registering
    // a second handler. The host then relies on Uno's next Loaded to restore the handler, and that
    // Loaded does not always come.
    function hasNativeBridge() {
        if (window.chrome && window.chrome.webview) {
            return true;
        }

        return !!(window.webkit
            && window.webkit.messageHandlers
            && window.webkit.messageHandlers.unoWebView);
    }

    // The bridge state is reported on the first focus or blur, not at install. This script runs at
    // document start, before the client exposes the live transport. A bridge absent at that point
    // would have no way to report itself, so the logs would only ever show healthy surfaces.
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

    // A page can receive more than one focus event for a single gesture. The host makes the view
    // the first responder, and then an editor's own DOM grant focuses an element. The count in each
    // log line shows that the repeats are separate events, not the host logging one event twice.
    var focusCount = 0;

    window.addEventListener('focus', function () {
        reportBridgeStateOnce();
        focusCount++;
        log('the page took the keyboard (focus event ' + focusCount + ')');
    });

    window.addEventListener('blur', function () {
        reportBridgeStateOnce();

        // Focus moving into an iframe of this page also blurs the top window, while the document
        // still reports focus. Wait one task before deciding that the keyboard left the page.
        setTimeout(function () {
            if (document.hasFocus()) {
                log('the page blurred but still holds the keyboard');
                return;
            }

            // A hidden surface is expected to have no bridge. The host removes the handler when the
            // surface is unloaded, and the surface that replaced it on screen claims focus itself.
            // Only a lost report from a visible surface is a problem, so only that case warns.
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
