using System.Runtime.CompilerServices;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

internal class WebViewFocusRegistry : IWebViewFocusRegistry
{
    private readonly IFocusService _focusService;
    private readonly IWebViewAdapter _webViewAdapter;
    private readonly IWebViewFocusMonitor _webViewFocusMonitor;
    private readonly IMessengerService _messengerService;
    private readonly IWebSurfaceMessageDispatcher _messageDispatcher;
    private readonly ILogger<WebViewFocusRegistry> _logger;

    // Keyed by CoreWebView2, the stable surface identity shared with the native monitor. Accessed only on the UI
    // thread: Register/Unregister run from view lifecycle, and every gain signal is marshalled to the UI thread
    // before it reaches the registry.
    private readonly Dictionary<CoreWebView2, WebViewFocusRegistration> _registrations = new();

    // The surface whose focus report is current. Cleared when the focus service releases it in favour of
    // another surface or panel (via the wrapped release callback in Report), and on Unregister.
    private WebViewFocusRegistration? _focusedRegistration;

    // Which surfaces already carry the focus-lost listener. Document-start scripts live as long as the
    // CoreWebView2 and cannot be removed on every head, so installing once per surface rather than once per
    // registration keeps a redock (which unregisters and re-registers a live surface) from stacking copies.
    // Weak keys so tracking a surface never keeps its web view alive.
    private readonly ConditionalWeakTable<CoreWebView2, object> _surfacesWithFocusLostScript = new();

    // Whether the host window currently holds the keyboard. A page blurs both when focus moves to another
    // part of the application and when the whole window is deactivated, and only the first is focus leaving
    // the surface: alt-tabbing away must leave the caret where the user put it.
    private bool _isHostWindowActive = true;

    // Whether a modal dialog currently holds the keyboard. A dialog blurs the page exactly as a click on
    // another panel does, and only the host knows which it was.
    private bool _isModalDialogOpen;

    // Resolved lazily: the reconciler depends on this registry, so constructor-injecting it here would cycle.
    private IFocusReconciler? _focusReconciler;

    // Reports the surface losing the keyboard, which the managed layer cannot see: on the packaged Windows
    // head the web content lives in its own child window, so a click on the caption or on any non-focusable
    // region moves the keyboard off it without moving managed focus at all. Injected at document start
    // through the adapter seam rather than carried by the client bundle, so a page we did not author reports
    // its losses too.
    //
    // Interpolated so the method names come from the same constants the host dispatches on: this script is a
    // third client of the web channel, and one written as a string literal is invisible to the contract tests
    // that keep the other two in step.
    private static readonly string FocusLostScript = $$"""
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
            // on the host as CoreWebView2.WebMessageReceived, which is where the focus registry listens.
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
            // scripts, because a page can be left without a native bridge. Focus loss cannot use it: only
            // the native bus reaches the registry that knows which surface reported.
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
                    method: '{{LogRpcMethods.Log}}',
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

                    postToNativeBridge(JSON.stringify({ jsonrpc: '2.0', method: '{{InputRpcMethods.FocusLost}}' }));
                }, 0);
            });
        })();
        """;

    // A grant for a surface that had not registered yet, applied when that web view registers. A freshly
    // opened document is activated before its web view finishes initializing. Dropped when the user moves
    // focus elsewhere in the meantime, and when the web view is torn down before it ever registers.
    private WebView2? _pendingGrant;

    public WebViewFocusRegistry(
        IFocusService focusService,
        IWebViewAdapter webViewAdapter,
        IWebViewFocusMonitor webViewFocusMonitor,
        IMessengerService messengerService,
        IWebSurfaceMessageDispatcher messageDispatcher,
        ILogger<WebViewFocusRegistry> logger)
    {
        _focusService = focusService;
        _webViewAdapter = webViewAdapter;
        _webViewFocusMonitor = webViewFocusMonitor;
        _messengerService = messengerService;
        _messageDispatcher = messageDispatcher;
        _logger = logger;

        // Claimed here rather than at the composition root because the registry both owns what a focus loss
        // means and attaches the surfaces that report one, so it cannot attach a surface without first
        // having registered its interest.
        _messageDispatcher.AddHandler(InputRpcMethods.FocusLost, OnFocusLostMessage);

        _messengerService.Register<MainWindowActivatedMessage>(this, (_, _) => OnHostWindowActivationChanged(true));
        _messengerService.Register<MainWindowDeactivatedMessage>(this, (_, _) => OnHostWindowActivationChanged(false));
        _messengerService.Register<ModalDialogOpenedMessage>(this, (_, _) => _isModalDialogOpen = true);
        _messengerService.Register<ModalDialogClosedMessage>(this, (_, _) => _isModalDialogOpen = false);
    }

    private void OnHostWindowActivationChanged(bool isActive)
    {
        _isHostWindowActive = isActive;

        _logger.LogDebug("Host window {Activation}", isActive ? "activated" : "deactivated");
    }

    public void Register(WebViewFocusRegistration registration)
    {
        var coreWebView = registration.WebView.CoreWebView2;
        if (coreWebView is null)
        {
            _logger.LogWarning("Cannot register a web surface for focus tracking before its CoreWebView2 is ready");
            return;
        }

        // A pooled WebView reacquired for a new surface keeps its CoreWebView2, so drop the previous
        // registration's subscriptions before replacing it.
        var replacesFocusedSurface = false;
        if (_registrations.TryGetValue(coreWebView, out var previousRegistration))
        {
            DetachSurfaceHandlers(registration.WebView, coreWebView);

            // A live surface can be re-registered under a new contract without the user ever moving focus
            // off it: docking a utility between the Utility Panel and a document tab re-points the same web
            // view at a different panel. The keyboard never left, so the model has to follow the surface to
            // its new panel rather than go on naming the old one.
            replacesFocusedSurface = ReferenceEquals(_focusedRegistration, previousRegistration);
        }

        _registrations[coreWebView] = registration;

        // The managed GotFocus is the Windows gain signal and also fires for clicks on non-focusable content
        // that raise no DOM focus event. The native monitor is the macOS equivalent; a no-op elsewhere.
        registration.WebView.GotFocus += OnWebViewGotFocus;
        _webViewFocusMonitor.Register(coreWebView, () => OnNativeFocusSignal(coreWebView));

        // The focus-lost signal comes back through the page rather than either of the gain paths above,
        // because neither the managed nor the native layer observes the keyboard leaving the web content.
        // It arrives over the message bus, which the surface joins here for as long as it is registered.
        _messageDispatcher.Attach(coreWebView, registration.SurfaceName);

        coreWebView.NavigationCompleted += OnNavigationCompleted;

        if (!_surfacesWithFocusLostScript.TryGetValue(coreWebView, out _))
        {
            _surfacesWithFocusLostScript.Add(coreWebView, new object());
            _ = InstallFocusLostScriptAsync(coreWebView);
        }

        if (replacesFocusedSurface)
        {
            // Granted rather than merely reported: the new registration is a different surface identity to
            // the focus service, so reporting it releases the old one, and releasing drops the page's caret.
            // The grant puts it back, which is what a redock should leave behind anyway.
            GrantFocus(registration.WebView);
            return;
        }

        if (!ReferenceEquals(_pendingGrant, registration.WebView))
        {
            return;
        }

        _pendingGrant = null;

        // The grant was issued for a panel the user has since moved away from, so applying it now would
        // pull the keyboard back off whatever they turned to while the surface was initializing.
        if (_focusService.FocusedPanel != registration.Panel)
        {
            _logger.LogDebug(
                "Dropped a deferred focus grant: focus moved to {Panel} while the surface was initializing",
                _focusService.FocusedPanel);
            return;
        }

        GrantFocus(registration.WebView);
    }

    public void Unregister(CoreWebView2 coreWebView)
    {
        if (!_registrations.Remove(coreWebView, out var registration))
        {
            return;
        }

        // A web view torn down before a deferred grant reached it must not keep the intent alive: web views
        // are pooled, so the same instance reacquired for another document would take the stale grant.
        if (ReferenceEquals(_pendingGrant, registration.WebView))
        {
            _pendingGrant = null;
        }

        var torndownSurfaceHeldFocus = ReferenceEquals(_focusedRegistration, registration);
        if (torndownSurfaceHeldFocus)
        {
            _focusedRegistration = null;
        }

        DetachSurfaceHandlers(registration.WebView, coreWebView);
        _webViewFocusMonitor.Unregister(coreWebView);

        if (torndownSurfaceHeldFocus)
        {
            ClearFocusUnlessAnotherSurfaceClaims(registration);
        }

        // Invalidate the edit context on teardown so a closed editor cannot leave the Edit menu enabled. The
        // focus service keeps a newer target that has replaced this one.
        if (registration.EditTarget is not null)
        {
            _focusService.ClearEditTarget(registration.EditTarget);
        }
    }

    public void GrantFocus(WebView2 webView)
    {
        var coreWebView = webView.CoreWebView2;
        if (coreWebView is null
            || !_registrations.TryGetValue(coreWebView, out var registration))
        {
            // The surface is still initializing, so hold the intent until it registers. A later grant
            // supersedes this one, so the surface the user last acted on is the one that takes focus.
            _pendingGrant = webView;
            _logger.LogDebug("Focus granted to a web surface that has not registered yet; deferred until it does");

            return;
        }

        _pendingGrant = null;

        _logger.LogDebug("Focus granted to web surface {Surface}", registration.SurfaceName);

        // Reporting applies the claim, which also releases the previously focused surface immediately
        // rather than waiting for the JS focus round trip that a surface with no DOM-side grant never
        // produces. The optional DOM-side focus then places the caret.
        Report(registration);

        _ = registration.GrantDomFocus?.Invoke();
    }

    private void OnWebViewGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WebView2 webView
            && webView.CoreWebView2 is not null
            && _registrations.TryGetValue(webView.CoreWebView2, out var registration))
        {
            Report(registration);
        }
    }

    private async Task InstallFocusLostScriptAsync(CoreWebView2 coreWebView)
    {
        try
        {
            await _webViewAdapter.InstallDocumentStartScriptAsync(coreWebView, FocusLostScript);

            // Document-start injection reaches the next navigation, not the current one, and a surface
            // registers once its content has already loaded. Run the listener against the document showing
            // now as well; installing twice is a no-op.
            await _webViewAdapter.ReinjectDocumentStartScriptAsync(coreWebView, FocusLostScript);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to install the focus-lost listener");
        }
    }

    // Document-start injection is unavailable on the Windows Skia head, which re-delivers after each
    // navigation instead. The listener guards against installing twice, so re-delivery is a no-op on the
    // heads whose injected script already survived the navigation.
    private async void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        try
        {
            await _webViewAdapter.ReinjectDocumentStartScriptAsync(sender, FocusLostScript);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-install the focus-lost listener after a navigation");
        }
    }

    // Drops everything Register subscribed on the surface itself. The document-start script is deliberately
    // left in place: it cannot be removed on every head, and the surface is tracked so it is never installed
    // twice.
    private void DetachSurfaceHandlers(WebView2 webView, CoreWebView2 coreWebView)
    {
        webView.GotFocus -= OnWebViewGotFocus;
        coreWebView.NavigationCompleted -= OnNavigationCompleted;

        _messageDispatcher.Detach(coreWebView);
    }

    // The surface holding the keyboard has been torn down, so nothing holds it any more. Deferred rather
    // than applied here because closing a document activates the next one, which claims focus a step later:
    // clearing now would take the caret straight back off it. If nothing has claimed by then, the focus model
    // is left naming a panel whose surface is gone, and the focus indicator would show a caret nobody has.
    private void ClearFocusUnlessAnotherSurfaceClaims(WebViewFocusRegistration registration)
    {
        var dispatcherQueue = registration.WebView.DispatcherQueue;
        if (dispatcherQueue is null)
        {
            return;
        }

        // Deferred twice. Closing a document tears its surface down before it selects the next one, so a
        // single hop would run while the replacement's own grant was still queued behind it, and focus would
        // be seen to leave the panel and come straight back. The second hop puts this behind everything the
        // close queued.
        dispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => dispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    if (_focusedRegistration is not null)
                    {
                        return;
                    }

                    _logger.LogDebug(
                        "Cleared focus after the focused web surface {Surface} was torn down",
                        registration.SurfaceName);

                    _focusService.ClearFocus();
                }));
    }

    private void OnFocusLostMessage(WebSurfaceMessage message)
    {
        if (_registrations.TryGetValue(message.Surface, out var registration))
        {
            OnFocusLost(registration);
        }
    }

    // The keyboard has left the page. A page can only ever report this for itself, so an untrusted document
    // gains nothing by forging it: the report is ignored unless that same surface currently holds focus.
    private void OnFocusLost(WebViewFocusRegistration registration)
    {
        // The window losing activation blurs the page just as a click on another panel does. The keyboard has
        // left the application rather than the surface, so the caret stays where the user put it and comes
        // back with them.
        if (!_isHostWindowActive)
        {
            _logger.LogDebug(
                "Ignored a focus loss from {Surface}: the host window is not active",
                registration.SurfaceName);
            return;
        }

        // The dialog took the keyboard, not another panel. Keeping the surface focused is what lets the
        // dialog hand the keyboard back to it on the way out.
        if (_isModalDialogOpen)
        {
            _logger.LogDebug(
                "Ignored a focus loss from {Surface}: a modal dialog holds the keyboard",
                registration.SurfaceName);
            return;
        }

        // The surface must still hold focus both when the report arrives and once the work the blur arrived
        // alongside has drained. The first check drops a report that raced past a release (the user left the
        // surface and came back while it was in flight); the deferred checks below decide the rest.
        if (!ReferenceEquals(_focusedRegistration, registration))
        {
            _logger.LogDebug(
                "Ignored a focus loss from {Surface}: it no longer holds focus",
                registration.SurfaceName);
            return;
        }

        // Queued below the focus reconcile, which the resign that caused this blur queues at the same
        // priority and therefore ahead of it. Reading the settled state is what separates a blur the host
        // caused from one the user did.
        registration.WebView.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                if (!ReferenceEquals(_focusedRegistration, registration))
                {
                    _logger.LogDebug(
                        "Ignored a focus loss from {Surface}: another surface claimed focus first",
                        registration.SurfaceName);
                    return;
                }

                if (HoldsPlatformKeyboardFocus(registration))
                {
                    _logger.LogDebug(
                        "Ignored a focus loss from {Surface}: the platform still routes the keyboard to it",
                        registration.SurfaceName);
                    return;
                }

                _logger.LogDebug(
                    "The focused web surface {Surface} reported that the keyboard left it",
                    registration.SurfaceName);

                _focusService.ClearFocus();
            });
    }

    // Whether the platform still routes the keyboard to the surface. Focus reconciliation resigns and
    // re-asserts native focus in one gesture, because Uno resigns the first responder on every managed focus
    // change, and the page reports that pair as a blur like any other. Only the platform can tell the two
    // apart. False off macOS, where the managed focus the other heads move is itself the signal.
    private bool HoldsPlatformKeyboardFocus(WebViewFocusRegistration registration)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var coreWebView = registration.WebView.CoreWebView2;
        if (coreWebView is null)
        {
            return false;
        }

        if (!Platform.MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView, out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not read the focused web surface's native focus: {Detail}", detail);
            return false;
        }

        return Platform.MacOSWebViewInterop.IsWebViewFirstResponder(nativeHandle);
    }

    private void OnNativeFocusSignal(CoreWebView2 coreWebView)
    {
        // Arrives from the native click monitor on the UI thread when a click lands inside this surface.
        if (_registrations.TryGetValue(coreWebView, out var registration))
        {
            Report(registration);
        }
    }

    public bool IsRegisteredWebSurface(DependencyObject element)
    {
        return element is WebView2 webView
            && webView.CoreWebView2 is not null
            && _registrations.ContainsKey(webView.CoreWebView2);
    }

    public bool HasFocusedSurface => _focusedRegistration is not null;

    public void FocusFocusedSurface()
    {
        var registration = _focusedRegistration;
        if (registration is null)
        {
            return;
        }

        _logger.LogTrace("Applying platform focus to web surface {Surface}", registration.SurfaceName);

        // Keyboard focus only: no report (app-level focus state has not changed) and no DOM-side grant
        // (the page's caret is exactly where the user put it and must not move).
        _webViewAdapter.FocusWebView(registration.WebView);
    }

    public bool TryForwardKeyEvent(IntPtr nativeKeyEvent)
    {
        var registration = _focusedRegistration;
        if (registration is null
            || !OperatingSystem.IsMacOS())
        {
            return false;
        }

        var coreWebView = registration.WebView.CoreWebView2;
        if (coreWebView is null)
        {
            return false;
        }

        if (!Platform.MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView, out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not forward a key to the focused web surface: {Detail}", detail);
            return false;
        }

        Platform.MacOSWebViewInterop.SendKeyDownToWebView(nativeHandle, nativeKeyEvent);

        return true;
    }

    public bool TryHandleTabKey(bool shift, IntPtr nativeKeyEvent)
    {
        var registration = _focusedRegistration;
        if (registration is null)
        {
            return false;
        }

        // The surface's own edit target takes the key first. Consulting the registration rather than the
        // focus service matters: the service preserves the previous edit target across a claim by a surface
        // that has none, so a .webview focused after a code editor would otherwise indent the hidden editor.
        if (registration.EditTarget is not null
            && registration.EditTarget.TryHandleTabKey(shift))
        {
            return true;
        }

        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var coreWebView = registration.WebView.CoreWebView2;
        if (coreWebView is null)
        {
            return true;
        }

        // Deliver the key straight to the page so it applies its own Tab behaviour (moving between form
        // fields). Reported handled even when the native handle cannot be resolved: a swallowed Tab beats
        // one the managed focus loop uses to walk focus out of the document.
        if (Platform.MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView, out var nativeHandle, out var detail))
        {
            Platform.MacOSWebViewInterop.SendKeyDownToWebView(nativeHandle, nativeKeyEvent);
        }
        else
        {
            _logger.LogWarning("Could not deliver Tab to the focused web surface: {Detail}", detail);
        }

        return true;
    }

    private void Report(WebViewFocusRegistration registration)
    {
        _logger.LogTrace("Web surface {Surface} reported focus", registration.SurfaceName);

        var wasAlreadyFocused = ReferenceEquals(_focusedRegistration, registration);
        _focusedRegistration = registration;

        // Gaining focus is not the same as re-reporting focus already held, and only the first runs the
        // surface's side effect. A document's side effect makes it the active document, and the active
        // document changing is itself what carries the keyboard to it, so a side effect on every report
        // would leave the two driving each other without end.
        if (!wasAlreadyFocused)
        {
            registration.OnFocusGained?.Invoke();
        }
        Action releaseFocus = () => ReleaseSurface(registration);
        var claim = FocusClaim.FromWebSurface(
            registration.Panel,
            registration.EditTarget,
            registration,
            releaseFocus);
        _focusService.OnFocusReceived(claim);

        // Applied here rather than only on the grant path so every claim converges, however it arrived: a
        // click landing inside a native web view reports through the monitor without any managed focus
        // change, so without this the managed control the user last used keeps consuming keys the page
        // should receive. The model is updated first because the reconciler derives from it.
        _focusReconciler ??= ServiceLocator.AcquireService<IFocusReconciler>();
        _focusReconciler.Reconcile();
    }

    // The release callback handed to the focus service, invoked when another surface or panel claims focus.
    // Clears the focused-surface tracking (unless a newer report has already replaced it) before running the
    // surface's own release.
    private void ReleaseSurface(WebViewFocusRegistration registration)
    {
        if (ReferenceEquals(_focusedRegistration, registration))
        {
            _focusedRegistration = null;
        }

        registration.ReleaseFocus();
    }
}
