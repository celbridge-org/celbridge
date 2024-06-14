namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Provides services for managing project data.
/// </summary>
public interface IProjectDataService
{
    /// <summary>
    /// Returns the currently loaded project data.
    /// </summary>
    public IProjectData? LoadedProjectData { get; }

    /// <summary>
    /// Create a new project using the specified config information.
    /// </summary>
    Task<Result> CreateProjectDataAsync(NewProjectConfig config);

    /// <summary>
    /// Load the project data at the specified path.
    /// </summary>
    Result LoadProjectData(string projectPath);

    /// <summary>
    /// Unloads the currently loaded project data.
    /// </summary>
    Result UnloadProjectData();
}
