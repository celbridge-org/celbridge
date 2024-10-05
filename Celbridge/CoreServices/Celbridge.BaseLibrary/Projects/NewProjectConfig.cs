namespace Celbridge.Projects;

/// <summary>
/// Configuration for a new project.
/// </summary>
public record NewProjectConfig(string ProjectName, string Folder, bool CreateSubfolder)
{
    private const string ProjectFileExtension = ".celbridge";

    public string ProjectFolder => Path.Combine(Folder, ProjectName);
    public string ProjectFilePath => Path.Combine(Folder, ProjectName, $"{ProjectName}{ProjectFileExtension}");
}
