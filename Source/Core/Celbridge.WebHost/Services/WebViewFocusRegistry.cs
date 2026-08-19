using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Celbridge.WebHost;

internal class WebViewFocusRegistry : IWebViewFocusRegistry
{
    private readonly IFocusService _focusService;
    private readonly IWebViewAdapter _webViewAdapter;
    private readonly IWebViewFocusMonitor _webViewFocusMonitor;
    private readonly ILogger<WebViewFocusRegistry> _logger;

    // Keyed by CoreWebView2, the stable surface identity shared with the native monitor. Accessed only on the UI
    // thread: Register/Unregister run from view lifecycle, and every gain signal is marshalled to the UI thread
    // before it reaches the registry.
    private readonly Dictionary<CoreWebView2, WebViewFocusRegistration> _registrations = new();

    // The surface whose focus report is current. Cleared when the focus service releases it in favour of
    // another surface or panel (via the wrapped release callback in Report), and on Unregister.
    private WebViewFocusRegistration? _focusedRegistration;

    // The web message handler per surface. The CoreWebView2 handed to the event is a different managed
    // projection of the same native object than the one the WebView2 property returns, so it cannot be used
    // to find the registration. Each surface gets a handler closing over the key it registered under.
    private readonly Dictionary<CoreWebView2, TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs>> _webMessageHandlers = new();

    // Resolved lazily: the reconciler depends on this registry, so constructor-injecting it here would cycle.
    private IFocusReconciler? _focusReconciler;

    // The JSON-RPC method the injected listener reports the loss under. The input namespace and the past
    // tense match the notifications the page already sends its host. A well formed notification rather
    // than a bare string because every registered surface also has a host channel reading the same web
    // message event, and that channel logs an error for anything it cannot deserialize. StreamJsonRpc drops
    // a notification naming a method nothing implements, so the channel ignores this one quietly.
    private const string FocusLostMethod = "input/focusLost";

    // Reports the surface losing the keyboard, which the managed layer cannot see: on the packaged Windows
    // head the web content lives in its own child window, so a click on the caption or on any non-focusable
    // region moves the keyboard off it without moving managed focus at all. Injected at document start
    // through the adapter seam rather than carried by the client bundle, so a page we did not author reports
    // its losses too.
    private const string FocusLostScript = """
        (function () {
            if (window.__celbridgeFocusLostInstalled) {
                return;
            }
            window.__celbridgeFocusLostInstalled = true;

            // macOS injects into every frame, and only the top document's focus stands for the surface.
            if (window.top !== window) {
                return;
            }

            var notification = JSON.stringify({ jsonrpc: '2.0', method: 'input/focusLost' });

            window.addEventListener('blur', function () {
                // Focus moving into an iframe of this same page also blurs the top window, and the document
                // still reports focus in that case, so settle on the next task before deciding it left.
                setTimeout(function () {
                    if (document.hasFocus()) {
                        return;
                    }

                    var webView = window.chrome && window.chrome.webview;
                    if (webView) {
                        webView.postMessage(notification);
                    }
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
        ILogger<WebViewFocusRegistry> logger)
    {
        _focusService = focusService;
        _webViewAdapter = webViewAdapter;
        _webViewFocusMonitor = webViewFocusMonitor;
        _logger = logger;
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
        // registration's GotFocus subscription before replacing it.
        if (_registrations.ContainsKey(coreWebView))
        {
            registration.WebView.GotFocus -= OnWebViewGotFocus;
            coreWebView.NavigationCompleted -= OnNavigationCompleted;
            RemoveWebMessageHandler(coreWebView);
        }

        _registrations[coreWebView] = registration;

        // The managed GotFocus is the Windows gain signal and also fires for clicks on non-focusable content
        // that raise no DOM focus event. The native monitor is the macOS equivalent; a no-op elsewhere.
        registration.WebView.GotFocus += OnWebViewGotFocus;
        _webViewFocusMonitor.Register(coreWebView, () => OnNativeFocusSignal(coreWebView));

        // The focus-lost signal comes back through the page rather than either of the gain paths above,
        // because neither the managed nor the native layer observes the keyboard leaving the web content.
        TypedEventHandler<CoreWebView2, CoreWebView2WebMessageReceivedEventArgs> webMessageHandler =
            (_, args) => OnWebMessageReceived(coreWebView, args);
        _webMessageHandlers[coreWebView] = webMessageHandler;
        coreWebView.WebMessageReceived += webMessageHandler;

        coreWebView.NavigationCompleted += OnNavigationCompleted;
        _ = InstallFocusLostScriptAsync(coreWebView);

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

        if (ReferenceEquals(_focusedRegistration, registration))
        {
            _focusedRegistration = null;
        }

        registration.WebView.GotFocus -= OnWebViewGotFocus;
        coreWebView.NavigationCompleted -= OnNavigationCompleted;
        RemoveWebMessageHandler(coreWebView);
        _webViewFocusMonitor.Unregister(coreWebView);

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

    private void RemoveWebMessageHandler(CoreWebView2 coreWebView)
    {
        if (_webMessageHandlers.Remove(coreWebView, out var webMessageHandler))
        {
            coreWebView.WebMessageReceived -= webMessageHandler;
        }
    }

    private void OnWebMessageReceived(CoreWebView2 coreWebView, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // This handler runs on the UI thread alongside the host channel reading the same event, so an
        // escaping exception would be fatal. A malformed web message must never crash the host.
        try
        {
            if (!_registrations.TryGetValue(coreWebView, out var registration))
            {
                return;
            }

            // Read as JSON rather than through TryGetWebMessageAsString, which throws on the macOS WKWebView
            // head where a message arrives as JSON rather than a string. That would cost a thrown exception
            // per message per surface, only to reach a discriminator. The marker carries no character JSON
            // escaping touches, so it matches whichever shape the message takes.
            var message = e.WebMessageAsJson;
            if (string.IsNullOrEmpty(message)
                || !message.Contains(FocusLostMethod, StringComparison.Ordinal))
            {
                // Every message the surface sends its host arrives here as well, so the marker is matched
                // before parsing rather than deserializing the whole RPC stream twice.
                return;
            }

            OnFocusLost(registration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read a web message while watching for focus loss");
        }
    }

    // The keyboard has left the page. Only the surface holding focus can lose it, and a click that moves
    // focus elsewhere claims the new panel before this arrives, so a loss that is still current on the
    // next turn of the UI thread is one that left focus nowhere.
    private void OnFocusLost(WebViewFocusRegistration registration)
    {
        if (!ReferenceEquals(_focusedRegistration, registration))
        {
            return;
        }

        var departingRegistration = registration;

        registration.WebView.DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_focusedRegistration, departingRegistration))
            {
                return;
            }

            _logger.LogDebug("The focused web surface reported that the keyboard left it");

            _focusService.ClearFocus();
        });
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
        _focusedRegistration = registration;
        registration.OnFocusGained?.Invoke();
        Action releaseFocus = () => ReleaseSurface(registration);
        var claim = FocusClaim.FromWebSurface(registration.Panel, registration.EditTarget, releaseFocus);
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
