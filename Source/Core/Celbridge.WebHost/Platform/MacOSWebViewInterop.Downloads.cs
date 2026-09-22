using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// What WebKit does with a navigation's response. The values are WebKit's own, so they pass straight to
/// its decision handler.
/// </summary>
public enum MacNavigationResponsePolicy
{
    Cancel = 0,
    Allow = 1,
    Download = 2
}

/// <summary>
/// What a navigation's response says about itself: whether it is for the page rather than a frame inside
/// it, whether WebKit can display its content, and its Content-Disposition header, empty when it has none.
/// </summary>
public sealed record MacNavigationResponse(bool IsForMainFrame, bool CanShowMimeType, string ContentDisposition);

/// <summary>
/// How much of a download has arrived, and how large it is, which is -1 where the server did not say.
/// </summary>
public sealed record MacDownloadProgress(long BytesReceived, long TotalBytes);

/// <summary>
/// Receives the downloads of the web views MacOSWebViewInterop.RouteDownloads was called for. Every member
/// is called on the main thread from inside a WebKit callback.
/// </summary>
public interface IMacOSDownloadListener
{
    /// <summary>
    /// Whether the web view's downloads are routed. A download the page asks for with a link's download
    /// attribute, or the user asks for from the page's context menu, is taken over only in a web view that is.
    /// </summary>
    bool IsRoutingDownloads(IntPtr webView);

    /// <summary>
    /// Decides whether a navigation's response is displayed, downloaded or dropped.
    /// </summary>
    MacNavigationResponsePolicy DecideNavigationResponse(IntPtr webView, MacNavigationResponse response);

    /// <summary>
    /// WebKit has started a download and asks where to write it. Answer through ProvideDownloadDestination.
    /// </summary>
    void OnDownloadDestinationRequested(IntPtr download, string suggestedFileName, string sourceUrl);

    /// <summary>
    /// The download has been written in full to the destination it was given.
    /// </summary>
    void OnDownloadFinished(IntPtr download);

    /// <summary>
    /// The download stopped before it finished. The description is WebKit's, for the log.
    /// </summary>
    void OnDownloadFailed(IntPtr download, bool isCancelled, string description);
}

/// <summary>
/// The WebKit download handling Uno's WKWebView lacks: the navigation policy that turns a navigation into a
/// download, and the WKDownloadDelegate that takes the download over and reports it to a listener.
/// </summary>
public static partial class MacOSWebViewInterop
{
    [DllImport(LibSystem)]
    private static extern IntPtr _Block_copy(IntPtr block);

    [DllImport(LibSystem)]
    private static extern void _Block_release(IntPtr block);

    [DllImport(LibObjC)]
    private static extern IntPtr class_getInstanceMethod(IntPtr classHandle, IntPtr selector);

    [DllImport(LibObjC)]
    private static extern IntPtr method_getImplementation(IntPtr method);

    [DllImport(LibObjC)]
    private static extern IntPtr method_setImplementation(IntPtr method, IntPtr implementation);

    [DllImport(LibObjC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr classHandle, IntPtr selector, IntPtr implementation, string types);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [DllImport(LibObjC)]
    private static extern void objc_registerClassPair(IntPtr classHandle);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getProtocol(string name);

    [DllImport(LibObjC)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addProtocol(IntPtr classHandle, IntPtr protocol);

    // A download's progress counts are int64_t, which comes back whole in the one return register.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern long SendMessageReturnLong(IntPtr receiver, IntPtr selector);

    // Where WebKit's caller finds the function a block runs: after the block's isa, flags and reserved words.
    private const int BlockInvokeOffset = 16;

    // WKNavigationActionPolicyCancel, WKNavigationActionPolicyAllow and WKNavigationActionPolicyDownload.
    private const nint NavigationActionPolicyCancel = 0;
    private const nint NavigationActionPolicyAllow = 1;
    private const nint NavigationActionPolicyDownload = 2;

    // NSURLErrorCancelled, the code a download stopped before it finished reports.
    private const nint UrlErrorCancelled = -999;

    // The download state is touched only on the main thread, where WebKit calls back and where
    // RouteDownloads, ProvideDownloadDestination and CancelDownload are called.
    private static IMacOSDownloadListener? _downloadListener;

    // The navigation delegate class the hooks were added to. Uno's web view is its own delegate, so this is
    // one class for the life of the process.
    private static IntPtr _hookedDelegateClass;

    // The object WebKit reports each download's destination request, finish and failure to.
    private static IntPtr _downloadDelegate;

    // The implementations the hooks took the place of, run for whatever a hook leaves alone.
    private static IntPtr _originalDecidePolicyForNavigationAction;
    private static IntPtr _originalDecidePolicyForNavigationResponse;
    private static IntPtr _originalNavigationActionDidBecomeDownload;
    private static IntPtr _originalNavigationResponseDidBecomeDownload;
    private static IntPtr _originalContextMenuDidCreateDownload;

    // The destination requests WebKit is waiting on an answer to, as copies of their completion handlers.
    private static readonly Dictionary<IntPtr, IntPtr> _pendingDownloadDestinations = new();

    // The downloads taken over from WebKit, each retained until WebKit reports how it ended, so a download a
    // listener still holds is never a freed object.
    private static readonly HashSet<IntPtr> _adoptedDownloads = new();

    /// <summary>
    /// Hands the web view's downloads to the listener. Uno's navigation delegate turns no navigation into a
    /// download and takes no download over, so without this WebKit drops every one. The missing methods are
    /// added to the delegate's class once per process, and the web view's delegate is set again, because
    /// WebKit reads which methods a delegate implements only when it is set. Returns false with the reason
    /// in detail when the delegate cannot be reached, or is not of the class the methods were added to.
    /// </summary>
    // UNO-BUG: UNOWebView implements no navigation response policy and takes no download over.
    public static bool RouteDownloads(IntPtr webView, IMacOSDownloadListener listener, out string detail)
    {
        if (!TryHookNavigationDelegate(webView, out detail))
        {
            return false;
        }

        _downloadListener = listener;

        return true;
    }

    // Adds the hooks to the class of the web view's navigation delegate, once per process, and sets the web
    // view's delegate again, since WebKit reads which methods a delegate implements only when it is set.
    private static bool TryHookNavigationDelegate(IntPtr webView, out string detail)
    {
        detail = string.Empty;

        if (webView == IntPtr.Zero)
        {
            detail = "the native web view is null";
            return false;
        }

        var navigationDelegate = SendMessage(webView, GetSelector("navigationDelegate"));
        if (navigationDelegate == IntPtr.Zero)
        {
            detail = "the web view has no navigation delegate";
            return false;
        }

        // -class rather than the object's isa, which names a key-value observing subclass for an object
        // that is being observed.
        var delegateClass = SendMessage(navigationDelegate, GetSelector("class"));

        if (_hookedDelegateClass != IntPtr.Zero &&
            _hookedDelegateClass != delegateClass)
        {
            detail = $"the navigation delegate is a {GetClassName(navigationDelegate)}, not the class the hooks were added to";
            return false;
        }

        EnsureDownloadDelegate();

        if (_hookedDelegateClass == IntPtr.Zero)
        {
            // Recorded before the hooks go in, so a failure part way through can never hook the class a
            // second time, which would make each hook the implementation it falls back to.
            _hookedDelegateClass = delegateClass;
            InstallDownloadHooks(delegateClass);
        }

        SendMessageVoid(webView, GetSelector("setNavigationDelegate:"), navigationDelegate);

        return true;
    }

    /// <summary>
    /// Answers WebKit's request for where to write a download, with an absolute file path, or with null to
    /// refuse the download. Does nothing for a request that has already been answered.
    /// </summary>
    public static void ProvideDownloadDestination(IntPtr download, string? filePath)
    {
        if (!_pendingDownloadDestinations.Remove(download, out var completionHandler))
        {
            return;
        }

        var destinationUrl = IntPtr.Zero;
        if (!string.IsNullOrEmpty(filePath))
        {
            destinationUrl = SendMessage(GetClass("NSURL"), GetSelector("fileURLWithPath:"), CreateNSString(filePath));
        }

        InvokeObjectHandler(completionHandler, destinationUrl);
        _Block_release(completionHandler);

        // A refused download may end without WebKit reporting it, so it is let go of now.
        if (destinationUrl == IntPtr.Zero)
        {
            ReleaseDownload(download);
        }
    }

    /// <summary>
    /// How much of a download has arrived so far. Reads nothing once WebKit has reported how the download
    /// ended.
    /// </summary>
    public static MacDownloadProgress GetDownloadProgress(IntPtr download)
    {
        if (!_adoptedDownloads.Contains(download))
        {
            return new MacDownloadProgress(0, -1);
        }

        var progress = SendMessage(download, GetSelector("progress"));
        if (progress == IntPtr.Zero)
        {
            return new MacDownloadProgress(0, -1);
        }

        var bytesReceived = SendMessageReturnLong(progress, GetSelector("completedUnitCount"));
        var totalBytes = SendMessageReturnLong(progress, GetSelector("totalUnitCount"));

        return new MacDownloadProgress(bytesReceived, totalBytes);
    }

    /// <summary>
    /// Stops a download WebKit is running. The listener hears nothing further about it.
    /// </summary>
    public static void CancelDownload(IntPtr download)
    {
        // Taken out of the adopted set before it is told, so a failure WebKit reports for the cancel is not
        // passed on, and released after, so it is alive while it is told.
        if (!_adoptedDownloads.Remove(download))
        {
            return;
        }

        SendMessageVoid(download, GetSelector("cancel:"), IntPtr.Zero);
        SendMessage(download, GetSelector("release"));
    }

    private static unsafe void InstallDownloadHooks(IntPtr delegateClass)
    {
        _originalDecidePolicyForNavigationAction = HookMethod(
            delegateClass,
            "webView:decidePolicyForNavigationAction:decisionHandler:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DecidePolicyForNavigationActionHook,
            "v@:@@@?");

        _originalDecidePolicyForNavigationResponse = HookMethod(
            delegateClass,
            "webView:decidePolicyForNavigationResponse:decisionHandler:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DecidePolicyForNavigationResponseHook,
            "v@:@@@?");

        _originalNavigationActionDidBecomeDownload = HookMethod(
            delegateClass,
            "webView:navigationAction:didBecomeDownload:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&NavigationActionDidBecomeDownloadHook,
            "v@:@@@");

        _originalNavigationResponseDidBecomeDownload = HookMethod(
            delegateClass,
            "webView:navigationResponse:didBecomeDownload:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&NavigationResponseDidBecomeDownloadHook,
            "v@:@@@");

        // Private SPI: WebKit hands a download the user starts from the page's context menu to the
        // navigation delegate through this, when the delegate implements it.
        _originalContextMenuDidCreateDownload = HookMethod(
            delegateClass,
            "_webView:contextMenuDidCreateDownload:",
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&ContextMenuDidCreateDownloadHook,
            "v@:@@");
    }

    // Adds the hook to the class, or puts it in place of the class's own implementation, and returns what
    // the class responded with before, which is zero where it did not respond at all.
    private static IntPtr HookMethod(IntPtr targetClass, string selectorName, IntPtr hook, string typeEncoding)
    {
        var selector = GetSelector(selectorName);
        var method = class_getInstanceMethod(targetClass, selector);
        var originalImplementation = method == IntPtr.Zero
            ? IntPtr.Zero
            : method_getImplementation(method);

        // Adding fails only where the class defines the method itself. An inherited implementation is left
        // on the superclass, and the hook falls back to it.
        if (class_addMethod(targetClass, selector, hook, typeEncoding) ||
            method == IntPtr.Zero)
        {
            return originalImplementation;
        }

        method_setImplementation(method, hook);

        return originalImplementation;
    }

    private static unsafe void EnsureDownloadDelegate()
    {
        if (_downloadDelegate != IntPtr.Zero)
        {
            return;
        }

        var delegateClass = objc_allocateClassPair(GetClass("NSObject"), "CelbridgeDownloadDelegate", 0);
        if (delegateClass != IntPtr.Zero)
        {
            class_addMethod(
                delegateClass,
                GetSelector("download:decideDestinationUsingResponse:suggestedFilename:completionHandler:"),
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DecideDownloadDestinationHook,
                "v@:@@@@?");

            class_addMethod(
                delegateClass,
                GetSelector("downloadDidFinish:"),
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&DownloadDidFinishHook,
                "v@:@");

            class_addMethod(
                delegateClass,
                GetSelector("download:didFailWithError:resumeData:"),
                (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DownloadDidFailHook,
                "v@:@@@");

            var downloadDelegateProtocol = objc_getProtocol("WKDownloadDelegate");
            if (downloadDelegateProtocol != IntPtr.Zero)
            {
                class_addProtocol(delegateClass, downloadDelegateProtocol);
            }

            objc_registerClassPair(delegateClass);
        }
        else
        {
            // Registered already by an earlier load of this code in the process.
            delegateClass = GetClass("CelbridgeDownloadDelegate");
        }

        var allocated = SendMessage(delegateClass, GetSelector("alloc"));
        _downloadDelegate = SendMessage(allocated, GetSelector("init"));
    }

    // Takes over a download WebKit has just started. WebKit holds a download only weakly to its delegate,
    // and the download itself is retained until WebKit reports how it ended.
    private static void AdoptDownload(IntPtr download)
    {
        if (download == IntPtr.Zero ||
            _downloadDelegate == IntPtr.Zero)
        {
            return;
        }

        if (_adoptedDownloads.Add(download))
        {
            SendMessage(download, GetSelector("retain"));
        }

        SendMessageVoid(download, GetSelector("setDelegate:"), _downloadDelegate);
    }

    private static void ReleaseDownload(IntPtr download)
    {
        if (_adoptedDownloads.Remove(download))
        {
            SendMessage(download, GetSelector("release"));
        }
    }

    private static MacNavigationResponse ReadNavigationResponse(IntPtr navigationResponse)
    {
        var isForMainFrame = SendMessageReturnBool(navigationResponse, GetSelector("isForMainFrame"));
        var canShowMimeType = SendMessageReturnBool(navigationResponse, GetSelector("canShowMIMEType"));

        var contentDisposition = string.Empty;
        var response = SendMessage(navigationResponse, GetSelector("response"));
        if (response != IntPtr.Zero &&
            SendMessageReturnBool(response, GetSelector("isKindOfClass:"), GetClass("NSHTTPURLResponse")))
        {
            var headerValue = SendMessage(
                response,
                GetSelector("valueForHTTPHeaderField:"),
                CreateNSString("Content-Disposition"));

            contentDisposition = ReadNSString(headerValue);
        }

        return new MacNavigationResponse(isForMainFrame, canShowMimeType, contentDisposition);
    }

    private static string ReadDownloadSourceUrl(IntPtr download)
    {
        var request = SendMessage(download, GetSelector("originalRequest"));
        if (request == IntPtr.Zero)
        {
            return string.Empty;
        }

        var url = SendMessage(request, GetSelector("URL"));
        if (url == IntPtr.Zero)
        {
            return string.Empty;
        }

        return ReadNSString(SendMessage(url, GetSelector("absoluteString")));
    }

    private static unsafe void InvokePolicyHandler(IntPtr block, nint policy)
    {
        var invoke = Marshal.ReadIntPtr(block, BlockInvokeOffset);
        ((delegate* unmanaged[Cdecl]<IntPtr, nint, void>)invoke)(block, policy);
    }

    private static unsafe void InvokeObjectHandler(IntPtr block, IntPtr argument)
    {
        var invoke = Marshal.ReadIntPtr(block, BlockInvokeOffset);
        ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)invoke)(block, argument);
    }

    private static unsafe void CallOriginalImplementation(
        IntPtr implementation,
        IntPtr self,
        IntPtr selector,
        IntPtr firstArgument,
        IntPtr secondArgument)
    {
        ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)implementation)(
            self,
            selector,
            firstArgument,
            secondArgument);
    }

    private static unsafe void CallOriginalImplementation(
        IntPtr implementation,
        IntPtr self,
        IntPtr selector,
        IntPtr firstArgument,
        IntPtr secondArgument,
        IntPtr thirdArgument)
    {
        ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)implementation)(
            self,
            selector,
            firstArgument,
            secondArgument,
            thirdArgument);
    }

    // A link's download attribute asks for a download, which is WebKit's own answer for a delegate that
    // does not decide, and which Uno's delegate never gives. A navigation of the page is put to the web
    // view's gate before any request for it is sent. Everything else is left to Uno's delegate.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void DecidePolicyForNavigationActionHook(
        IntPtr self,
        IntPtr selector,
        IntPtr webView,
        IntPtr navigationAction,
        IntPtr decisionHandler)
    {
        // Decided before anything is answered, so a throw can leave WebKit neither unanswered nor answered
        // twice. Never let an exception unwind into WebKit.
        var shouldDownload = false;
        var isRefused = false;
        try
        {
            var shouldPerformDownloadSelector = GetSelector("shouldPerformDownload");
            var isDownloadRequested =
                SendMessageReturnBool(navigationAction, RespondsToSelectorSelector, shouldPerformDownloadSelector) &&
                SendMessageReturnBool(navigationAction, shouldPerformDownloadSelector);

            shouldDownload = isDownloadRequested &&
                _downloadListener?.IsRoutingDownloads(webView) == true;

            isRefused = !shouldDownload &&
                !IsNavigationAllowed(webView, navigationAction);
        }
        catch
        {
        }

        if (shouldDownload)
        {
            InvokePolicyHandler(decisionHandler, NavigationActionPolicyDownload);
            return;
        }

        if (isRefused)
        {
            InvokePolicyHandler(decisionHandler, NavigationActionPolicyCancel);
            return;
        }

        if (_originalDecidePolicyForNavigationAction != IntPtr.Zero)
        {
            CallOriginalImplementation(
                _originalDecidePolicyForNavigationAction,
                self,
                selector,
                webView,
                navigationAction,
                decisionHandler);
            return;
        }

        InvokePolicyHandler(decisionHandler, NavigationActionPolicyAllow);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void DecidePolicyForNavigationResponseHook(
        IntPtr self,
        IntPtr selector,
        IntPtr webView,
        IntPtr navigationResponse,
        IntPtr decisionHandler)
    {
        var policy = MacNavigationResponsePolicy.Allow;
        try
        {
            var response = ReadNavigationResponse(navigationResponse);

            // WebKit's own answer where no delegate decides: show what it can and drop the rest.
            policy = response.CanShowMimeType
                ? MacNavigationResponsePolicy.Allow
                : MacNavigationResponsePolicy.Cancel;

            var listener = _downloadListener;
            if (listener is not null)
            {
                policy = listener.DecideNavigationResponse(webView, response);
            }
        }
        catch
        {
        }

        if (policy != MacNavigationResponsePolicy.Download &&
            _originalDecidePolicyForNavigationResponse != IntPtr.Zero)
        {
            CallOriginalImplementation(
                _originalDecidePolicyForNavigationResponse,
                self,
                selector,
                webView,
                navigationResponse,
                decisionHandler);
            return;
        }

        InvokePolicyHandler(decisionHandler, (nint)(int)policy);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void NavigationActionDidBecomeDownloadHook(
        IntPtr self,
        IntPtr selector,
        IntPtr webView,
        IntPtr navigationAction,
        IntPtr download)
    {
        try
        {
            if (_originalNavigationActionDidBecomeDownload != IntPtr.Zero)
            {
                CallOriginalImplementation(
                    _originalNavigationActionDidBecomeDownload,
                    self,
                    selector,
                    webView,
                    navigationAction,
                    download);
            }

            AdoptDownload(download);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void NavigationResponseDidBecomeDownloadHook(
        IntPtr self,
        IntPtr selector,
        IntPtr webView,
        IntPtr navigationResponse,
        IntPtr download)
    {
        try
        {
            if (_originalNavigationResponseDidBecomeDownload != IntPtr.Zero)
            {
                CallOriginalImplementation(
                    _originalNavigationResponseDidBecomeDownload,
                    self,
                    selector,
                    webView,
                    navigationResponse,
                    download);
            }

            AdoptDownload(download);
        }
        catch
        {
        }
    }

    // WebKit's context menu offers Download Linked File and Download Image, which start a download no
    // navigation led to. Without a delegate to take it over, WebKit drops it.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ContextMenuDidCreateDownloadHook(
        IntPtr self,
        IntPtr selector,
        IntPtr webView,
        IntPtr download)
    {
        try
        {
            if (_originalContextMenuDidCreateDownload != IntPtr.Zero)
            {
                CallOriginalImplementation(
                    _originalContextMenuDidCreateDownload,
                    self,
                    selector,
                    webView,
                    download);
            }

            if (_downloadListener?.IsRoutingDownloads(webView) == true)
            {
                AdoptDownload(download);
            }
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void DecideDownloadDestinationHook(
        IntPtr self,
        IntPtr selector,
        IntPtr download,
        IntPtr response,
        IntPtr suggestedFilename,
        IntPtr completionHandler)
    {
        // WebKit waits for the answer, which the listener gives after an await, so the handler is copied
        // off the stack it arrived on.
        _pendingDownloadDestinations[download] = _Block_copy(completionHandler);

        try
        {
            var listener = _downloadListener;
            if (listener is null)
            {
                ProvideDownloadDestination(download, null);
                return;
            }

            var suggestedFileName = ReadNSString(suggestedFilename);
            var sourceUrl = ReadDownloadSourceUrl(download);

            listener.OnDownloadDestinationRequested(download, suggestedFileName, sourceUrl);
        }
        catch
        {
            try
            {
                ProvideDownloadDestination(download, null);
            }
            catch
            {
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void DownloadDidFinishHook(IntPtr self, IntPtr selector, IntPtr download)
    {
        try
        {
            if (_adoptedDownloads.Contains(download))
            {
                _downloadListener?.OnDownloadFinished(download);
            }

            ReleaseDownload(download);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void DownloadDidFailHook(IntPtr self, IntPtr selector, IntPtr download, IntPtr error, IntPtr resumeData)
    {
        try
        {
            if (_adoptedDownloads.Contains(download))
            {
                var isCancelled = false;
                var description = string.Empty;

                if (error != IntPtr.Zero)
                {
                    isCancelled = SendMessageReturnNint(error, GetSelector("code")) == UrlErrorCancelled &&
                        ReadNSString(SendMessage(error, GetSelector("domain"))) == "NSURLErrorDomain";
                    description = ReadNSString(SendMessage(error, GetSelector("localizedDescription")));
                }

                _downloadListener?.OnDownloadFailed(download, isCancelled, description);
            }

            ReleaseDownload(download);
        }
        catch
        {
        }
    }
}
