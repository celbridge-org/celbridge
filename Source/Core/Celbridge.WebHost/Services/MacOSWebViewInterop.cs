#if !WINDOWS
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Services;

/// <summary>
/// Objective-C runtime interop for reaching the native WKWebView behind Uno's macOS Skia WebView2
/// control and calling the WebKit methods it does not surface through managed code.
/// </summary>
/// <remarks>
/// On the macOS Skia head, CoreWebView2._nativeWebView is a MacOSNativeWebView whose _webview field
/// is a UNOWebView* (a WKWebView subclass). That pointer is messaged directly with objc_msgSend to
/// reach the three capabilities the managed CoreWebView2 leaves unimplemented on macOS: serving a
/// document under a chosen origin (loadHTMLString:baseURL:), document-start script injection
/// (WKUserContentController.addUserScript:), and capturing the rendered surface
/// (takeSnapshotWithConfiguration:).
///
/// The _webview reflection field is version-pinned to the Uno runtime validated by the macOS spike
/// (6.5.237, shipped by Uno.Sdk 6.5.36). It is the single place that needs re-verification on any Uno
/// bump, which is why all of the reflection lives here.
///
/// Every method touches WebKit, which is only safe on the macOS main (UI) thread. Callers must invoke
/// these on the UI thread; the methods do not marshal internally. The whole type is macOS-only, so
/// callers also gate on OperatingSystem.IsMacOS() before use.
/// </remarks>
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

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr object_getClassName(IntPtr nativeObject);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, IntPtr firstArgument, IntPtr secondArgument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendMessageVoidTwoPointers(IntPtr receiver, IntPtr selector, IntPtr firstArgument, IntPtr secondArgument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageEnumThenPointer(IntPtr receiver, IntPtr selector, nint firstArgument, IntPtr secondArgument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageInitUserScript(
        IntPtr receiver,
        IntPtr selector,
        IntPtr source,
        nint injectionTime,
        [MarshalAs(UnmanagedType.I1)] bool mainFrameOnly);

    [DllImport(LibSystem)]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    private static readonly IntPtr RtldDefault = new(-2);

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
        var classNamePointer = object_getClassName(nativeObject);
        return Marshal.PtrToStringAnsi(classNamePointer) ?? "(null)";
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

        var nsUrlClass = objc_getClass("NSURL");
        var urlWithStringSelector = sel_registerName("URLWithString:");
        var baseUrlObject = SendMessage(nsUrlClass, urlWithStringSelector, baseUrlString);

        var loadSelector = sel_registerName("loadHTMLString:baseURL:");
        SendMessage(webView, loadSelector, htmlString, baseUrlObject);
    }

    /// <summary>
    /// Registers a WKUserScript on the native webview's userContentController so it runs at document
    /// start on subsequent navigations. This is the macOS replacement for the (unimplemented on Uno
    /// Skia) CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync.
    /// </summary>
    public static void AddUserScriptAtDocumentStart(IntPtr webView, string source)
    {
        var configuration = SendMessage(webView, sel_registerName("configuration"));
        var userContentController = SendMessage(configuration, sel_registerName("userContentController"));

        var userScriptClass = objc_getClass("WKUserScript");
        var allocatedUserScript = SendMessage(userScriptClass, sel_registerName("alloc"));
        var sourceString = CreateNSString(source);

        var initSelector = sel_registerName("initWithSource:injectionTime:forMainFrameOnly:");
        var userScript = SendMessageInitUserScript(
            allocatedUserScript,
            initSelector,
            sourceString,
            InjectionTimeAtDocumentStart,
            false);

        SendMessage(userContentController, sel_registerName("addUserScript:"), userScript);
    }

    /// <summary>
    /// Captures the rendered WKWebView surface via -[WKWebView
    /// takeSnapshotWithConfiguration:completionHandler:] and returns it as PNG bytes, or null if the
    /// snapshot does not complete within the timeout. This is the macOS replacement for the Chrome
    /// DevTools Protocol Page.captureScreenshot, which is unavailable on WKWebView.
    /// </summary>
    public static async Task<byte[]?> TakeSnapshotPngAsync(IntPtr webView)
    {
        _snapshotCompletion = new TaskCompletionSource<IntPtr>();
        var completionBlock = EnsureSnapshotBlock();

        var selector = sel_registerName("takeSnapshotWithConfiguration:completionHandler:");
        SendMessageVoidTwoPointers(webView, selector, IntPtr.Zero, completionBlock);

        var finishedTask = await Task.WhenAny(_snapshotCompletion.Task, Task.Delay(8000));
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
            return ConvertNSImageToPng(image);
        }
        finally
        {
            SendMessage(image, sel_registerName("release"));
        }
    }

    private static byte[]? ConvertNSImageToPng(IntPtr nsImage)
    {
        var tiffData = SendMessage(nsImage, sel_registerName("TIFFRepresentation"));
        if (tiffData == IntPtr.Zero)
        {
            return null;
        }

        var bitmapImageRepClass = objc_getClass("NSBitmapImageRep");
        var bitmapRep = SendMessage(bitmapImageRepClass, sel_registerName("imageRepWithData:"), tiffData);
        if (bitmapRep == IntPtr.Zero)
        {
            return null;
        }

        var emptyProperties = SendMessage(objc_getClass("NSDictionary"), sel_registerName("dictionary"));

        var pngData = SendMessageEnumThenPointer(
            bitmapRep,
            sel_registerName("representationUsingType:properties:"),
            BitmapImageFileTypePng,
            emptyProperties);
        if (pngData == IntPtr.Zero)
        {
            return null;
        }

        var length = (long)SendMessage(pngData, sel_registerName("length"));
        var bytesPointer = SendMessage(pngData, sel_registerName("bytes"));
        if (bytesPointer == IntPtr.Zero
            || length <= 0)
        {
            return null;
        }

        var managedBytes = new byte[length];
        Marshal.Copy(bytesPointer, managedBytes, 0, (int)length);
        return managedBytes;
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
            SendMessage(image, sel_registerName("retain"));
            _snapshotCompletion?.TrySetResult(image);
        }
        else
        {
            _snapshotCompletion?.TrySetResult(IntPtr.Zero);
        }
    }

    private static IntPtr CreateNSString(string value)
    {
        var nsStringClass = objc_getClass("NSString");
        var selector = sel_registerName("stringWithUTF8String:");

        var utf8Bytes = Encoding.UTF8.GetBytes(value + '\0');
        var buffer = Marshal.AllocHGlobal(utf8Bytes.Length);
        try
        {
            Marshal.Copy(utf8Bytes, 0, buffer, utf8Bytes.Length);
            return SendMessage(nsStringClass, selector, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
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
#endif
