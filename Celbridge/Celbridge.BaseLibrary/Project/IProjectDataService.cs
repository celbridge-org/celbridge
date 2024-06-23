using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Provides services for managing project data.
/// </summary>
public interface IProjectDataService
{
    /// <summary>
    /// Returns the currently loaded project data.
    /// </summary>
    IProjectData? LoadedProjectData { get; }

    /// <summary>
    /// Returns the currently opened workspace data.
    /// </summary>
    IWorkspaceData? WorkspaceData { get; }

    /// <summary>
    /// Checks if a new project config is valid.
    /// </summary>
    Result ValidateNewProjectConfig(NewProjectConfig config);

    /// <summary>
    /// Checks if a project name is valid.
    /// </summary>
    Result ValidateProjectName(string projectName);

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
