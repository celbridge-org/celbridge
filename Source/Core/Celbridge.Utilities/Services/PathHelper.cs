using Path = System.IO.Path;

namespace Celbridge.Utilities;

/// <summary>
/// Provides path-related utility methods.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Returns a path to a randomly named file in temporary storage.
    /// The path includes the specified folder name and extension.
    /// </summary>
    public static string GetTemporaryFilePath(string folderName, string extension)
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

    /// <summary>
    /// Returns a path which is guaranteed not to clash with any existing file or folder.
    /// </summary>
    public static Result<string> GetUniquePath(string path)
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
}
