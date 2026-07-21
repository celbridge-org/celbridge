namespace Celbridge.Projects;

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
    public ProjectConfig Config { get; }

    /// <summary>
    /// True when the .celbridge file opened cleanly (migration succeeded and the config parsed). False
    /// when the project opened in a degraded state with an empty config, in which case the file is
    /// preserved for repair: the normalize-on-load write is suppressed and the file stays visible in
    /// the resource tree so the code editor can fix it.
    /// </summary>
    bool ConfigIsHealthy { get; }

    /// <summary>
    /// Gets the complete migration result from when the project was loaded.
    /// Contains the migration status, old/new versions, and operation result.
    /// </summary>
    MigrationResult MigrationResult { get; }
}
