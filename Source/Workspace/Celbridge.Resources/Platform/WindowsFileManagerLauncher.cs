#if WINDOWS
using Celbridge.Platform;
using Windows.Storage;
using Windows.System;

namespace Celbridge.Resources.Platform;

/// <summary>
/// File-manager launcher for the packaged Windows head. Uses the WinRT Launcher and StorageFile APIs that
/// are fully implemented on the WinAppSDK head.
/// </summary>
public sealed class WindowsFileManagerLauncher : IFileManagerLauncher
{
    private readonly ILocalFileSystem _fileSystem;

    public WindowsFileManagerLauncher(ILocalFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<Result> OpenApplicationAsync(string path)
    {
        try
        {
            var infoResult = await _fileSystem.GetInfoAsync(path);
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
                var openResult = await OpenFileManagerAsync(path);
                if (openResult.IsSuccess)
                {
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
    }

    public async Task<Result> OpenFileManagerAsync(string path)
    {
        try
        {
            var infoResult = await _fileSystem.GetInfoAsync(path);
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
                    var parentFolder = Path.GetDirectoryName(path)!;
                    var parentInfoResult = await _fileSystem.GetInfoAsync(parentFolder);
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
    }
}
#endif
