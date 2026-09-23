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
/// taken in WebKit's own navigation policy callback, before any request for it is sent, whether the user
/// started a new window the page asks for, and the moment a navigation commits.
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

    // What each web view's commits are reported to, by native web view. Touched only on the main thread.
    private static readonly Dictionary<IntPtr, Action<string>> _commitListeners = new();

    // The implementation the new-window hook took the place of.
    private static IntPtr _originalCreateWebView;

    // The window WebKit is asking for, while Uno raises NewWindowRequested from inside the request. Touched
    // only on the main thread.
    private static WindowRequest? _windowRequest;

    // The implementation the commit hook took the place of, which Uno's web view does not have.
    private static IntPtr _originalDidCommitNavigation;

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

        return new WebViewRegistration<MacNavigationGate>(_navigationGates, webView, gate);
    }

    /// <summary>
    /// Reports the address of each page the web view commits to, as WebKit commits it, and returns the
    /// registration that stops the reports again. Returns null with the reason in detail when the web view's
    /// navigation delegate cannot be hooked.
    /// </summary>
    // UNO-BUG: UNOWebView implements no didCommitNavigation, and sets CoreWebView2.Source only once a page has
    // finished loading.
    public static IDisposable? ObserveNavigationCommits(IntPtr webView, Action<string> onCommitted, out string detail)
    {
        if (!TryHookNavigationDelegate(webView, out detail))
        {
            return null;
        }

        _commitListeners[webView] = onCommitted;

        return new WebViewRegistration<Action<string>>(_commitListeners, webView, onCommitted);
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

    private static unsafe void InstallCommitHook(IntPtr delegateClass)
    {
        _originalDidCommitNavigation = HookMethod(
            delegateClass,
            "webView:didCommitNavigation:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&DidCommitNavigationHook,
            "v@:@@");
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void DidCommitNavigationHook(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigation)
    {
        // Never let an exception unwind into WebKit.
        try
        {
            if (_originalDidCommitNavigation != IntPtr.Zero)
            {
                CallOriginalImplementation(_originalDidCommitNavigation, self, selector, webView, navigation);
            }

            if (!_commitListeners.TryGetValue(webView, out var onCommitted))
            {
                return;
            }

            var url = ReadCommittedUrl(webView);
            if (url is null)
            {
                return;
            }

            onCommitted(url);
        }
        catch
        {
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

        if (!IsMainFrameNavigation(navigationAction))
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

    /// <summary>
    /// Whether the navigation moves within the page the web view already shows, as a link to an anchor
    /// further down it does. Such a navigation loads nothing: WebKit scrolls the page it has.
    /// </summary>
    // UNO-BUG: Uno handles these itself and reads the managed callback's answer the wrong way round -- the
    // callback returns 1 for a navigation it allows, which Uno's native code takes as a refusal -- so every
    // one of them is cancelled and the page never moves. Answering before Uno sees it leaves WebKit to
    // scroll. The NavigationStarting and NavigationCompleted events Uno raises for an anchor go with it,
    // which is no loss: nothing here reads them for a navigation that loads no content.
    internal static bool IsSameDocumentNavigation(IntPtr webView, IntPtr navigationAction)
    {
        if (!IsMainFrameNavigation(navigationAction))
        {
            return false;
        }

        return IsSameDocument(ReadRequestUrl(navigationAction), ReadCommittedUrl(webView));
    }

    /// <summary>
    /// Whether the destination names a place in the committed page rather than a page of its own: the same
    /// address, and a fragment to move to. An address with no fragment, or one that names another page, is
    /// a navigation like any other.
    /// </summary>
    internal static bool IsSameDocument(string? destinationUrl, string? committedUrl)
    {
        if (string.IsNullOrEmpty(destinationUrl) || string.IsNullOrEmpty(committedUrl))
        {
            return false;
        }

        var fragmentIndex = destinationUrl.IndexOf('#');
        if (fragmentIndex < 0)
        {
            return false;
        }

        return string.Equals(
            destinationUrl[..fragmentIndex],
            BeforeFragment(committedUrl),
            StringComparison.Ordinal);
    }

    private static string BeforeFragment(string url)
    {
        var fragmentIndex = url.IndexOf('#');

        return fragmentIndex < 0 ? url : url[..fragmentIndex];
    }

    // A navigation of the page itself, rather than of a frame inside it or of a window it asks for.
    private static bool IsMainFrameNavigation(IntPtr navigationAction)
    {
        var targetFrame = SendMessage(navigationAction, GetSelector("targetFrame"));

        return targetFrame != IntPtr.Zero &&
            SendMessageReturnBool(targetFrame, GetSelector("isMainFrame"));
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

    // The address of the page the web view has committed to, or null where it names none. WebKit's public URL
    // can name an address the web view has only been asked to load, so the committed one is read through SPI
    // where WebKit offers it.
    private static string? ReadCommittedUrl(IntPtr webView)
    {
        var url = IntPtr.Zero;

        var committedUrlSelector = GetSelector("_committedURL");
        if (SendMessageReturnBool(webView, RespondsToSelectorSelector, committedUrlSelector))
        {
            url = SendMessage(webView, committedUrlSelector);
        }

        if (url == IntPtr.Zero)
        {
            url = SendMessage(webView, GetSelector("URL"));
        }

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

    // Removes what a surface registered for a web view, such as its gate.
    private sealed class WebViewRegistration<T> : IDisposable
        where T : class
    {
        private readonly Dictionary<IntPtr, T> _registrations;
        private readonly IntPtr _webView;
        private readonly T _registered;

        public WebViewRegistration(Dictionary<IntPtr, T> registrations, IntPtr webView, T registered)
        {
            _registrations = registrations;
            _webView = webView;
            _registered = registered;
        }

        // Only what is still registered for the web view, so a late dispose cannot undo what another surface
        // has registered for it since.
        public void Dispose()
        {
            if (_registrations.TryGetValue(_webView, out var current) &&
                ReferenceEquals(current, _registered))
            {
                _registrations.Remove(_webView);
            }
        }
    }
}
