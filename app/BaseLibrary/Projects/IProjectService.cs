namespace Celbridge.Projects;

/// <summary>
/// Provides services for managing projects.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Returns the current loaded project.
    /// </summary>
    IProject? CurrentProject { get; }

    /// <summary>
    /// Check if a new project config is valid.
    /// </summary>
    Result ValidateNewProjectConfig(NewProjectConfig config);

    /// <summary>
    /// Create a new project file and database using the specified config information.
    /// </summary>
    Task<Result> CreateProjectAsync(NewProjectConfig config);

    /// <summary>
    /// Load the project file at the specified path.
    /// The migrationResult should be obtained from ProjectLoader before calling this method.
    /// </summary>
    Task<Result<IProject>> LoadProjectAsync(string projectFilePath, MigrationResult migrationResult);

    /// <summary>
    /// Clears the CurrentProject reference without disposing it.
    /// The caller is responsible for disposing the project.
    /// </summary>
    void ClearCurrentProject();

    /// <summary>
    /// Returns the list of recent projects that still exist on disk,
    /// excluding the currently opened project.
    /// </summary>
    List<RecentProject> GetRecentProjects();

    /// <summary>
    /// Clears the list of recently opened projects.
    /// </summary>
    void ClearRecentProjects();
}
