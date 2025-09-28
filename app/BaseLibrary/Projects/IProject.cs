namespace Celbridge.Projects;

/// <summary>
/// Manages all project data for a Celbridge project.
/// </summary>
public interface IProject
{
    /// <summary>
    /// Returns the name of the project.
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// Returns the path to the current loaded project file.
    /// </summary>
    string ProjectFilePath { get; }

    /// <summary>
    /// Returns the path to the folder containing the current loaded project file.
    /// </summary>
    string ProjectFolderPath { get; }

    /// <summary>
    /// Returns the path to the folder containing the project database.
    /// </summary>
    string ProjectDataFolderPath { get; }

    /// <summary>
    /// Gets the project configuration.
    /// </summary>
    public IProjectConfigService ProjectConfig { get; }
}
