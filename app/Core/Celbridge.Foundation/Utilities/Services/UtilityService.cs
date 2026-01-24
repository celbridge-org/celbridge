using System.Reflection;
using Celbridge.Utilities;

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

    public Result<string> GetUniquePath(string path)
    {
        try
        {
            path = Path.GetFullPath(path);

            string directoryPath = Path.GetDirectoryName(path)!;
            string nameWithoutExtension = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string uniqueName = Path.GetFileName(path);
            int count = 1;

            while (File.Exists(Path.Combine(directoryPath, uniqueName)) || 
                Directory.Exists(Path.Combine(directoryPath, uniqueName)))
            {
                if (!string.IsNullOrEmpty(extension))
                {
                    // If it's a file, add the number before the extension
                    uniqueName = $"{nameWithoutExtension} ({count}){extension}";
                }
                else
                {
                    // If it's a folder (or file with no extension), just append the number
                    uniqueName = $"{nameWithoutExtension} ({count})";
                }
                count++;
            }

            var output = Path.Combine(directoryPath, uniqueName);

            return Result<string>.Ok(output);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when generating a unique path: {path}")
                .WithException(ex);
        }
    }

    public EnvironmentInfo GetEnvironmentInfo()
    {
#if WINDOWS
        var platform = "Windows";
        var packageVersion = Package.Current.Id.Version;
        var appVersion = $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}";
#else
        var platform = "SkiaGtk";
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var appVersion = version != null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "unknown";
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
            Result.Fail($"An exception occurred when deleting old files.")
                .WithException(ex);
        }

        return Result.Ok(); 
    }

    public (byte a, byte r, byte g, byte b) ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');

        byte a = 255; // Default alpha value
        byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
        byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);

        if (hex.Length == 8)
        {
            a = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
        }

        return (a, r, g, b);
    }

    public Result<string> LoadEmbeddedResource(Type type, string resourcePath)
    {
        // Load the component config JSON from an embedded resource
        var assembly = type.Assembly;
        var stream = assembly.GetManifestResourceStream(resourcePath);
        if (stream is null)
        {
            return Result<string>.Fail($"Embedded resource '{resourcePath}' not found in assembly '{assembly}'");
        }

        try
        {
            using (stream)
            using (StreamReader reader = new StreamReader(stream))
            {
                var data = reader.ReadToEnd();
                return Result<string>.Ok(data);
            }
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"An exception occurred when reading content of embedded resource: {resourcePath}")
                .WithException(ex);
        }
    }
}
