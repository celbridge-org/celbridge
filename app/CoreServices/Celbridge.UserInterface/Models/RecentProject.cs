namespace Celbridge.UserInterface.Models;

/// <summary>
/// An entry in the recently used projects list
/// </summary>
public record RecentProject
{
    public string ProjectFilePath { get; private set; }
    public string ProjectFolderPath => Path.GetDirectoryName(ProjectFilePath)!;
    public string ProjectName => Path.GetFileNameWithoutExtension(Path.GetFileName(ProjectFilePath));

    public RecentProject(string projectFilePath)
    {
        Guard.IsNotNullOrEmpty(projectFilePath);
        Guard.IsTrue(File.Exists(projectFilePath));

        ProjectFilePath = projectFilePath;

        Guard.IsNotNullOrEmpty(ProjectFolderPath);
        Guard.IsNotNullOrEmpty(ProjectName);
    }
}
