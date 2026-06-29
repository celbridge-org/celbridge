using System.Runtime.InteropServices;
using System.Text;

namespace Celbridge.Utilities.Platform;

/// <summary>
/// The shared Objective-C runtime marshaling used by the macOS native interop across the projects: class and
/// selector lookup, the objc_msgSend overloads keyed by argument and return shape, and NSString conversion.
/// The DllImports are metadata until called, so the type compiles on every head; callers gate on
/// OperatingSystem.IsMacOS() and invoke on the main (UI) thread, where AppKit and WebKit are safe.
/// </summary>
/// <remarks>
/// Only the signatures shared by more than one consumer live here. Interop that is specific to one file stays
/// with that file: struct-by-value returns (CGRect/NSRect), Objective-C block and class-pair construction, and
/// the Uno-internals reflection in MacOSWebViewInterop.
/// </remarks>
public static class ObjectiveCRuntime
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC, EntryPoint = "objc_getClass")]
    public static extern IntPtr GetClass(string name);

    [DllImport(LibObjC, EntryPoint = "sel_registerName")]
    public static extern IntPtr GetSelector(string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, IntPtr firstArgument, IntPtr secondArgument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, IntPtr firstArgument, IntPtr secondArgument, IntPtr thirdArgument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, nuint argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, double argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendMessageVoid(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendMessageVoid(IntPtr receiver, IntPtr selector, IntPtr firstArgument, IntPtr secondArgument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendMessageVoid(IntPtr receiver, IntPtr selector, nuint argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern nint SendMessageReturnNint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern nuint SendMessageReturnNuint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern double SendMessageReturnDouble(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendMessageReturnBool(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendMessageReturnBool(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(LibObjC, EntryPoint = "object_getClassName")]
    private static extern IntPtr object_getClassName(IntPtr target);

    /// <summary>
    /// Returns the Objective-C class name of a native object, for diagnostics when the runtime shape does not
    /// match expectations.
    /// </summary>
    public static string GetClassName(IntPtr target)
    {
        var classNamePointer = object_getClassName(target);
        return Marshal.PtrToStringAnsi(classNamePointer) ?? "(null)";
    }

    /// <summary>
    /// Creates an autoreleased NSString from a managed string via +[NSString stringWithUTF8String:], which
    /// copies the bytes, so the marshaling buffer is freed immediately after the call returns.
    /// </summary>
    public static IntPtr CreateNSString(string value)
    {
        var nsStringClass = GetClass("NSString");
        var selector = GetSelector("stringWithUTF8String:");

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

    /// <summary>
    /// Reads a managed string from an NSString via -[NSString UTF8String]. Returns the empty string for a
    /// null NSString.
    /// </summary>
    public static string ReadNSString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
        {
            return string.Empty;
        }

        var utf8Pointer = SendMessage(nsString, GetSelector("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8Pointer) ?? string.Empty;
    }
}
