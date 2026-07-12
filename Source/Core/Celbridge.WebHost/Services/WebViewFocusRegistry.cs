using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.UI.Xaml;
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
    }

    public void Unregister(CoreWebView2 coreWebView)
    {
        if (!_registrations.Remove(coreWebView, out var registration))
        {
            return;
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
            return;
        }

        // The adapter gives the web content keyboard focus per platform (native first responder on macOS, where
        // managed focus would route keys away from the content); the optional DOM-side focus then places the
        // caret. Reporting the focus here releases the previously focused surface immediately rather than waiting
        // for the JS focus round trip, which a surface with no DOM-side grant never produces.
        _webViewAdapter.FocusWebView(webView);
        _ = registration.GrantDomFocus?.Invoke();

        Report(registration);
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

    private void Report(WebViewFocusRegistration registration)
    {
        registration.OnFocusGained?.Invoke();
        _focusService.OnFocusReceived(registration.Panel, registration.EditTarget, registration.ReleaseFocus);
    }
}
