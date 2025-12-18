namespace Celbridge.Projects;

/// <summary>
/// Result of a project migration operation, including status, version information, and any error details.
/// </summary>
public record MigrationResult(
    ProjectMigrationStatus Status, 
    Result OperationResult, 
    string OldVersion = "", 
    string NewVersion = "")
{
    public static MigrationResult Success() => new(ProjectMigrationStatus.Complete, Result.Ok());
    public static MigrationResult FromStatus(ProjectMigrationStatus status, Result operationResult) => new(status, operationResult);
    public static MigrationResult WithVersions(ProjectMigrationStatus status, Result operationResult, string oldVersion, string newVersion) => 
        new(status, operationResult, oldVersion, newVersion);
}

/// <summary>
/// Service for migrating older projects to the current version.
/// </summary>
public interface IProjectMigrationService
{
    /// <summary>
    /// Performs migration on a project if needed.
    /// Returns a MigrationResult containing the migration status and operation result.
    /// </summary>
    Task<MigrationResult> PerformMigrationAsync(string projectFilePath);
}
