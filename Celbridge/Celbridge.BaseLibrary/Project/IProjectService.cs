namespace Celbridge.BaseLibrary.Project;

public interface IProjectService
{
    /// <summary>
    /// Returns the loaded Project Data associated with the open workspace.
    /// </summary>
    IProjectData LoadedProjectData { get; }
}
