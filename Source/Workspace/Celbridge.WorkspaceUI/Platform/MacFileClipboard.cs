using Celbridge.DataTransfer;
using static Celbridge.Utilities.Platform.ObjectiveCRuntime;

namespace Celbridge.WorkspaceUI.Platform;

/// <summary>
/// File clipboard backed by the native macOS NSPasteboard. Writes file URLs (the modern file-copy
/// pasteboard representation) so copy/paste interoperates with Finder. NSPasteboard carries no
/// copy-versus-move semantic, so the transfer mode is remembered for the duration of this service's own
/// write. Content placed by another application (e.g. a Finder copy) reads back as a copy. Used on
/// macOS, where the WinRT storage-item clipboard does not round-trip on the Skia head.
/// </summary>
public sealed class MacFileClipboard : IFileClipboard
{
    // The uniform type identifier NSPasteboard uses for a file URL (NSPasteboardTypeFileURL).
    private const string FileUrlType = "public.file-url";

    private readonly object _writeLock = new();
    private nint _lastWriteChangeCount = -1;
    private DataTransferMode _lastWriteMode;

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

            SendMessageReturnNint(pasteboard, GetSelector("clearContents"));

            var urlArray = SendMessage(GetClass("NSMutableArray"), GetSelector("array"));
            var nsUrlClass = GetClass("NSURL");
            var fileUrlWithPathSelector = GetSelector("fileURLWithPath:");
            var addObjectSelector = GetSelector("addObject:");

            foreach (var file in files)
            {
                var nsPath = CreateNSString(file.Path);
                var url = SendMessage(nsUrlClass, fileUrlWithPathSelector, nsPath);
                if (url != IntPtr.Zero)
                {
                    SendMessage(urlArray, addObjectSelector, url);
                }
            }

            SendMessageReturnBool(pasteboard, GetSelector("writeObjects:"), urlArray);

            // Remember which write the mode belongs to, keyed by the pasteboard's change count.
            lock (_writeLock)
            {
                _lastWriteChangeCount = SendMessageReturnNint(pasteboard, GetSelector("changeCount"));
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

    public async Task<ClipboardFileContents?> GetFilesAsync()
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

        var nsUrlClasses = SendMessage(
            GetClass("NSArray"),
            GetSelector("arrayWithObject:"),
            GetClass("NSURL"));

        var urls = SendMessage(
            pasteboard,
            GetSelector("readObjectsForClasses:options:"),
            nsUrlClasses,
            IntPtr.Zero);
        if (urls == IntPtr.Zero)
        {
            return null;
        }

        var count = SendMessageReturnNuint(urls, GetSelector("count"));
        if (count == 0)
        {
            return null;
        }

        var objectAtIndexSelector = GetSelector("objectAtIndex:");
        var isFileUrlSelector = GetSelector("isFileURL");
        var pathSelector = GetSelector("path");

        var paths = new List<string>();
        for (nuint index = 0; index < count; index++)
        {
            var url = SendMessage(urls, objectAtIndexSelector, index);
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

        return new ClipboardFileContents(paths, ResolveTransferMode(pasteboard));
    }

    private bool PasteboardContainsFileUrls(IntPtr pasteboard)
    {
        var types = SendMessage(pasteboard, GetSelector("types"));
        if (types == IntPtr.Zero)
        {
            return false;
        }

        var fileUrlType = CreateNSString(FileUrlType);
        return SendMessageReturnBool(types, GetSelector("containsObject:"), fileUrlType);
    }

    private DataTransferMode ResolveTransferMode(IntPtr pasteboard)
    {
        var changeCount = SendMessageReturnNint(pasteboard, GetSelector("changeCount"));
        lock (_writeLock)
        {
            return changeCount == _lastWriteChangeCount
                ? _lastWriteMode
                : DataTransferMode.Copy;
        }
    }

    private static IntPtr GetGeneralPasteboard()
    {
        return SendMessage(GetClass("NSPasteboard"), GetSelector("generalPasteboard"));
    }
}
