using Path = System.IO.Path;

namespace Celbridge.Projects;

/// <summary>
/// An entry in the recently used projects list.
/// </summary>
public record RecentProject
{
    /// <summary>
    /// Full path to the project file.
    /// </summary>
    public string ProjectFilePath { get; }

    /// <summary>
    /// Path to the folder containing the project file.
    /// </summary>
    public string ProjectFolderPath { get; }

    /// <summary>
    /// Project name (filename without extension).
    /// </summary>
    public string ProjectName { get; }

    public RecentProject(string projectFilePath)
    {
        Guard.IsNotNullOrEmpty(projectFilePath);

        ProjectFilePath = projectFilePath;
        ProjectFolderPath = Path.GetDirectoryName(projectFilePath) ?? string.Empty;
        ProjectName = Path.GetFileNameWithoutExtension(projectFilePath);

        Guard.IsNotNullOrEmpty(ProjectFolderPath);
        Guard.IsNotNullOrEmpty(ProjectName);
    }
}
