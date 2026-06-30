using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// A WKWebView snapshot encoded to PNG or JPEG bytes, with the captured pixel dimensions.
/// </summary>
public sealed record MacWebViewSnapshot(byte[] Bytes, int Width, int Height);

/// <summary>
/// Parameters for a WKWebView snapshot. The clip rectangle (CSS pixels, in the web view's coordinate space)
/// selects the region to capture. SnapshotWidth is the target output width in points (0 leaves the capture
/// at native resolution). Format is "png" or "jpeg" (Quality 1-100 applies to JPEG).
/// </summary>
public sealed record MacSnapshotRequest(
    double ClipX,
    double ClipY,
    double ClipWidth,
    double ClipHeight,
    double SnapshotWidth,
    string Format,
    int Quality);

/// <summary>
/// Objective-C interop for reaching the native WKWebView behind Uno's macOS Skia WebView2 control and
/// calling the WebKit methods the managed CoreWebView2 leaves unimplemented on macOS: serving a document
/// under a chosen origin, document-start script injection, surface capture, and view teardown. macOS-only.
/// Every method touches WebKit, so call on the main (UI) thread.
/// </summary>
public static class MacOSWebViewInterop
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    // BLOCK_IS_GLOBAL marks a block literal as a global (never copied or freed) block.
    private const int BlockIsGlobal = 1 << 28;

    // WKUserScriptInjectionTimeAtDocumentStart == 0.
    private const nint InjectionTimeAtDocumentStart = 0;

    // NSBitmapImageFileTypePNG == 4.
    private const nint BitmapImageFileTypePng = 4;

    // NSBitmapImageFileTypeJPEG == 3.
    private const nint BitmapImageFileTypeJpeg = 3;

    // initWithSource:injectionTime:forMainFrameOnly: takes an NSInteger and a BOOL after the source, a
    // combination the shared runtime does not carry, so this declaration stays local.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageInitUserScript(
        IntPtr receiver,
        IntPtr selector,
        IntPtr source,
        nint injectionTime,
        [MarshalAs(UnmanagedType.I1)] bool mainFrameOnly);

    // A CGRect is four doubles, a homogeneous float aggregate the ARM64 ABI passes in the floating-point
    // registers, so the struct marshals by value directly. The struct-by-value argument keeps this
    // declaration local rather than in the shared runtime.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendMessageVoidCGRect(IntPtr receiver, IntPtr selector, CGRect rect);

    [DllImport(LibSystem)]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    private static readonly IntPtr RtldDefault = new(-2);

    // Single snapshot in flight at a time: snapshots are serialized through the command queue and run on
    // the main thread, so a second concurrent snapshot would clobber this field.
    private static TaskCompletionSource<IntPtr>? _snapshotCompletion;
    private static IntPtr _snapshotBlock;

    /// <summary>
    /// Walks CoreWebView2._nativeWebView and its _webview field to recover the native WKWebView
    /// pointer. Returns false with the type name walked through in 'detail' when the shape does not
    /// match the validated Uno runtime (the signal that the version pin needs re-verification).
    /// </summary>
    public static bool TryGetNativeWebViewHandle(CoreWebView2 coreWebView, out IntPtr handle, out string detail)
    {
        handle = IntPtr.Zero;

        var nativeField = typeof(CoreWebView2).GetField("_nativeWebView", BindingFlags.NonPublic | BindingFlags.Instance);
        if (nativeField is null)
        {
            detail = "CoreWebView2._nativeWebView field not found";
            return false;
        }

        var nativeWebView = nativeField.GetValue(coreWebView);
        if (nativeWebView is null)
        {
            detail = "_nativeWebView is null";
            return false;
        }

        var nativeWebViewType = nativeWebView.GetType();
        detail = nativeWebViewType.FullName ?? nativeWebViewType.Name;

        // _webview is an Uno-internal field, so its name is coupled to the Uno runtime version: re-verify it
        // on an Uno bump. A mismatch returns false with the walked type name in 'detail' rather than crashing.
        var webViewField = FindFieldInHierarchy(nativeWebViewType, "_webview");
        if (webViewField is null)
        {
            detail += " (no _webview field)";
            return false;
        }

        var webViewValue = webViewField.GetValue(nativeWebView);
        if (webViewValue is null)
        {
            detail += " (_webview value null)";
            return false;
        }

        handle = (IntPtr)webViewValue;
        return handle != IntPtr.Zero;
    }

    /// <summary>
    /// Returns the Objective-C class name of a native object, for diagnostics when the runtime shape
    /// does not match expectations.
    /// </summary>
    public static string GetObjectiveCClassName(IntPtr nativeObject)
    {
        return GetClassName(nativeObject);
    }

    /// <summary>
    /// Calls -[WKWebView _close], WebKit's view teardown SPI (what Safari uses when closing a tab). It
    /// terminates the WebContent process and marks the view closed so WebKit will not relaunch a renderer for
    /// it. Unlike sending -release it does not free the object, so Uno's async dispose can still touch the view
    /// safely. This is the macOS reclaim path for the WKWebView the Skia head otherwise leaks (no native
    /// destroy exists). _close is private SPI, so it is guarded by respondsToSelector: to degrade to a no-op
    /// rather than crash if a future macOS drops it.
    /// </summary>
    public static void CloseNativeWebView(IntPtr webView)
    {
        if (webView == IntPtr.Zero)
        {
            return;
        }

        var closeSelector = GetSelector("_close");
        var respondsToSelector = GetSelector("respondsToSelector:");
        if (SendMessage(webView, respondsToSelector, closeSelector) == IntPtr.Zero)
        {
            return;
        }

        SendMessage(webView, closeSelector);
    }

    /// <summary>
    /// Sets the native WKWebView's customUserAgent. WKWebView's default User-Agent omits the Safari token that
    /// some sites (e.g. Gmail) require, so they flag it as an unsupported browser even though the engine is the
    /// current system WebKit. Must be set before navigation to take effect.
    /// </summary>
    public static void SetCustomUserAgent(IntPtr webView, string userAgent)
    {
        if (webView == IntPtr.Zero)
        {
            return;
        }

        var userAgentString = CreateNSString(userAgent);
        var customUserAgentSelector = GetSelector("setCustomUserAgent:");
        SendMessage(webView, customUserAgentSelector, userAgentString);
    }

    /// <summary>
    /// Reads the installed Safari's marketing version (CFBundleShortVersionString) so the WebView can report the
    /// real Safari "Version/" token in its User-Agent rather than a hardcoded value that would go stale. Returns
    /// an empty string if Safari cannot be read.
    /// </summary>
    public static string GetSafariVersion()
    {
        var safariPath = CreateNSString("/Applications/Safari.app");
        var bundle = SendMessage(GetClass("NSBundle"), GetSelector("bundleWithPath:"), safariPath);
        if (bundle == IntPtr.Zero)
        {
            return string.Empty;
        }

        var versionKey = CreateNSString("CFBundleShortVersionString");
        var versionString = SendMessage(bundle, GetSelector("objectForInfoDictionaryKey:"), versionKey);
        if (versionString == IntPtr.Zero)
        {
            return string.Empty;
        }

        var utf8 = SendMessage(versionString, GetSelector("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
    }

    /// <summary>
    /// Calls -[WKWebView loadHTMLString:baseURL:] directly so the loaded document reports the given
    /// base URL as its origin. This is the macOS replacement for SetVirtualHostNameToFolderMapping,
    /// which is a silent no-op on the Skia head: assets are served from a loopback server and the
    /// document is brought into a .celbridge origin here.
    /// </summary>
    public static void LoadHtmlString(IntPtr webView, string html, string baseUrl)
    {
        var htmlString = CreateNSString(html);
        var baseUrlString = CreateNSString(baseUrl);

        var nsUrlClass = GetClass("NSURL");
        var urlWithStringSelector = GetSelector("URLWithString:");
        var baseUrlObject = SendMessage(nsUrlClass, urlWithStringSelector, baseUrlString);

        var loadSelector = GetSelector("loadHTMLString:baseURL:");
        SendMessage(webView, loadSelector, htmlString, baseUrlObject);
    }

    /// <summary>
    /// Registers a WKUserScript on the native webview's userContentController so it runs at document
    /// start on subsequent navigations. This is the macOS replacement for the (unimplemented on Uno
    /// Skia) CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync.
    /// </summary>
    public static void AddUserScriptAtDocumentStart(IntPtr webView, string source)
    {
        var configuration = SendMessage(webView, GetSelector("configuration"));
        var userContentController = SendMessage(configuration, GetSelector("userContentController"));

        var userScriptClass = GetClass("WKUserScript");
        var allocatedUserScript = SendMessage(userScriptClass, GetSelector("alloc"));
        var sourceString = CreateNSString(source);

        var initSelector = GetSelector("initWithSource:injectionTime:forMainFrameOnly:");
        var userScript = SendMessageInitUserScript(
            allocatedUserScript,
            initSelector,
            sourceString,
            InjectionTimeAtDocumentStart,
            false);

        SendMessage(userContentController, GetSelector("addUserScript:"), userScript);
    }

    /// <summary>
    /// Captures the rendered WKWebView surface via -[WKWebView
    /// takeSnapshotWithConfiguration:completionHandler:], clipping to the request's rect and rendering at
    /// its SnapshotWidth, then encodes to the requested format ("png" or "jpeg", quality 1-100 for JPEG).
    /// Returns null if the snapshot does not complete within the timeout. This is the macOS replacement for
    /// the Chrome DevTools Protocol Page.captureScreenshot, which is unavailable on WKWebView. On a Retina
    /// display the captured pixels can be up to the backing-scale multiple of SnapshotWidth.
    /// </summary>
    public static async Task<MacWebViewSnapshot?> TakeSnapshotAsync(IntPtr webView, MacSnapshotRequest request)
    {
        _snapshotCompletion = new TaskCompletionSource<IntPtr>();
        var completionBlock = EnsureSnapshotBlock();

        var configuration = BuildSnapshotConfiguration(request);

        var selector = GetSelector("takeSnapshotWithConfiguration:completionHandler:");
        SendMessageVoid(webView, selector, configuration, completionBlock);

        var finishedTask = await Task.WhenAny(_snapshotCompletion.Task, Task.Delay(8000));

        if (configuration != IntPtr.Zero)
        {
            SendMessage(configuration, GetSelector("release"));
        }

        if (finishedTask != _snapshotCompletion.Task)
        {
            return null;
        }

        var image = await _snapshotCompletion.Task;
        if (image == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return ConvertNSImage(image, request.Format, request.Quality);
        }
        finally
        {
            SendMessage(image, GetSelector("release"));
        }
    }

    private static IntPtr BuildSnapshotConfiguration(MacSnapshotRequest request)
    {
        var configurationClass = GetClass("WKSnapshotConfiguration");
        if (configurationClass == IntPtr.Zero)
        {
            // No configuration class: fall back to a full-surface snapshot.
            return IntPtr.Zero;
        }

        var allocated = SendMessage(configurationClass, GetSelector("alloc"));
        var configuration = SendMessage(allocated, GetSelector("init"));

        // Leave rect at its default (the whole view) when no positive clip is supplied.
        if (request.ClipWidth > 0
            && request.ClipHeight > 0)
        {
            var rect = new CGRect
            {
                X = request.ClipX,
                Y = request.ClipY,
                Width = request.ClipWidth,
                Height = request.ClipHeight,
            };
            SendMessageVoidCGRect(configuration, GetSelector("setRect:"), rect);
        }

        if (request.SnapshotWidth > 0)
        {
            var snapshotWidthNumber = SendMessage(GetClass("NSNumber"), GetSelector("numberWithDouble:"), request.SnapshotWidth);
            SendMessage(configuration, GetSelector("setSnapshotWidth:"), snapshotWidthNumber);
        }

        return configuration;
    }

    private static MacWebViewSnapshot? ConvertNSImage(IntPtr nsImage, string format, int quality)
    {
        var tiffData = SendMessage(nsImage, GetSelector("TIFFRepresentation"));
        if (tiffData == IntPtr.Zero)
        {
            return null;
        }

        var bitmapImageRepClass = GetClass("NSBitmapImageRep");
        var bitmapRep = SendMessage(bitmapImageRepClass, GetSelector("imageRepWithData:"), tiffData);
        if (bitmapRep == IntPtr.Zero)
        {
            return null;
        }

        var width = (int)SendMessageReturnNint(bitmapRep, GetSelector("pixelsWide"));
        var height = (int)SendMessageReturnNint(bitmapRep, GetSelector("pixelsHigh"));

        nint fileType;
        IntPtr properties;
        if (string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase))
        {
            fileType = BitmapImageFileTypeJpeg;

            // NSImageCompressionFactor expects a 0-1 quality value.
            var compressionFactor = Math.Clamp(quality, 1, 100) / 100.0;
            var compressionNumber = SendMessage(GetClass("NSNumber"), GetSelector("numberWithDouble:"), compressionFactor);
            var compressionKey = CreateNSString("NSImageCompressionFactor");
            properties = SendMessage(GetClass("NSDictionary"), GetSelector("dictionaryWithObject:forKey:"), compressionNumber, compressionKey);
        }
        else
        {
            fileType = BitmapImageFileTypePng;
            properties = SendMessage(GetClass("NSDictionary"), GetSelector("dictionary"));
        }

        var imageData = SendMessage(
            bitmapRep,
            GetSelector("representationUsingType:properties:"),
            fileType,
            properties);
        if (imageData == IntPtr.Zero)
        {
            return null;
        }

        var length = (long)SendMessage(imageData, GetSelector("length"));
        var bytesPointer = SendMessage(imageData, GetSelector("bytes"));
        if (bytesPointer == IntPtr.Zero
            || length <= 0)
        {
            return null;
        }

        var managedBytes = new byte[length];
        Marshal.Copy(bytesPointer, managedBytes, 0, (int)length);
        return new MacWebViewSnapshot(managedBytes, width, height);
    }

    // takeSnapshotWithConfiguration:completionHandler: calls back through an Objective-C block. We
    // build a no-capture global block whose invoke pointer is a managed UnmanagedCallersOnly method,
    // reused across calls.
    private static unsafe IntPtr EnsureSnapshotBlock()
    {
        if (_snapshotBlock != IntPtr.Zero)
        {
            return _snapshotBlock;
        }

        var descriptor = new BlockDescriptor
        {
            Reserved = 0,
            Size = (nuint)Marshal.SizeOf<BlockLiteral>(),
        };
        var descriptorPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
        Marshal.StructureToPtr(descriptor, descriptorPointer, false);

        var blockIsa = dlsym(RtldDefault, "_NSConcreteGlobalBlock");
        var invoke = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&SnapshotCompletionCallback;

        var block = new BlockLiteral
        {
            Isa = blockIsa,
            Flags = BlockIsGlobal,
            Reserved = 0,
            Invoke = invoke,
            Descriptor = descriptorPointer,
        };
        var blockPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
        Marshal.StructureToPtr(block, blockPointer, false);

        _snapshotBlock = blockPointer;
        return blockPointer;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void SnapshotCompletionCallback(IntPtr block, IntPtr image, IntPtr error)
    {
        if (image != IntPtr.Zero
            && error == IntPtr.Zero)
        {
            // Retain so the NSImage survives past the completion handler's autorelease pool.
            SendMessage(image, GetSelector("retain"));
            _snapshotCompletion?.TrySetResult(image);
        }
        else
        {
            _snapshotCompletion?.TrySetResult(IntPtr.Zero);
        }
    }

    private static FieldInfo? FindFieldInHierarchy(Type? type, string fieldName)
    {
        while (type is not null)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field is not null)
            {
                return field;
            }

            type = type.BaseType;
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }
}
