using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Documents;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Status;

namespace Celbridge.BaseLibrary.Workspace;

/// <summary>
/// Service for interacting with the sub-services of a loaded workspace.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Returns the Console Service associated with the workspace.
    /// </summary>
    public IConsoleService ConsoleService { get; }

    /// <summary>
    /// Returns the Documents Service associated with the workspace.
    /// </summary>
    public IDocumentsService DocumentsService { get; }

    /// <summary>
    /// Returns the Project Service associated with the workspace.
    /// </summary>
    public IProjectService ProjectService { get; }

    /// <summary>
    /// Returns the Status Service associated with the workspace.
    /// </summary>
    public IStatusService StatusService { get; }
}
