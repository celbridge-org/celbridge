namespace Celbridge.Projects;

/// <summary>
/// Provides services for managing project databases.
/// </summary>
public interface IProjectDataService
{
    /// <summary>
    /// Returns the currently loaded project database.
    /// </summary>
    IProjectData? LoadedProjectData { get; }

    /// <summary>
    /// Checks if a new project config is valid.
    /// </summary>
    Result ValidateNewProjectConfig(NewProjectConfig config);

    /// <summary>
    /// Create a new project file and database using the specified config information.
    /// </summary>
    Task<Result> CreateProjectDataAsync(NewProjectConfig config);

    /// <summary>
    /// Load the project file at the specified path.
    /// </summary>
    Result LoadProjectData(string projectFilePath);

    /// <summary>
    /// Unloads the currently loaded project.
    /// </summary>
    Result UnloadProjectData();
}
