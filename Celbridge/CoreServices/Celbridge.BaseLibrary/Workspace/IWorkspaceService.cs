using Celbridge.Console;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.GenerativeAI;
using Celbridge.Inspector;
using Celbridge.Scripting;
using Celbridge.Status;

namespace Celbridge.Workspace;

/// <summary>
/// Service for interacting with the sub-services of a loaded workspace.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Returns the Workspace Settings Service associated with the workspace.
    /// </summary>
    IWorkspaceSettingsService WorkspaceSettingsService { get; }

    /// <summary>
    /// Returns the Workspace Settings associated with the workspace.
    /// </summary>
    IWorkspaceSettings WorkspaceSettings { get; }

    /// <summary>
    /// Returns the Scripting Service associated with the workspace.
    /// </summary>
    IScriptingService ScriptingService { get; }

    /// <summary>
    /// Returns the Console Service associated with the workspace.
    /// </summary>
    IConsoleService ConsoleService { get; }

    /// <summary>
    /// Returns the Documents Service associated with the workspace.
    /// </summary>
    IDocumentsService DocumentsService { get; }

    /// <summary>
    /// Returns the Explorer Service associated with the workspace.
    /// </summary>
    IExplorerService ExplorerService { get; }

    /// <summary>
    /// Returns the Inspector Service associated with the workspace.
    /// </summary>
    IInspectorService InspectorService { get; }

    /// <summary>
    /// Returns the Status Service associated with the workspace.
    /// </summary>
    IStatusService StatusService { get; }

    /// <summary>
    /// Returns the Data Transfer Service associated with the workspace.
    /// </summary>
    IDataTransferService DataTransferService { get; }

    /// <summary>
    /// Returns the Entity Service associated with the workspace.
    /// </summary>
    IEntityService EntityService { get; }

    /// <summary>
    /// Returns the Generative AI Service associated with the workspace.
    /// </summary>
    IGenerativeAIService GenerativeAIService { get; }

    /// <summary>
    /// Toggle focus mode on/off by hiding and showing the workspace panels.
    /// </summary>
    void ToggleFocusMode();

    /// <summary>
    /// Set a flag to indicate that the workspace state is dirty and needs to be saved.
    /// </summary>
    void SetWorkspaceStateIsDirty();

    /// <summary>
    /// Save any pending workspace settings changes to disk.
    /// </summary>
    Task<Result> FlushPendingSaves(double deltaTime);
}
