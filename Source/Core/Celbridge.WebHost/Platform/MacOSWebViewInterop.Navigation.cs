using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// What Uno's WKWebView does not tell managed code about a navigation: the moment it commits, and whether it
/// moves within the page the web view already shows.
/// </summary>
public static partial class MacOSWebViewInterop
{
    // Where each web view's commits are reported, keyed by native web view. Touched only on the main thread.
    private static readonly Dictionary<IntPtr, Action<string>> _commitListeners = new();

    // The implementation the commit hook took the place of, which Uno's web view does not have.
    private static IntPtr _originalDidCommitNavigation;

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

    // Removes what a surface registered for a web view, such as where its commits are reported.
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
