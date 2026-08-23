namespace Celbridge.Projects.Services;

/// <summary>
/// Data container for a Celbridge project.
/// </summary>
public record Project(
    string ProjectFilePath,
    string ProjectName,
    string ProjectFolderPath,
    ProjectConfig Config,
    MigrationResult MigrationResult,
    bool ConfigIsHealthy,
    Result? ConfigLoadFailure) : IProject
{
    public bool IsProjectFile(ResourceKey resource)
    {
        // Path rather than ToString, which carries the "project:" root prefix.
        return resource.Root == ResourceKey.DefaultRoot
            && string.Equals(resource.Path, Path.GetFileName(ProjectFilePath), StringComparison.Ordinal);
    }
}
