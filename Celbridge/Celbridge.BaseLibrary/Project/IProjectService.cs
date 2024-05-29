namespace Celbridge.BaseLibrary.Project;

public interface IProjectService
{
    /// <summary>
    /// Returns the loaded Project Data associated with the open workspace.
    /// </summary>
    IProjectData ProjectData { get; }

    /// <summary>
    /// Initializes the Project Service with the loaded Project Data.
    /// </summary>
    void Initialize(IProjectData projectData);
}
