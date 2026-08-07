using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Web.WebView2.Core;

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

    // Resolved lazily: the reconciler depends on this registry, so constructor-injecting it here would cycle.
    private IFocusReconciler? _focusReconciler;

    // A grant for a surface that had not registered yet, applied when that web view registers. A freshly
    // opened document is activated before its web view finishes initializing.
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
        }

        _registrations[coreWebView] = registration;

        // The managed GotFocus is the Windows gain signal and also fires for clicks on non-focusable content
        // that raise no DOM focus event. The native monitor is the macOS equivalent; a no-op elsewhere.
        registration.WebView.GotFocus += OnWebViewGotFocus;
        _webViewFocusMonitor.Register(coreWebView, () => OnNativeFocusSignal(coreWebView));

        if (ReferenceEquals(_pendingGrant, registration.WebView))
        {
            GrantFocus(registration.WebView);
        }
    }

    public void Unregister(CoreWebView2 coreWebView)
    {
        if (!_registrations.Remove(coreWebView, out var registration))
        {
            return;
        }

        if (ReferenceEquals(_focusedRegistration, registration))
        {
            _focusedRegistration = null;
        }

        registration.WebView.GotFocus -= OnWebViewGotFocus;
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

    private void OnNativeFocusSignal(CoreWebView2 coreWebView)
    {
        // Arrives from the native click monitor on the UI thread when a click lands inside this surface.
        if (_registrations.TryGetValue(coreWebView, out var registration))
        {
            Report(registration);
        }
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
        _focusService.OnFocusReceived(registration.Panel, registration.EditTarget, () => ReleaseSurface(registration));

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
