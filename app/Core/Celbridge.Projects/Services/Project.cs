namespace Celbridge.Projects.Services;

/// <summary>
/// Data container for a Celbridge project.
/// </summary>
public record Project(
    string ProjectFilePath,
    string ProjectName,
    string ProjectFolderPath,
    string ProjectDataFolderPath,
    IProjectConfigService ProjectConfig,
    MigrationResult MigrationResult) : IProject;
