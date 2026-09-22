using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Decides whether a navigation of a web view's page may go ahead, given its URL and whether the user
/// started it. Called on the main thread from inside WebKit's navigation policy callback, so it must answer
/// at once.
/// </summary>
public delegate bool MacNavigationGate(string url, bool isUserInitiated);

/// <summary>
/// The navigation gate Uno's WKWebView lacks: a decision on a navigation of the page taken in WebKit's own
/// navigation policy callback, before any request for it is sent.
/// </summary>
public static partial class MacOSWebViewInterop
{
    // WKNavigationTypeLinkActivated.
    private const long NavigationTypeLinkActivated = 0;

    // The gate each web view's page navigations are put to, by native web view. Touched only on the main
    // thread, where WebKit calls back and where gates are added and removed.
    private static readonly Dictionary<IntPtr, MacNavigationGate> _navigationGates = new();

    /// <summary>
    /// Puts every navigation of the web view's page to the gate before WebKit sends a request for it, and
    /// returns the registration that removes the gate again. Returns null with the reason in detail when the
    /// web view's navigation delegate cannot be hooked.
    /// </summary>
    // UNO-BUG: UNOWebView raises NavigationStarting from didStartProvisionalNavigation, once the request is
    // under way, so a navigation cancelled there has already reached its server.
    public static IDisposable? GateNavigations(IntPtr webView, MacNavigationGate gate, out string detail)
    {
        if (!TryHookNavigationDelegate(webView, out detail))
        {
            return null;
        }

        _navigationGates[webView] = gate;

        return new NavigationGateRegistration(webView, gate);
    }

    // False when the web view's gate refuses the navigation. A navigation that opens a window or loads a
    // frame inside the page is not the page's own, and is never put to the gate.
    private static bool IsNavigationAllowed(IntPtr webView, IntPtr navigationAction)
    {
        if (!_navigationGates.TryGetValue(webView, out var gate))
        {
            return true;
        }

        var targetFrame = SendMessage(navigationAction, GetSelector("targetFrame"));
        if (targetFrame == IntPtr.Zero ||
            !SendMessageReturnBool(targetFrame, GetSelector("isMainFrame")))
        {
            return true;
        }

        var request = SendMessage(navigationAction, GetSelector("request"));
        var url = request == IntPtr.Zero
            ? IntPtr.Zero
            : SendMessage(request, GetSelector("URL"));
        if (url == IntPtr.Zero)
        {
            return true;
        }

        var urlText = ReadNSString(SendMessage(url, GetSelector("absoluteString")));

        return gate(urlText, IsUserInitiated(navigationAction));
    }

    // Whether a user gesture started the navigation, which is what WebView2 reports as IsUserInitiated.
    // WebKit says so only through SPI, and a followed link is the nearest public signal where it does not.
    private static bool IsUserInitiated(IntPtr navigationAction)
    {
        var userInitiatedSelector = GetSelector("_isUserInitiated");
        if (SendMessageReturnBool(navigationAction, RespondsToSelectorSelector, userInitiatedSelector))
        {
            return SendMessageReturnBool(navigationAction, userInitiatedSelector);
        }

        return SendMessageReturnLong(navigationAction, GetSelector("navigationType")) == NavigationTypeLinkActivated;
    }

    private sealed class NavigationGateRegistration : IDisposable
    {
        private readonly IntPtr _webView;
        private readonly MacNavigationGate _gate;

        public NavigationGateRegistration(IntPtr webView, MacNavigationGate gate)
        {
            _webView = webView;
            _gate = gate;
        }

        // Only the gate still registered for the web view, so a late dispose cannot ungate a web view another
        // surface has gated since.
        public void Dispose()
        {
            if (_navigationGates.TryGetValue(_webView, out var registeredGate) &&
                registeredGate == _gate)
            {
                _navigationGates.Remove(_webView);
            }
        }
    }
}
