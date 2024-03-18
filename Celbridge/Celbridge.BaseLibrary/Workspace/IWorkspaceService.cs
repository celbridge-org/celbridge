using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Status;

namespace Celbridge.BaseLibrary.Workspace;

/// <summary>
/// Service for interacting with the sub-services of a loaded workspace.
/// </summary>
public interface IWorkspaceService
{
    public IProjectService ProjectService { get; }

    public IStatusService StatusService { get; }

    public IConsoleService ConsoleService { get; }

    /// <summary>
    /// Register a workspace service (ProjectService, ConsoleService, StatusService, etc.) with the workspace service.
    /// </summary>
    void RegisterService(object workspaceService);
}
