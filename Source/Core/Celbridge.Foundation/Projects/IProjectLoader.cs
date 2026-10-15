namespace Celbridge.Projects;

/// <summary>
/// Handles the complete workflow of loading a project.
/// </summary>
public interface IProjectLoader
{
    /// <summary>
    /// Loads a project with full migration support, user dialogs, and navigation. The checks that can refuse the
    /// project run before the current project closes, and so does the upgrade prompt. A refused project or a
    /// declined upgrade therefore leaves the current project open. The origin decides what happens when another
    /// instance has the project open.
    /// </summary>
    Task<Result> LoadProjectAsync(string projectFilePath, ProjectLoadOrigin origin);
}
