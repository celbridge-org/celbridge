namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Manages all project data for a Celbridge project.
/// </summary>
public interface IProjectData
{
    /// <summary>
    /// Returns the name of the project database.
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// Returns the path to the loaded project file.
    /// </summary>
    string ProjectFilePath { get; }

    /// <summary>
    /// Returns the path to the folder containing the loaded project.
    /// </summary>
    string ProjectFolder { get; }

    /// <summary>
    /// Returns the configuration data for the project.
    /// </summary>
    Task<IProjectConfig> GetConfigAsync();

    /// <summary>
    /// Updates the configuration data for the project.
    /// </summary>
    Task SetConfigAsync(IProjectConfig config);
}
