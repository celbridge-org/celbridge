using Windows.System;

namespace Celbridge.Resources.Helpers;

public class ResourceUtils
{
    public static async Task<Result> OpenApplication(string path)
    {
#if WINDOWS
        try
        {
            var fileSystem = ServiceLocator.AcquireService<IFileSystem>();
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
        return Result.Fail("Launching associated application is only supported on Windows");
#endif
    }

    public static async Task<Result> OpenFileManager(string path)
    {
#if WINDOWS
        try
        {
            var fileSystem = ServiceLocator.AcquireService<IFileSystem>();
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
        return Result.Fail("File manager is only supported on Windows");
#endif
    }

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
