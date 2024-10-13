using Celbridge.Foundation;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json.Linq;
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

    public static async Task<Result> OpenApplication(string path)
    {
        await Task.CompletedTask;

#if WINDOWS
        try
        {
            if (File.Exists(path))
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
            return Result.Fail($"An exception occurred when opening the path in the file manager: {path}")
                .WithException(ex);
        }
#else
        return Result.Fail("File manager is only supported on Windows");
#endif
    }

    public static async Task<Result> OpenURL(string url)
    {
        try
        {
            var uri = new Uri(url);
            await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to open URL: {url}")
                .WithException(ex);
        }

        return Result.Ok();
    }

    public static Result<string> ExtractUrlFromWebFile(string webFilePath)
    {
        try
        {
            if (string.IsNullOrEmpty(webFilePath))
            {
                return Result<string>.Fail($"Failed to get path for file resource: {webFilePath}");
            }

            if (!File.Exists(webFilePath))
            {
                return Result<string>.Fail($"File does not exist: {webFilePath}");
            }

            if (Path.GetExtension(webFilePath) != ".web")
            {
                return Result<string>.Fail($"File does not have the .web extension: {webFilePath}");
            }

            var json = File.ReadAllText(webFilePath);
            var jsonObj = JObject.Parse(json);

            var urlToken = jsonObj["url"];
            if (urlToken is null)
            {
                return Result<string>.Fail($"Failed to find 'url' property in .web JSON data: {webFilePath}");
            }

            var url = urlToken.ToString();
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return Result<string>.Fail($"Url is not valid: {url}");
            }

            return Result<string>.Ok(url);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when extracting the url from a .web file")
                .WithException(ex); ;
        }
    }
}
