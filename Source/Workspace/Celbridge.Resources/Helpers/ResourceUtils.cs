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
    // members, so these shell out to the native file manager. Windows-only for now; the macOS port
    // adds the open / xdg-open equivalents.
    private static Result LaunchAssociatedApplication(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Fail("Launching the associated application is not supported on this platform");
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when opening the associated application: {path}")
                .WithException(ex);
        }
    }

    private static Result RevealInFileManager(string path, bool selectItem)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Fail("Opening the file manager is not supported on this platform");
        }

        try
        {
            // explorer.exe /select,"<path>" opens the containing folder with the item highlighted;
            // without /select it opens the folder itself.
            var fullPath = Path.GetFullPath(path);
            var arguments = selectItem ? $"/select,\"{fullPath}\"" : $"\"{fullPath}\"";
            var startInfo = new System.Diagnostics.ProcessStartInfo("explorer.exe", arguments)
            {
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when opening the path in the file manager: {path}")
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
