using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Adds an application:openURLs: method to Uno's macOS application delegate, which Uno does not implement.
/// Without it AppKit rejects the Finder open-document event for a double-clicked .celbridge file and shows a
/// "cannot open files" error instead of opening the project. macOS delivers the launch open event before
/// applicationDidFinishLaunching:, so the method must be added to the delegate CLASS before the run loop
/// starts (InstallOnDelegateClass, called from Program.Main), while the routing callback is wired up later
/// once dependency injection exists (SetCallback). Paths that arrive before the callback is set are buffered
/// and flushed to it. macOS-only.
/// </summary>
public static class MacOSFileActivation
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // Uno's application delegate class, whose only launch methods are applicationDidFinishLaunching: and the
    // terminate handlers, so it does not respond to the open-document event on its own.
    private const string DelegateClassName = "UNOApplicationDelegate";

    // Objective-C type encoding for -(void)application:(id)app openURLs:(id)urls: void return, then self,
    // _cmd, and the two object arguments.
    private const string OpenURLsTypeEncoding = "v@:@@";

    [DllImport(LibObjC, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr classHandle, IntPtr selector, IntPtr implementation, string types);

    private static readonly object _gate = new();
    private static readonly List<string> _bufferedFilePaths = new();
    private static Action<IReadOnlyList<string>>? _filesOpenedCallback;

    /// <summary>
    /// Adds application:openURLs: to Uno's application delegate class. Call before the app's run loop starts,
    /// because macOS delivers the launch open event before applicationDidFinishLaunching:. Returns false off
    /// macOS or when the delegate class is not yet registered.
    /// </summary>
    public static bool InstallOnDelegateClass()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var delegateClass = GetClass(DelegateClassName);
        if (delegateClass == IntPtr.Zero)
        {
            return false;
        }

        var selector = GetSelector("application:openURLs:");

        unsafe
        {
            var implementation = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&HandleOpenURLs;
            return class_addMethod(delegateClass, selector, implementation, OpenURLsTypeEncoding);
        }
    }

    /// <summary>
    /// Registers the routing callback and flushes any file paths that arrived before it was set. A cold-launch
    /// open event fires before dependency injection is ready, so its path is buffered until this is called.
    /// The callback runs on the main (UI) thread.
    /// </summary>
    public static void SetCallback(Action<IReadOnlyList<string>> filesOpenedCallback)
    {
        List<string> pendingFilePaths;
        lock (_gate)
        {
            _filesOpenedCallback = filesOpenedCallback;
            pendingFilePaths = new List<string>(_bufferedFilePaths);
            _bufferedFilePaths.Clear();
        }

        if (pendingFilePaths.Count > 0)
        {
            filesOpenedCallback(pendingFilePaths);
        }
    }

    // AppKit invokes this on the main thread with the NSApplication and an NSArray<NSURL*> of opened files.
    // A managed exception must never unwind back into AppKit.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void HandleOpenURLs(IntPtr self, IntPtr selector, IntPtr application, IntPtr urls)
    {
        try
        {
            if (urls == IntPtr.Zero)
            {
                return;
            }

            var filePaths = ReadFilePaths(urls);
            if (filePaths.Count == 0)
            {
                return;
            }

            Action<IReadOnlyList<string>>? callback;
            lock (_gate)
            {
                callback = _filesOpenedCallback;
                if (callback is null)
                {
                    // The callback is not wired up yet (cold launch, before DI). Buffer for SetCallback.
                    _bufferedFilePaths.AddRange(filePaths);
                    return;
                }
            }

            callback(filePaths);
        }
        catch
        {
            // The routing callback logs its own failures. This boundary guard only exists so a throw from the
            // Objective-C marshaling cannot cross back into AppKit, so there is nothing useful to log here.
        }
    }

    private static List<string> ReadFilePaths(IntPtr urls)
    {
        var count = SendMessageReturnNuint(urls, GetSelector("count"));
        var objectAtIndexSelector = GetSelector("objectAtIndex:");
        var pathSelector = GetSelector("path");

        var filePaths = new List<string>();
        for (nuint index = 0; index < count; index++)
        {
            var url = SendMessage(urls, objectAtIndexSelector, index);
            if (url == IntPtr.Zero)
            {
                continue;
            }

            var pathString = SendMessage(url, pathSelector);
            var path = ReadNSString(pathString);
            if (!string.IsNullOrEmpty(path))
            {
                filePaths.Add(path);
            }
        }

        return filePaths;
    }
}
