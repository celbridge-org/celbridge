using Celbridge.BaseLibrary.Utilities;
using Windows.Storage;

namespace Celbridge.Messaging.Services;

public class UtilityService : IUtilityService
{
    public bool IsValidResourcePath(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return false;
        }

        // Backslashes are not permitted
        if (resourcePath.Contains("\\"))
        {
            return false;
        }

        // Resource paths must be specified relative to the project folder
        if (Path.IsPathRooted(resourcePath))
        {
            return false;
        }

        // Resource paths may not contain parent or same directory references
        if (resourcePath.Contains("..") || 
            resourcePath.Contains("./") || 
            resourcePath.Contains(".\\"))
        {
            return false;
        }

        // Resource paths may not start with a separator character
        if (resourcePath[0] == '/')
        {
            return false;
        }

        // Each segment in the resource path must be a valid filename
        var resourcePathSegments = resourcePath.Split('/');
        foreach (var segment in resourcePathSegments)
        {
            if (!IsValidResourcePathSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsValidResourcePathSegment(string resourcePathSegment)
    {
        if (string.IsNullOrWhiteSpace(resourcePathSegment))
        {
            return false;
        }

        // It's very difficult to robustly check for invalid characters in a way that works for every
        // platform. We do a basic check for invalid characters on the current platform.
        // Note that we're using GetInvalidFileNameChars() instead of GetInvalidPathChars() here.
        var invalidChars = Path.GetInvalidFileNameChars();

        foreach (var c in resourcePathSegment)
        {
            if (invalidChars.Contains(c))
            {
                return false;
            }
        }

        return true;
    }

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
}
