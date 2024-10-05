namespace Celbridge.Projects;

/// <summary>
/// Configuration for a new project.
/// </summary>
public record NewProjectConfig(string ProjectName, string DestFolderPath, bool CreateSubfolder)
{
    private const string ProjectFileExtension = ".celbridge";

    public string ProjectFolder
    {
        get 
        {
            if (CreateSubfolder)
            {
                return Path.Combine(DestFolderPath, ProjectName);
            }

            return DestFolderPath;
        }
    }

    public string ProjectFilePath => Path.Combine(ProjectFolder, $"{ProjectName}{ProjectFileExtension}");
}
