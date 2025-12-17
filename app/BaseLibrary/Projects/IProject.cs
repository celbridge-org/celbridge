namespace Celbridge.Projects;

/// <summary>
/// Represents the result of a project migration operation.
/// </summary>
public enum ProjectMigrationStatus
{
    /// <summary>
    /// Migration completed successfully or was not needed.
    /// </summary>
    Success,
    
    /// <summary>
    /// Project version is newer than application version - Python initialization disabled.
    /// </summary>
    IncompatibleAppVersion,
    
    /// <summary>
    /// Unable to resolve version compatibility - Python initialization disabled.
    /// </summary>
    InvalidAppVersion,
    
    /// <summary>
    /// Migration failed for other reasons.
    /// </summary>
    Failed
}

/// <summary>
/// Manages all project data for a Celbridge project.
/// </summary>
public interface IProject
{
    /// <summary>
    /// Returns the name of the project.
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// Returns the path to the current loaded project file.
    /// </summary>
    string ProjectFilePath { get; }

    /// <summary>
    /// Returns the path to the folder containing the current loaded project file.
    /// </summary>
    string ProjectFolderPath { get; }

    /// <summary>
    /// Returns the path to the folder containing the project database.
    /// </summary>
    string ProjectDataFolderPath { get; }

    /// <summary>
    /// Gets the project configuration.
    /// </summary>
    public IProjectConfigService ProjectConfig { get; }

    /// <summary>
    /// Gets the migration status from when the project was loaded.
    /// </summary>
    ProjectMigrationStatus MigrationStatus { get; }

    /// <summary>
    /// Gets the old version before migration (empty if no migration occurred).
    /// </summary>
    string MigrationOldVersion { get; }

    /// <summary>
    /// Gets the new version after migration (empty if no migration occurred).
    /// </summary>
    string MigrationNewVersion { get; }
}
