using Celbridge.Platform;

namespace Celbridge.Resources.Platform;

/// <summary>
/// File-manager launcher for the Uno Skia desktop heads. The WinRT Launcher and FolderLauncherOptions
/// members are not fully implemented there, so files and folders are opened by shelling out to the native
/// OS mechanism: open on macOS, xdg-open on Linux, and the shell or Explorer on Windows.
/// </summary>
public sealed class SkiaFileManagerLauncher : IFileManagerLauncher
{
    private readonly ILocalFileSystem _fileSystem;

    public SkiaFileManagerLauncher(ILocalFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<Result> OpenApplicationAsync(string path)
    {
        var infoResult = await _fileSystem.GetInfoAsync(path);
        bool isFile = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;
        if (isFile)
        {
            return LaunchAssociatedApplication(path);
        }

        return await OpenFileManagerAsync(path);
    }

    public async Task<Result> OpenFileManagerAsync(string path)
    {
        var infoResult = await _fileSystem.GetInfoAsync(path);

        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File)
        {
            return RevealInFileManager(path, selectItem: true);
        }

        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.Folder)
        {
            return RevealInFileManager(path, selectItem: false);
        }

        // The path does not exist, so fall back to revealing its parent folder if that exists.
        var parentFolder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parentFolder))
        {
            var parentInfoResult = await _fileSystem.GetInfoAsync(parentFolder);
            if (parentInfoResult.IsSuccess
                && parentInfoResult.Value.Kind == StorageItemKind.Folder)
            {
                return RevealInFileManager(parentFolder, selectItem: false);
            }
        }

        return Result.Fail($"Failed to open file manager for path: {path}");
    }

    private static Result LaunchAssociatedApplication(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            // 'open <path>' launches a file with its default application, or opens a folder in Finder.
            return StartProcess("open", path);
        }

        if (OperatingSystem.IsLinux())
        {
            return StartProcess("xdg-open", path);
        }

        if (OperatingSystem.IsWindows())
        {
            return StartShellExecute(path);
        }

        return Result.Fail("Launching the associated application is not supported on this platform");
    }

    private static Result RevealInFileManager(string path, bool selectItem)
    {
        var fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsMacOS())
        {
            if (selectItem)
            {
                // 'open -R' reveals and selects the item in Finder.
                return StartProcess("open", "-R", fullPath);
            }

            // 'open' opens the folder itself.
            return StartProcess("open", fullPath);
        }

        if (OperatingSystem.IsLinux())
        {
            // xdg-open has no reveal-and-select, so open the containing folder when selecting a file.
            var target = fullPath;
            if (selectItem)
            {
                target = Path.GetDirectoryName(fullPath) ?? fullPath;
            }

            return StartProcess("xdg-open", target);
        }

        if (OperatingSystem.IsWindows())
        {
            if (selectItem)
            {
                return StartExplorerReveal(fullPath);
            }

            return StartShellExecute(fullPath);
        }

        return Result.Fail("Opening the file manager is not supported on this platform");
    }

    // Starts a process with raw (unquoted) arguments via ArgumentList, which quotes each argument
    // per-platform. This avoids the shell-quoting pitfalls of a single arguments string.
    private static Result StartProcess(string fileName, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return StartProcess(startInfo);
    }

    // Hands the path to the Windows shell, which opens a file with its associated application and a folder in
    // Explorer, the same as double-clicking it.
    private static Result StartShellExecute(string path)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        };

        return StartProcess(startInfo);
    }

    // Explorer parses its own command line rather than following the argument-quoting rules every other
    // process uses, and its reveal switch is only understood as one argument with the path quoted inside it,
    // which is why this is the one launch here that passes a raw argument string.
    private static Result StartExplorerReveal(string fullPath)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{fullPath}\"",
            UseShellExecute = false
        };

        return StartProcess(startInfo);
    }

    private static Result StartProcess(System.Diagnostics.ProcessStartInfo startInfo)
    {
        try
        {
            System.Diagnostics.Process.Start(startInfo);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            var launchDetail = startInfo.Arguments;
            if (string.IsNullOrEmpty(launchDetail))
            {
                launchDetail = string.Join(' ', startInfo.ArgumentList);
            }

            return Result.Fail($"Failed to start the '{startInfo.FileName}' process: {launchDetail}")
                .WithException(ex);
        }
    }
}
