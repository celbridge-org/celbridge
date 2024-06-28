namespace Celbridge.BaseLibrary.Project;

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
    /// Returns true if a path segment is valid (does not contain invalid characters).
    /// </summary>
    bool IsPathSegmentValid(string projectName);

    /// <summary>
    /// Create a new project file and database using the specified config information.
    /// </summary>
    Task<Result> CreateProjectDataAsync(NewProjectConfig config);

    /// <summary>
    /// Load the project file at the specified path.
    /// </summary>
    Result LoadProjectData(string projectPath);

    /// <summary>
    /// Unloads the currently loaded project.
    /// </summary>
    Result UnloadProjectData();
}
