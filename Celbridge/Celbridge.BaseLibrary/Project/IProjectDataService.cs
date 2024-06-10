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
    /// Create a new project in the specified folder.
    /// </summary>
    Task<Result<string>> CreateProjectDataAsync(string folder, string projectName);

    /// <summary>
    /// Load the project data at the specified path.
    /// </summary>
    Result OpenProjectData(string projectPath);

    /// <summary>
    /// Closes the currently opened project data.
    /// </summary>
    Result CloseProjectData();
}
