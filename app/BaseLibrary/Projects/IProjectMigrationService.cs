namespace Celbridge.Projects;

/// <summary>
/// Service for migrating older projects to the current version.
/// </summary>
public interface IProjectMigrationService
{
    /// <summary>
    /// Check if a project needs migration.
    /// </summary>
    Result<bool> CheckNeedsMigration(string projectFilePath);

    /// <summary>
    /// Migrate a project to the current version.
    /// </summary>
    Task<Result> MigrateProjectAsync(string projectFilePath);
}
