using Windows.Foundation;

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
    /// Load the project file at the specified path with migration support.
    /// </summary>
    Task<Result<IProject>> LoadProjectAsync(string projectFilePath);

    /// <summary>
    /// Unload the current loaded project.
    /// </summary>
    Task<Result> UnloadProjectAsync();
}
