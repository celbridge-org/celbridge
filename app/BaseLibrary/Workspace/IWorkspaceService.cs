using Celbridge.Activities;
using Celbridge.Console;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.GenerativeAI;
using Celbridge.Inspector;
using Celbridge.Python;
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
    /// Returns the Python Service associated with the workspace.
    /// </summary>
    IPythonService PythonService { get; }

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
    /// Returns the Activity Service associated with the workspace.
    /// </summary>
    IActivityService ActivityService { get; }

    /// <summary>
    /// The most recently focussed workspace panel.
    /// </summary>
    WorkspacePanel ActivePanel { get; }

    /// <summary>
    /// Toggle all workspace panels. If any panel is collapsed, expand all panels. Otherwise, collapse all panels.
    /// </summary>
    void ToggleAllPanels();

    /// <summary>
    /// Set a flag to indicate that the workspace state is dirty and needs to be saved.
    /// </summary>
    void SetWorkspaceStateIsDirty();

    /// <summary>
    /// Update the workspace state, for example by saving any pending workspace or document changes to disk.
    /// </summary>
    Task<Result> UpdateWorkspaceAsync(double deltaTime);

    /// <summary>
    /// Clear all currently stored Context Area Uses.
    /// </summary>
    void ClearContextAreaUses();

    /// <summary>
    /// Set the current Context Area Usage to the Usage given
    /// </summary>
    void SetCurrentContextAreaUsage(ContextAreaUse contextAreaUse);

    /// <summary>
    /// Adds the given UIElement to the available map of Context Area Usages, keyed by the given ContextAreaUse.
    /// </summary>
    void AddContextAreaUse(ContextAreaUse contextAreaUse, UIElement element);
}
