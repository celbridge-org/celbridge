namespace Celbridge.Projects;

/// <summary>
/// Handles the complete workflow of loading a project.
/// </summary>
public interface IProjectLoader
{
    /// <summary>
    /// Loads a project with full migration support, user dialogs, and navigation.
    /// </summary>
    Task<Result> LoadProjectAsync(string projectFilePath);
}
