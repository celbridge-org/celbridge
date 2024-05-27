namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Provides services for managing Celbridge project data.
/// </summary>
public interface IProjectDataService
{
    /// <summary>
    /// Create a new project data file in the specified folder.
    /// </summary>
    Result<IProjectData> CreateProjectData(string folder, string projectName, int version);

    /// <summary>
    /// Load the project at the specified path and open it in a workspace.
    /// </summary>
    Result OpenProjectWorkspace(string projectPath);
}
