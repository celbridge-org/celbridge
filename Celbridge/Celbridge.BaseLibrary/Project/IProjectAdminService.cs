namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Provides services for managing projects.
/// </summary>
public interface IProjectAdminService
{
    /// <summary>
    /// Returns the currently loaded project data, if any.
    /// </summary>
    public IProjectData? LoadedProjectData { get; }

    /// <summary>
    /// Create a new project in the specified folder.
    /// </summary>
    Task<Result<string>> CreateProjectAsync(string folder, string projectName);

    /// <summary>
    /// Load the project at the specified path and open it in a workspace for editing.
    /// </summary>
    Result OpenProjectWorkspace(string projectPath);

    /// <summary>
    /// Closes the currently opened project workspace
    /// </summary>
    Result CloseProjectWorkspace();
}
