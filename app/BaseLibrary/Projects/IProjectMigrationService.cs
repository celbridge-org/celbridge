namespace Celbridge.Projects;

/// <summary>
/// Service for migrating older projects to the current version.
/// </summary>
public interface IProjectMigrationService
{
    /// <summary>
    /// Performs migration on a project if needed.
    /// </summary>
    Task<Result> PerformMigrationAsync(string projectFilePath);
}
