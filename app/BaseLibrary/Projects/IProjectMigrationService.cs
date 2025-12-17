namespace Celbridge.Projects;

/// <summary>
/// Result of a project migration operation, including status and any error details.
/// </summary>
public record MigrationResult(ProjectMigrationStatus Status, Result OperationResult)
{
    public static MigrationResult Success() => new(ProjectMigrationStatus.Success, Result.Ok());
    public static MigrationResult FromStatus(ProjectMigrationStatus status, Result operationResult) => new(status, operationResult);
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
