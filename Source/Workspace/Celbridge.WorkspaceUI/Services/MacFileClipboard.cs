using System.Runtime.InteropServices;
using System.Text;
using Celbridge.DataTransfer;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// File clipboard backed by the native macOS NSPasteboard. Writes file URLs (the modern file-copy
/// pasteboard representation) so copy/paste interoperates with Finder. NSPasteboard carries no
/// copy-versus-move semantic, so the transfer mode is remembered for the duration of this service's own
/// write; content placed by another application (e.g. a Finder copy) reads back as a copy. Used on
/// macOS, where the WinRT storage-item clipboard does not round-trip on the Skia head.
/// </summary>
/// <remarks>
/// Objective-C messaging follows the same pattern as the other native interop helpers. NSPasteboard is
/// safe to touch from the app's main thread, which is where the copy/paste commands run. Registered
/// only on macOS, but compiles everywhere because the DllImports are metadata until called.
/// </remarks>
public sealed class MacFileClipboard : IFileClipboard
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    // The uniform type identifier NSPasteboard uses for a file URL (NSPasteboardTypeFileURL).
    private const string FileUrlType = "public.file-url";

    private readonly object _writeLock = new();
    private nint _lastWriteChangeCount = -1;
    private DataTransferMode _lastWriteMode;

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageArg(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageTwoArgs(IntPtr receiver, IntPtr selector, IntPtr firstArgument, IntPtr secondArgument);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageIndex(IntPtr receiver, IntPtr selector, nuint index);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nint SendMessageReturnNint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern nuint SendMessageReturnNuint(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendMessageReturnBool(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendMessageReturnBoolArg(IntPtr receiver, IntPtr selector, IntPtr argument);

    public async Task<Result> SetFilesAsync(IReadOnlyList<ClipboardFile> files, DataTransferMode transferMode)
    {
        await Task.CompletedTask;

        if (!OperatingSystem.IsMacOS())
        {
            return Result.Fail("The NSPasteboard file clipboard is only available on macOS");
        }

        try
        {
            var pasteboard = GetGeneralPasteboard();
            if (pasteboard == IntPtr.Zero)
            {
                return Result.Fail("Could not access the general NSPasteboard");
            }

            SendMessageReturnNint(pasteboard, sel_registerName("clearContents"));

            var urlArray = SendMessage(objc_getClass("NSMutableArray"), sel_registerName("array"));
            var nsUrlClass = objc_getClass("NSURL");
            var fileUrlWithPathSelector = sel_registerName("fileURLWithPath:");
            var addObjectSelector = sel_registerName("addObject:");

            foreach (var file in files)
            {
                var nsPath = CreateNSString(file.Path);
                var url = SendMessageArg(nsUrlClass, fileUrlWithPathSelector, nsPath);
                if (url != IntPtr.Zero)
                {
                    SendMessageArg(urlArray, addObjectSelector, url);
                }
            }

            SendMessageReturnBoolArg(pasteboard, sel_registerName("writeObjects:"), urlArray);

            // Remember which write the mode belongs to, keyed by the pasteboard's change count.
            lock (_writeLock)
            {
                _lastWriteChangeCount = SendMessageReturnNint(pasteboard, sel_registerName("changeCount"));
                _lastWriteMode = transferMode;
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to write files to the macOS clipboard")
                .WithException(ex);
        }
    }

    public DataTransferMode? GetFileTransferMode()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        var pasteboard = GetGeneralPasteboard();
        if (pasteboard == IntPtr.Zero
            || !PasteboardContainsFileUrls(pasteboard))
        {
            return null;
        }

        return ResolveTransferMode(pasteboard);
    }

    public async Task<ClipboardFiles?> GetFilesAsync()
    {
        await Task.CompletedTask;

        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        var pasteboard = GetGeneralPasteboard();
        if (pasteboard == IntPtr.Zero
            || !PasteboardContainsFileUrls(pasteboard))
        {
            return null;
        }

        var nsUrlClasses = SendMessageArg(
            objc_getClass("NSArray"),
            sel_registerName("arrayWithObject:"),
            objc_getClass("NSURL"));

        var urls = SendMessageTwoArgs(
            pasteboard,
            sel_registerName("readObjectsForClasses:options:"),
            nsUrlClasses,
            IntPtr.Zero);
        if (urls == IntPtr.Zero)
        {
            return null;
        }

        var count = SendMessageReturnNuint(urls, sel_registerName("count"));
        if (count == 0)
        {
            return null;
        }

        var objectAtIndexSelector = sel_registerName("objectAtIndex:");
        var isFileUrlSelector = sel_registerName("isFileURL");
        var pathSelector = sel_registerName("path");

        var paths = new List<string>();
        for (nuint index = 0; index < count; index++)
        {
            var url = SendMessageIndex(urls, objectAtIndexSelector, index);
            if (url == IntPtr.Zero
                || !SendMessageReturnBool(url, isFileUrlSelector))
            {
                continue;
            }

            var nsPath = SendMessage(url, pathSelector);
            var path = ReadNSString(nsPath);
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        if (paths.Count == 0)
        {
            return null;
        }

        return new ClipboardFiles(paths, ResolveTransferMode(pasteboard));
    }

    private bool PasteboardContainsFileUrls(IntPtr pasteboard)
    {
        var types = SendMessage(pasteboard, sel_registerName("types"));
        if (types == IntPtr.Zero)
        {
            return false;
        }

        var fileUrlType = CreateNSString(FileUrlType);
        return SendMessageReturnBoolArg(types, sel_registerName("containsObject:"), fileUrlType);
    }

    private DataTransferMode ResolveTransferMode(IntPtr pasteboard)
    {
        var changeCount = SendMessageReturnNint(pasteboard, sel_registerName("changeCount"));
        lock (_writeLock)
        {
            return changeCount == _lastWriteChangeCount
                ? _lastWriteMode
                : DataTransferMode.Copy;
        }
    }

    private static IntPtr GetGeneralPasteboard()
    {
        return SendMessage(objc_getClass("NSPasteboard"), sel_registerName("generalPasteboard"));
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
            return SendMessageArg(nsStringClass, selector, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string ReadNSString(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
        {
            return string.Empty;
        }

        var utf8Pointer = SendMessage(nsString, sel_registerName("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8Pointer) ?? string.Empty;
    }
}
