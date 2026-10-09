using System.Runtime.CompilerServices;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// One registration of a web view. The focus service compares registrations by reference, so each one is a
/// separate surface.
/// </summary>
internal sealed class WebViewFocusRegistration : IFocusSurface
{
    public WebViewFocusRegistration(IWebView view, WebViewFocusContext context)
    {
        View = view;
        Context = context;
    }

    public IWebView View { get; }

    public WebViewFocusContext Context { get; }

    public string SurfaceName => View.Resource.ToString();
}

internal class WebViewFocusRegistry : IWebViewFocusRegistry
{
    private readonly IFocusService _focusService;
    private readonly IWebViewAdapter _webViewAdapter;
    private readonly IWebViewFocusMonitor _webViewFocusMonitor;
    private readonly IMessengerService _messengerService;
    private readonly IWebSurfaceMessageDispatcher _messageDispatcher;
    private readonly ILogger<WebViewFocusRegistry> _logger;

    // Used only on the UI thread. Views register and close there, and focus signals are marshalled there.
    private readonly Dictionary<IWebView, WebViewFocusRegistration> _registrations = new();

    // The views whose Closing event the registry has subscribed to.
    private readonly HashSet<IWebView> _observedViews = new();

    // The surface whose focus report is current. Cleared when the focus service releases it in favour of
    // another surface or panel (via the wrapped release callback in Report), and when its view closes.
    private WebViewFocusRegistration? _focusedRegistration;

    // The surfaces that already have the focus-lost script. It is installed once per surface, because a redock
    // registers the same view again and a script cannot be removed on every head. Weak keys never keep a web
    // view alive.
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

    // A grant for a view that has not registered yet. It applies when the view registers, since a new document
    // is activated before its web view is ready. It is dropped if the user moves focus elsewhere first, or if
    // the view closes before registering.
    private IWebView? _pendingGrant;

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

    public void Register(IWebView view, WebViewFocusContext focusContext)
    {
        var registration = new WebViewFocusRegistration(view, focusContext);
        var webView = GetControl(view);

        // A redock registers a live view under a new focus context, as when a utility moves between the Utility
        // Panel and a document tab. The keyboard never left the view, so the focus model follows it to the new
        // panel.
        var replacesFocusedSurface = false;
        if (_registrations.TryGetValue(view, out var previousRegistration))
        {
            DetachSurfaceHandlers(view);
            replacesFocusedSurface = ReferenceEquals(_focusedRegistration, previousRegistration);
        }

        _registrations[view] = registration;
        ObserveClosing(view);

        if (webView is not null &&
            GetCoreWebView2(view) is CoreWebView2 coreWebView)
        {
            AttachSurfaceHandlers(view, webView, coreWebView);
        }

        if (replacesFocusedSurface)
        {
            // Granted rather than merely reported: the new registration is a different surface identity to
            // the focus service, so reporting it releases the old one, and releasing drops the page's caret.
            // The grant puts it back, which is what a redock should leave behind anyway.
            GrantFocus(view);
            return;
        }

        if (!ReferenceEquals(_pendingGrant, view))
        {
            return;
        }

        _pendingGrant = null;

        // The grant was issued for a panel the user has since moved away from, so applying it now would
        // pull the keyboard back off whatever they turned to while the surface was initializing.
        if (_focusService.FocusedPanel != focusContext.Panel)
        {
            _logger.LogDebug(
                "Dropped a deferred focus grant: focus moved to {Panel} while the surface was initializing",
                _focusService.FocusedPanel);
            return;
        }

        GrantFocus(view);
    }

    private void AttachSurfaceHandlers(IWebView view, WebView2 webView, CoreWebView2 coreWebView)
    {
        // Key forwarding checks which key a web view last received, so recording has to start before any web
        // view gets a key.
        if (OperatingSystem.IsMacOS())
        {
            Platform.MacOSWebViewInterop.ObserveKeyDownDelivery();
        }

        // The managed GotFocus is the Windows gain signal and also fires for clicks on non-focusable content
        // that raise no DOM focus event. The native monitor is the macOS equivalent; a no-op elsewhere.
        webView.GotFocus += OnWebViewGotFocus;
        _webViewFocusMonitor.Register(coreWebView, () => OnNativeFocusSignal(view));

        // The focus-lost signal comes back through the page rather than either of the gain paths above,
        // because neither the managed nor the native layer observes the keyboard leaving the web content.
        // It arrives over the message bus, which the surface joins here for as long as it is registered.
        _messageDispatcher.Attach(view);

        coreWebView.NavigationCompleted += OnNavigationCompleted;

        if (!_surfacesWithFocusLostScript.TryGetValue(coreWebView, out _))
        {
            _surfacesWithFocusLostScript.Add(coreWebView, new object());
            _ = InstallFocusLostScriptAsync(coreWebView);
        }
    }

    // Subscribed once per view, however many times it registers.
    private void ObserveClosing(IWebView view)
    {
        if (_observedViews.Add(view))
        {
            view.Closing += OnViewClosing;
        }
    }

    // Drops a closed view's registration.
    private void OnViewClosing(object? sender, EventArgs e)
    {
        if (sender is not IWebView view)
        {
            return;
        }

        view.Closing -= OnViewClosing;
        _observedViews.Remove(view);

        // A view can close before a deferred grant reaches it.
        if (ReferenceEquals(_pendingGrant, view))
        {
            _pendingGrant = null;
        }

        if (!_registrations.Remove(view, out var registration))
        {
            return;
        }

        var closedSurfaceHeldFocus = ReferenceEquals(_focusedRegistration, registration);
        if (closedSurfaceHeldFocus)
        {
            _focusedRegistration = null;
        }

        DetachSurfaceHandlers(view);

        if (GetCoreWebView2(view) is CoreWebView2 coreWebView)
        {
            _webViewFocusMonitor.Unregister(coreWebView);
        }

        if (closedSurfaceHeldFocus)
        {
            ClearFocusUnlessAnotherSurfaceClaims(registration);
        }

        // Invalidate the edit context on close so a closed editor cannot leave the Edit menu enabled. The
        // focus service keeps a newer target that has replaced this one.
        _focusService.ClearEditTarget(registration.Context.EditTarget);
    }

    public void GrantFocus(IWebView view)
    {
        if (!_registrations.TryGetValue(view, out var registration))
        {
            // The view is still initializing, so hold the intent until it registers. A later grant supersedes
            // this one, so the view the user last acted on is the one that takes focus.
            _pendingGrant = view;
            ObserveClosing(view);
            _logger.LogDebug("Focus granted to a web surface that has not registered yet; deferred until it does");

            return;
        }

        _pendingGrant = null;

        _logger.LogDebug("Focus granted to web surface {Surface}", registration.SurfaceName);

        // Reporting applies the claim, which also releases the previously focused surface immediately
        // rather than waiting for the JS focus round trip that a surface with no DOM-side grant never
        // produces. The optional DOM-side focus then places the caret.
        Report(registration);

        _ = registration.Context.GrantDomFocus?.Invoke();
    }

    private void OnWebViewGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WebView2 webView
            && WebViewBase.FromControl(webView) is WebViewBase view
            && _registrations.TryGetValue(view, out var registration))
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
    private void DetachSurfaceHandlers(IWebView view)
    {
        var webView = GetControl(view);
        if (webView is null ||
            GetCoreWebView2(view) is not CoreWebView2 coreWebView)
        {
            return;
        }

        webView.GotFocus -= OnWebViewGotFocus;
        coreWebView.NavigationCompleted -= OnNavigationCompleted;

        _messageDispatcher.Detach(view);
    }

    // Null for a view without a control, as in a test. Such a view takes part in the focus model only.
    private static WebView2? GetControl(IWebView view)
    {
        return (view as WebViewBase)?.Control;
    }

    private static CoreWebView2? GetCoreWebView2(IWebView view)
    {
        return (view as WebViewBase)?.CoreWebView2;
    }

    // The surface holding the keyboard has closed, so nothing holds it any more. Deferred rather than applied
    // here because closing a document activates the next one, which claims focus a step later: clearing now
    // would take the caret straight back off it. If nothing has claimed by then, the focus model is left naming
    // a panel whose surface is gone, and the focus indicator would show a caret nobody has.
    private void ClearFocusUnlessAnotherSurfaceClaims(WebViewFocusRegistration registration)
    {
        var dispatcherQueue = GetControl(registration.View)?.DispatcherQueue;
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
                        "Cleared focus after the focused web surface {Surface} closed",
                        registration.SurfaceName);

                    _focusService.ClearFocus();
                }));
    }

    private void OnFocusLostMessage(WebSurfaceMessage message)
    {
        if (_registrations.TryGetValue(message.View, out var registration))
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

        var dispatcherQueue = GetControl(registration.View)?.DispatcherQueue;
        if (dispatcherQueue is null)
        {
            return;
        }

        // Queued below the focus reconcile, which the resign that caused this blur queues at the same
        // priority and therefore ahead of it. Reading the settled state is what separates a blur the host
        // caused from one the user did.
        dispatcherQueue.TryEnqueue(
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

    // Whether the keyboard still belongs to this surface. A page reports a blur every time it loses focus,
    // including when the host moved focus around and handed it straight back, so the page's own report cannot
    // tell that apart from the user clicking away. The platform can, so it is asked here.
    private bool HoldsPlatformKeyboardFocus(WebViewFocusRegistration registration)
    {
        if (!OperatingSystem.IsMacOS())
        {
            // Off macOS the WebView control itself holds keyboard focus, so focus still being on it means the
            // page has not really lost the keyboard, whatever it reported along the way. Anything that does
            // take the keyboard away moves focus off the control, clicking the window caption included.
            return HoldsManagedFocus(registration);
        }

        // On macOS the keyboard goes to the native view inside the control rather than to the control, so the
        // question has to be put to macOS itself: is that view the window's first responder? It has to be
        // asked because the host gives that up and takes it straight back whenever it moves focus anywhere,
        // and the page reports the gap in between as an ordinary blur.
        var coreWebView = GetCoreWebView2(registration.View);
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

    private void OnNativeFocusSignal(IWebView view)
    {
        // Arrives from the native click monitor on the UI thread when a click lands inside this surface.
        if (!_registrations.TryGetValue(view, out var registration))
        {
            return;
        }

        // A click inside the surface that already holds the keyboard changes nothing. Held here rather
        // than in the monitor because this is where the surface holding focus is actually known.
        if (ReferenceEquals(_focusedRegistration, registration))
        {
            return;
        }

        Report(registration);
    }

    public bool IsRegisteredWebSurface(DependencyObject element)
    {
        return element is WebView2 webView
            && WebViewBase.FromControl(webView) is WebViewBase view
            && _registrations.ContainsKey(view);
    }

    public bool HasFocusedSurface => _focusedRegistration is not null;

    public bool IsFocusedSurface(IWebView view)
    {
        return ReferenceEquals(_focusedRegistration?.View, view);
    }

    public bool IsPressOnWebSurface => _webViewFocusMonitor.IsLastPressInWebView;

    public void FocusFocusedSurface()
    {
        var registration = _focusedRegistration;
        if (registration is null)
        {
            return;
        }

        _logger.LogTrace("Applying platform focus to web surface {Surface}", registration.SurfaceName);

        var webView = GetControl(registration.View);
        if (webView is null)
        {
            return;
        }

        // Keyboard focus only: no report (app-level focus state has not changed) and no DOM-side grant
        // (the page's caret is exactly where the user put it and must not move).
        _webViewAdapter.FocusWebView(webView);
    }

    // Whether keyboard focus is currently on this surface's WebView control. The focus manager is asked for
    // the answer, which is only right once a focus change has finished: called from inside a focus event it
    // still names the element being moved away from. Always false on macOS, where the keyboard goes to the
    // native view inside the control instead.
    private static bool HoldsManagedFocus(WebViewFocusRegistration registration)
    {
        var webView = GetControl(registration.View);
        var xamlRoot = webView?.XamlRoot;
        if (xamlRoot is null)
        {
            return false;
        }

        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);

        return ReferenceEquals(focusedElement, webView);
    }

    public bool TryForwardKeyEvent(IntPtr nativeKeyEvent)
    {
        var registration = _focusedRegistration;
        if (registration is null
            || !OperatingSystem.IsMacOS())
        {
            return false;
        }

        var coreWebView = GetCoreWebView2(registration.View);
        if (coreWebView is null)
        {
            return false;
        }

        // The page already has this key. WebKit sent it back because the page left it unhandled.
        if (Platform.MacOSWebViewInterop.HasWebViewReceivedKeyDown(nativeKeyEvent))
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

        // The edit target comes from the registration, so the key stays with the surface holding the keyboard.
        if (registration.Context.EditTarget.TryHandleTabKey(shift))
        {
            return true;
        }

        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var coreWebView = GetCoreWebView2(registration.View);
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
            registration.Context.OnFocusGained?.Invoke();
        }
        Action releaseFocus = () => ReleaseSurface(registration);
        var claim = FocusClaim.FromWebSurface(
            registration.Context.Panel,
            registration.Context.EditTarget,
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

        registration.Context.ReleaseFocus();
    }
}
