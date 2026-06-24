using Windows.System;

namespace Celbridge.Resources.Helpers;

public class ResourceUtils
{
    public static async Task<Result> OpenApplication(string path)
    {
#if WINDOWS
        try
        {
            var fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();
            var infoResult = await fileSystem.GetInfoAsync(path);
            bool fileExists = infoResult.IsSuccess
                && infoResult.Value.Kind == StorageItemKind.File;
            if (fileExists)
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(path);
                bool launchResult = await Launcher.LaunchFileAsync(file);
                if (launchResult)
                {
                    return Result.Ok();
                }
            }
            else
            {
                var openResult = await OpenFileManager(path);
                if (openResult.IsSuccess)
                {
                    var failure = Result.Fail($"Failed to open file manager for path: {path}");
                    return Result.Ok();
                }
            }

            return Result.Fail($"Failed to open associated application for path: {path}");
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when opening the associated application: {path}")
                .WithException(ex);
        }
#else
        // The Uno Skia desktop head does not fully implement the WinRT Launcher members, so the
        // desktop head shells out to the OS instead (see LaunchAssociatedApplication / RevealInFileManager).
        var fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();
        var infoResult = await fileSystem.GetInfoAsync(path);
        bool isFile = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File;
        if (isFile)
        {
            return LaunchAssociatedApplication(path);
        }

        return await OpenFileManager(path);
#endif
    }

    public static async Task<Result> OpenFileManager(string path)
    {
#if WINDOWS
        try
        {
            var fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();
            var infoResult = await fileSystem.GetInfoAsync(path);
            bool fileExists = infoResult.IsSuccess
                && infoResult.Value.Kind == StorageItemKind.File;
            if (fileExists)
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(path);
                StorageFolder storageFolder = await file.GetParentAsync();
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(file);

                bool launchResult = await Launcher.LaunchFolderAsync(storageFolder, options);
                if (launchResult)
                {
                    return Result.Ok();
                }
            }
            else
            {
                string folder = string.Empty;
                bool folderExists = infoResult.IsSuccess
                    && infoResult.Value.Kind == StorageItemKind.Folder;
                if (folderExists)
                {
                    folder = path;
                }
                else
                {
                    // Try the parent folder
                    var parentFolder = Path.GetDirectoryName(path)!;
                    var parentInfoResult = await fileSystem.GetInfoAsync(parentFolder);
                    if (parentInfoResult.IsSuccess
                        && parentInfoResult.Value.Kind == StorageItemKind.Folder)
                    {
                        folder = parentFolder;
                    }
                }

                if (!string.IsNullOrEmpty(folder))
                {
                    StorageFolder storageFolder = await StorageFolder.GetFolderFromPathAsync(folder);
                    bool result = await Launcher.LaunchFolderAsync(storageFolder);
                    if (result)
                    {
                        return Result.Ok();
                    }
                }
            }

            return Result.Fail($"Failed to open file manager for path: {path}");
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when opening the path in the file manager: {path}")
                .WithException(ex);
        }
#else
        // FolderLauncherOptions.ItemsToSelect throws NotImplementedException on the Uno Skia
        // CoreWebView2, so the desktop head reveals items via the native shell instead.
        var fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();
        var infoResult = await fileSystem.GetInfoAsync(path);

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
            var parentInfoResult = await fileSystem.GetInfoAsync(parentFolder);
            if (parentInfoResult.IsSuccess
                && parentInfoResult.Value.Kind == StorageItemKind.Folder)
            {
                return RevealInFileManager(parentFolder, selectItem: false);
            }
        }

        return Result.Fail($"Failed to open file manager for path: {path}");
#endif
    }

#if !WINDOWS
    // The Uno Skia desktop head does not fully implement the WinRT Launcher / FolderLauncherOptions
    // members, so these shell out to the native file manager via the OS open command.
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

        return Result.Fail("Launching the associated application is not supported on this platform");
    }

    private static Result RevealInFileManager(string path, bool selectItem)
    {
        var fullPath = Path.GetFullPath(path);

        if (OperatingSystem.IsMacOS())
        {
            // 'open -R' reveals and selects the item in Finder; 'open' opens the folder itself.
            return selectItem
                ? StartProcess("open", "-R", fullPath)
                : StartProcess("open", fullPath);
        }

        if (OperatingSystem.IsLinux())
        {
            // xdg-open has no reveal-and-select, so open the containing folder when selecting a file.
            var target = selectItem
                ? (Path.GetDirectoryName(fullPath) ?? fullPath)
                : fullPath;
            return StartProcess("xdg-open", target);
        }

        return Result.Fail("Opening the file manager is not supported on this platform");
    }

    // Starts a process with raw (unquoted) arguments via ArgumentList, which quotes each argument
    // per-platform; this avoids the shell-quoting pitfalls of a single arguments string.
    private static Result StartProcess(string fileName, params string[] arguments)
    {
        try
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

            System.Diagnostics.Process.Start(startInfo);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to start the '{fileName}' process for path: {string.Join(' ', arguments)}")
                .WithException(ex);
        }
    }
#endif

    public static async Task<Result> OpenBrowser(string url)
    {
        try
        {
            string targetUrl = url.Trim();
            if (!string.IsNullOrWhiteSpace(targetUrl) &&
                !targetUrl.StartsWith("http") &&
                !targetUrl.StartsWith("file"))
            {
                targetUrl = $"https://{targetUrl}";
            }

            var uri = new Uri(targetUrl);
            await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to open URL: {url}")
                .WithException(ex);
        }

        return Result.Ok();
    }
}
