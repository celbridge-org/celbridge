using Celbridge.BaseLibrary.Utilities;
using Windows.Storage;

namespace Celbridge.Messaging.Services;

public class UtilityService : IUtilityService
{
    public bool IsPathSegmentValid(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        // It's very difficult to robustly check for invalid characters in a way that works for every
        // platform. We do a basic check for invalid characters on the current platform.
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in segment)
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
