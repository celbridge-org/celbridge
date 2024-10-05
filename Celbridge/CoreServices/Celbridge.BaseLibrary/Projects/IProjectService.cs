namespace Celbridge.Projects;

/// <summary>
/// Provides services for managing projects.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Returns the currently loaded project.
    /// </summary>
    IProject? LoadedProject { get; }

    /// <summary>
    /// Checks if a new project config is valid.
    /// </summary>
    Result ValidateNewProjectConfig(NewProjectConfig config);

    /// <summary>
    /// Create a new project file and database using the specified config information.
    /// </summary>
    Task<Result> CreateProjectAsync(NewProjectConfig config);

    /// <summary>
    /// Load the project file at the specified path.
    /// </summary>
    Result LoadProject(string projectFilePath);

    /// <summary>
    /// Unloads the currently loaded project.
    /// </summary>
    Task<Result> UnloadProjectAsync();
}
