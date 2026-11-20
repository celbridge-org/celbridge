using Celbridge.Dependencies;

namespace Celbridge.Projects.Services;

/// <summary>
/// Data container for a Celbridge project.
/// </summary>
public record Project(
    string ProjectFilePath,
    string ProjectName,
    string ProjectFolderPath,
    string ProjectDataFolderPath,
    ProjectConfig Config,
    MigrationResult MigrationResult,
    bool ConfigIsHealthy,
    Result? ConfigLoadFailure,
    bool IsTrusted) : IProject
{
    public DependencyInstallFailure? DependencyInstallFailure { get; init; }

    // The project root stands for the project folder, and the project file sits in that folder. So the file's key is
    // the file's name under the project root. Combine accepts every character that a file name can contain, so the
    // call always succeeds.
    public ResourceKey ProjectFileResource => ResourceKey.Empty.Combine(Path.GetFileName(ProjectFilePath));

    public bool IsProjectFile(ResourceKey resource)
    {
        return resource == ProjectFileResource;
    }
}
