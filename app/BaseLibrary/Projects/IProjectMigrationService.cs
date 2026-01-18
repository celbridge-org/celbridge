namespace Celbridge.Projects;

/// <summary>
/// Represents the result of a project migration operation.
/// </summary>
public enum MigrationStatus
{
    /// <summary>
    /// Migration completed successfully or was not needed.
    /// </summary>
    Complete,

    /// <summary>
    /// Project requires upgrade.
    /// </summary>
    UpgradeRequired,

    /// <summary>
    /// The project config file failed to parse.
    /// </summary>
    InvalidConfig,

    /// <summary>
    /// Project celbridge-version is newer than the application version.
    /// </summary>
    IncompatibleVersion,

    /// <summary>
    /// Project celbridge-version is invalid or not specified.
    /// </summary>
    InvalidVersion,

    /// <summary>
    /// Migration failed for other reasons.
    /// </summary>
    Failed
}

/// <summary>
/// Result of a project migration operation, including status, version information, and any error details.
/// </summary>
public record MigrationResult(
    MigrationStatus Status, 
    Result OperationResult, 
    string OldVersion = "", 
    string NewVersion = "")
{
    public static MigrationResult Success() => new(MigrationStatus.Complete, Result.Ok());
    public static MigrationResult FromStatus(MigrationStatus status, Result operationResult) => new(status, operationResult);
    public static MigrationResult WithVersions(MigrationStatus status, Result operationResult, string oldVersion, string newVersion) => 
        new(status, operationResult, oldVersion, newVersion);
}

/// <summary>
/// Service for migrating older projects to the current version.
/// </summary>
public interface IProjectMigrationService
{
    /// <summary>
    /// Checks if a project requires migration and returns the migration status.
    /// If the project requires upgrade, returns MigrationStatus.UpgradeRequired without modifying files.
    /// Call PerformMigrationUpgradeAsync to actually perform the upgrade after user confirmation.
    /// </summary>
    Task<MigrationResult> CheckMigrationAsync(string projectFilePath);

    /// <summary>
    /// Performs the actual migration upgrade on a project.
    /// This should only be called after the user has confirmed they want to upgrade.
    /// </summary>
    Task<MigrationResult> PerformMigrationUpgradeAsync(string projectFilePath);
}
