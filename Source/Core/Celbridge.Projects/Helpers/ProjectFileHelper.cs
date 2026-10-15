namespace Celbridge.Projects;

/// <summary>
/// Recognizes project files, and finds the project files in a folder or on a command line. Also finds a project
/// file's folder, and a second project file beside a project file.
/// </summary>
public static class ProjectFileHelper
{
    /// <summary>
    /// Returns true if the path has the project file extension, in any case.
    /// </summary>
    public static bool IsProjectFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ProjectConstants.ProjectFileExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the full path of the folder that contains the project file, with no trailing separator. A folder
    /// at the root of a drive keeps its separator.
    /// </summary>
    public static string GetProjectFolderPath(string projectFilePath)
    {
        var fullPath = Path.GetFullPath(projectFilePath);
        var projectFolderPath = Path.GetDirectoryName(fullPath)!;

        return Path.TrimEndingDirectorySeparator(projectFolderPath);
    }

    /// <summary>
    /// Returns the full paths of the project files directly in the folder, ordered by path. A missing folder
    /// gives an empty list.
    /// </summary>
    public static async Task<Result<IReadOnlyList<string>>> FindProjectFilesAsync(string folderPath, ILocalFileSystem fileSystem)
    {
        var folderResult = await fileSystem.GetInfoAsync(folderPath);
        var folderExists = folderResult.IsSuccess
            && folderResult.Value.Kind == StorageItemKind.Folder;
        if (!folderExists)
        {
            IReadOnlyList<string> noProjectFiles = Array.Empty<string>();
            return noProjectFiles.OkResult();
        }

        // The pattern matches every file, since a file system that respects case would miss an extension in
        // another case.
        var enumerateResult = await fileSystem.EnumerateAsync(folderPath, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            return Result<IReadOnlyList<string>>.Fail($"Failed to list the project files in the folder: '{folderPath}'")
                .WithErrors(enumerateResult);
        }

        IReadOnlyList<string> projectFilePaths = enumerateResult.Value
            .Where(entry => entry.Kind == StorageItemKind.File)
            .Select(entry => entry.FullPath)
            .Where(IsProjectFile)
            .ToList();

        return projectFilePaths.OkResult();
    }

    /// <summary>
    /// Returns the first path in the list that names a project file other than the given one. Returns null if every
    /// path names the given project file. File names match by the file system's rules for case.
    /// </summary>
    public static string? FindOtherProjectFile(string projectFilePath, IEnumerable<string> projectFilePaths)
    {
        var projectFileName = Path.GetFileName(projectFilePath);

        return projectFilePaths.FirstOrDefault(path =>
            !string.Equals(Path.GetFileName(path), projectFileName, PathComparison.Comparison));
    }

    /// <summary>
    /// Returns the full path of the first command line argument that has the project file extension, or null if
    /// there is none. A relative path is resolved against the working folder.
    /// </summary>
    public static string? FindProjectFile(IEnumerable<string> arguments, string workingFolderPath)
    {
        var projectFileArgument = arguments.FirstOrDefault(IsProjectFile);
        if (string.IsNullOrEmpty(projectFileArgument))
        {
            return null;
        }

        return Path.GetFullPath(projectFileArgument, workingFolderPath);
    }
}
