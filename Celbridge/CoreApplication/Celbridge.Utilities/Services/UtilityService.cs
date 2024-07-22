using Celbridge.Core;
using Celbridge.Utilities;
using System.Reflection;
using Windows.Storage;

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

    public string GenerateLogName(string logType)
    {
        // Get the current date and time in the desired format
        string currentDate = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        // Get the application version
        string version = GetAppVersion();

        // Todo: Select debug or release from the build configuration
        string environment = "Debug";

        // Construct the log file name
        string logName = $"{logType}_{environment}_v{version}_{currentDate}";

        return logName;
    }

    public string GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}" : "unknown";
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
