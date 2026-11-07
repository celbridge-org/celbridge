using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// A window a page asked WebKit to open, waiting for its answer. The page's script stays paused until the answer
/// arrives, so every request gets exactly one answer, and soon.
/// </summary>
public sealed record MacNewWindowAction(string Url, bool IsUserInitiated);

/// <summary>
/// Receives each web view's requests to open and close windows. Every member is called on the main thread from inside
/// a WebKit callback.
/// </summary>
public interface IMacOSNewWindowListener
{
    /// <summary>
    /// A page in the web view asked for a new window. Returns true when the listener takes the request, and
    /// answers it through AnswerNewWindowRequest. False leaves the request to Uno's own handling.
    /// </summary>
    bool OnNewWindowRequested(IntPtr webView, IntPtr request, MacNewWindowAction action);

    /// <summary>
    /// The page in the web view closed itself.
    /// </summary>
    void OnPageClosed(IntPtr webView);
}

/// <summary>
/// Adds what Uno's WKWebView lacks for a page's new window: the link from the new page to the opener, and the report of
/// a page closing itself.
/// </summary>
public static partial class MacOSWebViewInterop
{
    // Every member below is touched only on the main thread, where WebKit calls back.
    private static IMacOSNewWindowListener? _newWindowListener;

    private static bool _newWindowHooksInstalled;

    // The implementations the hooks took the place of.
    private static IntPtr _originalCreateWebView;
    private static IntPtr _originalInitWithFrame;

    // The configuration WebKit gave for a new window. The next UNOWebView takes this configuration in place of Uno's own.
    private static IntPtr _nextWebViewConfiguration;

    // The requests waiting on an answer, with WebKit's completion handler for each. A request is its handler's address.
    private static readonly HashSet<IntPtr> _pendingNewWindows = new();

    // The configuration for each waiting request, which builds the request's view.
    private static readonly Dictionary<IntPtr, IntPtr> _newWindowConfigurations = new();

    /// <summary>
    /// Reports the web view's new window requests and closing pages to the listener. One listener hears every web
    /// view, so every call must pass the same listener. The hooks go on Uno's class once per process. The web view's
    /// delegate is then set again, because WebKit reads a delegate's methods only when the delegate is set. Returns
    /// false with the reason in detail when the hooks cannot go on Uno's class.
    /// </summary>
    // UNO-BUG: UNOWebView answers every new window with nil, which cuts the new page off from the opener. UNOWebView
    // also lacks webViewDidClose:.
    public static bool ObserveNewWindows(IntPtr webView, IMacOSNewWindowListener listener, out string detail)
    {
        detail = string.Empty;

        if (webView == IntPtr.Zero)
        {
            detail = "the native web view is null";
            return false;
        }

        if (!TryInstallNewWindowHooks(out detail))
        {
            return false;
        }

        _newWindowListener = listener;

        var uiDelegate = SendMessage(webView, GetSelector("UIDelegate"));
        if (uiDelegate == IntPtr.Zero)
        {
            detail = "the web view has no UI delegate";
            return false;
        }

        SendMessageVoid(webView, GetSelector("setUIDelegate:"), uiDelegate);

        return true;
    }

    /// <summary>
    /// Runs an action that creates exactly one UNOWebView. That view is built from the configuration WebKit gave for the
    /// request, which links the new page to the opener. Returns false when the action created no view.
    /// </summary>
    public static bool CreateWebViewForNewWindow(IntPtr request, Action createWebView)
    {
        if (!_newWindowConfigurations.TryGetValue(request, out var configuration))
        {
            return false;
        }

        // WebKit copies the opener's configuration, and the user content controller with it. Uno adds a script message
        // handler of a fixed name to each view's controller, and a second one of that name aborts the process. A
        // new page also has no use for the opener's scripts, since the view installs its own.
        var controller = SendMessage(SendMessage(GetClass("WKUserContentController"), GetSelector("alloc")), GetSelector("init"));
        SendMessageVoid(configuration, GetSelector("setUserContentController:"), controller);
        SendMessage(controller, GetSelector("release"));

        _nextWebViewConfiguration = configuration;
        try
        {
            createWebView();

            return _nextWebViewConfiguration == IntPtr.Zero;
        }
        finally
        {
            _nextWebViewConfiguration = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Answers a new window request with the web view that shows the new page, or with zero to open no window. Does
    /// nothing for a request that has already been answered.
    /// </summary>
    public static void AnswerNewWindowRequest(IntPtr request, IntPtr webView)
    {
        if (!_pendingNewWindows.Remove(request))
        {
            return;
        }

        if (_newWindowConfigurations.Remove(request, out var configuration))
        {
            SendMessage(configuration, GetSelector("release"));
        }

        InvokeObjectHandler(request, webView);
        _Block_release(request);
    }

    private static unsafe bool TryInstallNewWindowHooks(out string detail)
    {
        detail = string.Empty;

        if (_newWindowHooksInstalled)
        {
            return true;
        }

        var webViewClass = GetClass("UNOWebView");
        if (webViewClass == IntPtr.Zero ||
            class_getInstanceMethod(webViewClass, GetSelector("initWithFrame:configuration:")) == IntPtr.Zero)
        {
            detail = "Uno's web view class has no initWithFrame:configuration: to build a linked view through";
            return false;
        }

        // Recorded before the hooks go in, so a failure part way through cannot hook the class twice.
        _newWindowHooksInstalled = true;

        _originalInitWithFrame = HookMethod(
            webViewClass,
            "initWithFrame:configuration:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, CGRect, IntPtr, IntPtr>)&InitWithFrameHook,
            "@@:{CGRect={CGPoint=dd}{CGSize=dd}}@");

        _originalCreateWebView = method_getImplementation(class_getInstanceMethod(
            webViewClass,
            GetSelector("webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:")));

        // Private SPI: WebKit waits for this method's completion handler, where the public method has to return a
        // view at once. A view needs its visual tree and a layout pass, which cannot happen inside a callback.
        HookMethod(
            webViewClass,
            "_webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:completionHandler:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&CreateWebViewHook,
            "v@:@@@@@?");

        HookMethod(
            webViewClass,
            "webViewDidClose:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&WebViewDidCloseHook,
            "v@:@");

        return true;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe IntPtr InitWithFrameHook(IntPtr self, IntPtr selector, CGRect frame, IntPtr configuration)
    {
        var effectiveConfiguration = configuration;

        var nextConfiguration = _nextWebViewConfiguration;
        if (nextConfiguration != IntPtr.Zero)
        {
            _nextWebViewConfiguration = IntPtr.Zero;
            effectiveConfiguration = nextConfiguration;
        }

        return ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, CGRect, IntPtr, IntPtr>)_originalInitWithFrame)(
            self,
            selector,
            frame,
            effectiveConfiguration);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void CreateWebViewHook(
        IntPtr self,
        IntPtr selector,
        IntPtr webView,
        IntPtr configuration,
        IntPtr navigationAction,
        IntPtr windowFeatures,
        IntPtr completionHandler)
    {
        // WebKit keeps the page paused until the handler runs, and raises an exception if it never does. Never let an
        // exception unwind into WebKit.
        var request = _Block_copy(completionHandler);
        try
        {
            var listener = _newWindowListener;
            if (listener is not null)
            {
                _pendingNewWindows.Add(request);
                _newWindowConfigurations[request] = SendMessage(configuration, GetSelector("retain"));

                var action = new MacNewWindowAction(
                    ReadRequestUrl(navigationAction) ?? string.Empty,
                    ReadIsUserInitiated(navigationAction));

                if (listener.OnNewWindowRequested(webView, request, action))
                {
                    return;
                }

                _pendingNewWindows.Remove(request);
                _newWindowConfigurations.Remove(request);
                SendMessage(configuration, GetSelector("release"));
            }

            var createdWebView = IntPtr.Zero;
            if (_originalCreateWebView != IntPtr.Zero)
            {
                createdWebView = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)_originalCreateWebView)(
                    self,
                    GetSelector("webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:"),
                    webView,
                    configuration,
                    navigationAction,
                    windowFeatures);
            }

            InvokeObjectHandler(request, createdWebView);
            _Block_release(request);
        }
        catch
        {
            AnswerAfterFailure(request);
        }
    }

    private static void AnswerAfterFailure(IntPtr request)
    {
        try
        {
            if (_pendingNewWindows.Contains(request))
            {
                AnswerNewWindowRequest(request, IntPtr.Zero);
                return;
            }

            InvokeObjectHandler(request, IntPtr.Zero);
            _Block_release(request);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void WebViewDidCloseHook(IntPtr self, IntPtr selector, IntPtr webView)
    {
        // Never let an exception unwind into WebKit.
        try
        {
            _newWindowListener?.OnPageClosed(webView);
        }
        catch
        {
        }
    }

    // Returns true when the page asks for the window while handling the user's input. WebKit carries a gesture across a short
    // timer and a pending fetch, so a sign-in that waits on a response still counts. Without the SPI, only a link the
    // user followed counts.
    private static bool ReadIsUserInitiated(IntPtr navigationAction)
    {
        var selector = GetSelector("_isUserInitiated");
        if (SendMessageReturnBool(navigationAction, RespondsToSelectorSelector, selector))
        {
            return SendMessageReturnBool(navigationAction, selector);
        }

        const nint NavigationTypeLinkActivated = 0;

        return SendMessageReturnNint(navigationAction, GetSelector("navigationType")) == NavigationTypeLinkActivated;
    }
}
