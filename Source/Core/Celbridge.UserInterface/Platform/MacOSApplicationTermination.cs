using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Lets the application finish its work before macOS terminates it. Quit in the Dock and a logout do not
/// close the window. Without this class they end the process at once, before the project saves its edits
/// and its editors' state. With it, macOS waits for that work and then terminates the application.
/// macOS-only.
/// </summary>
// UNO-BUG: Uno's macOS application delegate always answers yes to applicationShouldTerminate:. It never
// raises AppWindow.Closing, and nothing can defer the termination.
public static class MacOSApplicationTermination
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    private const string DelegateClassName = "UNOApplicationDelegate";

    // Objective-C type encoding for -(NSApplicationTerminateReply)applicationShouldTerminate:(id)sender: an
    // NSUInteger return, then self, _cmd and the application.
    private const string ShouldTerminateTypeEncoding = "Q@:@";

    // NSTerminateNow and NSTerminateLater.
    private const nuint TerminateNow = 1;
    private const nuint TerminateLater = 2;

    [DllImport(LibObjC, EntryPoint = "class_replaceMethod")]
    private static extern IntPtr class_replaceMethod(IntPtr classHandle, IntPtr selector, IntPtr implementation, string types);

    // Used only on the main thread, where AppKit asks and where this class answers.
    private static Func<Task?>? _prepareToTerminate;

    /// <summary>
    /// Calls prepareToTerminate each time macOS is about to terminate the application. The callback returns
    /// null if the application can terminate now. Otherwise it returns the work to finish first, and the
    /// application terminates when that work completes. Returns false off macOS, or when Uno's application
    /// delegate class is not registered.
    /// </summary>
    public static bool Install(Func<Task?> prepareToTerminate)
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

        _prepareToTerminate = prepareToTerminate;

        var selector = GetSelector("applicationShouldTerminate:");

        unsafe
        {
            var implementation = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, nuint>)&HandleShouldTerminate;
            class_replaceMethod(delegateClass, selector, implementation, ShouldTerminateTypeEncoding);
        }

        return true;
    }

    // AppKit calls this on the main thread. A managed exception must never unwind into AppKit. If anything
    // here fails, the termination goes ahead, as it would without this hook.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static nuint HandleShouldTerminate(IntPtr self, IntPtr selector, IntPtr application)
    {
        try
        {
            var preparation = _prepareToTerminate?.Invoke();
            if (preparation is null)
            {
                return TerminateNow;
            }

            _ = TerminateAfterAsync(preparation, application);

            return TerminateLater;
        }
        catch
        {
            return TerminateNow;
        }
    }

    // AppKit waits for the answer in a modal run loop. That loop still drains the main thread's dispatch
    // queue, so the work finishes on the main thread, and this method replies from it.
    private static async Task TerminateAfterAsync(Task preparation, IntPtr application)
    {
        try
        {
            await preparation;
        }
        catch
        {
            // The work reports its own failures, and the application terminates either way.
        }

        // YES, so the termination goes ahead.
        SendMessageVoid(application, GetSelector("replyToApplicationShouldTerminate:"), (nuint)1);
    }
}
