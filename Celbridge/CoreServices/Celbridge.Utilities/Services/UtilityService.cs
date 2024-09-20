using Celbridge.Utilities;
using System.Reflection;

using Path = System.IO.Path;

namespace Celbridge.Messaging.Services;

public class UtilityService : IUtilityService
{
    public string GetTemporaryFilePath(string folderName, string extension)
    {
        StorageFolder tempFolder = ApplicationData.Current.TemporaryFolder;
        var tempFolderPath = tempFolder.Path;

        var randomName = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());

        string archivePath = string.Empty;
        while (string.IsNullOrEmpty(archivePath) ||
               File.Exists(archivePath))
        {
            archivePath = Path.Combine(tempFolderPath, folderName, randomName + extension);
        }

        return archivePath;
    }

    public EnvironmentInfo GetEnvironmentInfo()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var appVersion = version != null 
            ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}" 
            : "unknown";

#if WINDOWS
        var platform = "Windows";
#else
        var platform = "SkiaGtk";
#endif

#if DEBUG
        var configuration = "Debug";
#else
        var configuration = "Release";
#endif

        var environmentInfo = new EnvironmentInfo(appVersion, platform, configuration);

        return environmentInfo;
    }

    public string GetTimestamp()
    {
        // Get the current date and time in the desired format
        return DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
    }

    public Result DeleteOldFiles(string folderPath, string filePrefix, int maxFilesToKeep)
    {
        try
        {
            // Get all files in the folder that start with the specified prefix
            var files = Directory.GetFiles(folderPath)
                .Where(file => Path.GetFileName(file).StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                .Select(file => new FileInfo(file))
                .OrderByDescending(file => file.CreationTime)
                .ToList();

            int keep = Math.Max(0, maxFilesToKeep - 1);

            // If the number of files is greater than the maximum allowed, delete the oldest files
            if (files.Count > maxFilesToKeep)
            {
                var filesToDelete = files.Skip(maxFilesToKeep);

                foreach (var file in filesToDelete)
                {
                    file.Delete();
                }
            }
        }
        catch (Exception ex)
        {
            Result.Fail($"An error occurred: {ex.Message}");
        }

        return Result.Ok(); 
    }

}
