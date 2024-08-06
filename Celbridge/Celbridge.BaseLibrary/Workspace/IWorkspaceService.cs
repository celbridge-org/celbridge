using Celbridge.Clipboard;
using Celbridge.Console;
using Celbridge.Documents;
using Celbridge.Resources;
using Celbridge.Status;

namespace Celbridge.Workspace;

/// <summary>
/// Service for interacting with the sub-services of a loaded workspace.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Returns the Workspace Data Service associated with the workspace.
    /// </summary>
    IWorkspaceDataService WorkspaceDataService { get; }

    /// <summary>
    /// Returns the Console Service associated with the workspace.
    /// </summary>
    IConsoleService ConsoleService { get; }

    /// <summary>
    /// Returns the Documents Service associated with the workspace.
    /// </summary>
    IDocumentsService DocumentsService { get; }

    /// <summary>
    /// Returns the Resource Service associated with the workspace.
    /// </summary>
    IResourceService ResourceService { get; }

    /// <summary>
    /// Returns the Status Service associated with the workspace.
    /// </summary>
    IStatusService StatusService { get; }

    /// <summary>
    /// Returns the Clipboard Service associated with the workspace.
    /// </summary>
    IClipboardService ClipboardService { get; }

    /// <summary>
    /// Save the workspace state to the database.
    /// </summary>
    Task<Result> SaveWorkspaceStateAsync();
}
