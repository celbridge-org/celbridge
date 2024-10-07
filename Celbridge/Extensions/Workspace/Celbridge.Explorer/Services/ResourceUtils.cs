using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Celbridge.Explorer.Services;

public class ResourceUtils
{
    public static void CopyFolder(string sourceFolder, string destFolder)
    {
        DirectoryInfo dir = new DirectoryInfo(sourceFolder);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceFolder}");
        }

        DirectoryInfo[] dirs = dir.GetDirectories();
        if (!Directory.Exists(destFolder))
        {
            Directory.CreateDirectory(destFolder);
        }

        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            string tempPath = Path.Combine(destFolder, file.Name);
            file.CopyTo(tempPath);
        }

        foreach (DirectoryInfo subdir in dirs)
        {
            string tempPath = Path.Combine(destFolder, subdir.Name);
            CopyFolder(subdir.FullName, tempPath);
        }
    }

    public static async Task<Result> OpenFileManager(string path)
    {
        await Task.CompletedTask;

#if WINDOWS
        try
        {
            if (File.Exists(path))
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
                if (Directory.Exists(path))
                {
                    folder = path;
                }
                else
                {
                    // Try the parent folder
                    var parentFolder = Path.GetDirectoryName(path)!;
                    if (Directory.Exists(parentFolder))
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
            return Result.Fail(ex, $"An exception occurred when opening the path in the file manager: {path}");
        }
#else
        return Result.Fail("File manager is only supported on Windows");
#endif
    }
}
