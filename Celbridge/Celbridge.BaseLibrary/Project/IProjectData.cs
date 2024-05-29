namespace Celbridge.BaseLibrary.Project;

/// <summary>
/// Manages all project data for a Celbridge project.
/// </summary>
public interface IProjectData
{
    /// <summary>
    /// Returns the configuration data for the project.
    /// </summary>
    public IProjectConfig Config { get; }
}
