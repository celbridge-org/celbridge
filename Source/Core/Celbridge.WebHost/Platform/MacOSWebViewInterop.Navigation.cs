using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Decides whether a navigation of a web view's page may go ahead, given its URL and whether the user
/// started it. Called on the main thread from inside WebKit's navigation policy callback, so it must answer
/// at once.
/// </summary>
public delegate bool MacNavigationGate(string url, bool isUserInitiated);

/// <summary>
/// What Uno's WKWebView does not tell managed code about a navigation: a decision on a navigation of the page
/// taken in WebKit's own navigation policy callback, before any request for it is sent, and whether the user
/// started a new window the page asks for.
/// </summary>
public static partial class MacOSWebViewInterop
{
    // A window WebKit is asking the UI delegate to open: its address, and whether a user gesture started it.
    private sealed record WindowRequest(string Url, bool IsUserInitiated);

    // WKNavigationTypeLinkActivated.
    private const long NavigationTypeLinkActivated = 0;

    // The gate each web view's page navigations are put to, by native web view. Touched only on the main
    // thread, where WebKit calls back and where gates are added and removed.
    private static readonly Dictionary<IntPtr, MacNavigationGate> _navigationGates = new();

    // The implementation the new-window hook took the place of.
    private static IntPtr _originalCreateWebView;

    // The window WebKit is asking for, while Uno raises NewWindowRequested from inside the request. Touched
    // only on the main thread.
    private static WindowRequest? _windowRequest;

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

    /// <summary>
    /// Whether a user gesture started the new window WebKit is asking for at the address. Uno raises
    /// NewWindowRequested from inside that request, so this answers only while a NewWindowRequested handler
    /// runs, and is false at any other time.
    /// </summary>
    // UNO-BUG: CoreWebView2NewWindowRequestedEventArgs.IsUserInitiated throws NotImplementedException.
    public static bool IsUserInitiatedWindowRequest(string url)
    {
        return _windowRequest is { IsUserInitiated: true } windowRequest &&
            string.Equals(windowRequest.Url, url, StringComparison.Ordinal);
    }

    // Uno's web view is its own UI delegate as well as its navigation delegate, so the new-window hook goes on
    // the same class. WebKit already knows the class opens windows, so the hook needs no delegate set again.
    private static unsafe void InstallNewWindowHook(IntPtr delegateClass)
    {
        _originalCreateWebView = HookMethod(
            delegateClass,
            "webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)&CreateWebViewHook,
            "@@:@@@@");
    }

    // Notes the window for the length of Uno's implementation, which raises NewWindowRequested, and puts back
    // whatever was noted before, so a request never outlives the call it belongs to.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe IntPtr CreateWebViewHook(
        IntPtr self,
        IntPtr selector,
        IntPtr webView,
        IntPtr configuration,
        IntPtr navigationAction,
        IntPtr windowFeatures)
    {
        var enclosingRequest = _windowRequest;
        try
        {
            var url = ReadRequestUrl(navigationAction);
            _windowRequest = url is null
                ? null
                : new WindowRequest(url, IsUserInitiated(navigationAction));
        }
        catch
        {
            _windowRequest = null;
        }

        try
        {
            if (_originalCreateWebView == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)_originalCreateWebView)(
                self,
                selector,
                webView,
                configuration,
                navigationAction,
                windowFeatures);
        }
        finally
        {
            _windowRequest = enclosingRequest;
        }
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

        var url = ReadRequestUrl(navigationAction);
        if (url is null)
        {
            return true;
        }

        return gate(url, IsUserInitiated(navigationAction));
    }

    // The address a navigation action asks for, or null where its request names none.
    private static string? ReadRequestUrl(IntPtr navigationAction)
    {
        var request = SendMessage(navigationAction, GetSelector("request"));
        var url = request == IntPtr.Zero
            ? IntPtr.Zero
            : SendMessage(request, GetSelector("URL"));
        if (url == IntPtr.Zero)
        {
            return null;
        }

        return ReadNSString(SendMessage(url, GetSelector("absoluteString")));
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
