using Celbridge.Activities;
using Celbridge.Console;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Inspector;
using Celbridge.Python;
using Celbridge.Search;

namespace Celbridge.Workspace;

/// <summary>
/// Service for interacting with the sub-services of a loaded workspace.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Sets all workspace panel references.
    /// Called by WorkspacePage during initialization.
    /// </summary>
    void SetPanels(
        IActivityPanel activityPanel,
        IDocumentsPanel documentsPanel,
        IInspectorPanel inspectorPanel,
        IConsolePanel? consolePanel);

    /// <summary>
    /// Returns the Workspace Settings Service associated with the workspace.
    /// </summary>
    IWorkspaceSettingsService WorkspaceSettingsService { get; }

    /// <summary>
    /// Returns the Workspace Settings associated with the workspace.
    /// </summary>
    IWorkspaceSettings WorkspaceSettings { get; }

    /// <summary>
    /// Returns the Package Service associated with the workspace.
    /// </summary>
    IPackageService PackageService { get; }

    /// <summary>
    /// Returns the Resource Service associated with the workspace.
    /// </summary>
    IResourceService ResourceService { get; }

    /// <summary>
    /// Returns the gateway file-system layer for project resources.
    /// </summary>
    IFileStorage FileStorage { get; }

    /// <summary>
    /// Returns the soft-delete trash service: move-to-trash, restore, and purge
    /// operations used by the resource operation service for undoable deletes.
    /// </summary>
    ITrashService TrashService { get; }

    /// <summary>
    /// Returns the on-demand scanner over project text and sidecar files,
    /// used by the rename cascade, tag queries, and the project-health check.
    /// </summary>
    IResourceScanner ResourceScanner { get; }

    /// <summary>
    /// Returns the sidecar service: validation helpers plus read / mutate /
    /// write operations over .cel sidecar files via the file-system gateway.
    /// </summary>
    ISidecarService SidecarService { get; }

    /// <summary>
    /// Returns the Explorer Service associated with the workspace.
    /// </summary>
    IExplorerService ExplorerService { get; }

    /// <summary>
    /// Returns the Documents Service associated with the workspace.
    /// </summary>
    IDocumentsService DocumentsService { get; }

    /// <summary>
    /// Returns the Inspector Service associated with the workspace.
    /// </summary>
    IInspectorService InspectorService { get; }

    /// <summary>
    /// Returns the Console Service associated with the workspace.
    /// </summary>
    IConsoleService ConsoleService { get; }

    /// <summary>
    /// Gets the search service used to perform text search operations within the workspace.
    /// </summary>
    ISearchService SearchService { get; }

    /// <summary>
    /// Returns the Python Service associated with the workspace.
    /// </summary>
    IPythonService PythonService { get; }

    /// <summary>
    /// Returns the Entity Service associated with the workspace.
    /// </summary>
    IEntityService EntityService { get; }

    /// <summary>
    /// Returns the Activity Service associated with the workspace.
    /// </summary>
    IActivityService ActivityService { get; }

    /// <summary>
    /// Returns the Data Transfer Service associated with the workspace.
    /// </summary>
    IDataTransferService DataTransferService { get; }

    /// <summary>
    /// The most recently focussed workspace panel.
    /// </summary>
    WorkspacePanel ActivePanel { get; }

    /// <summary>
    /// Returns the Activity Panel view.
    /// </summary>
    IActivityPanel ActivityPanel { get; }

    /// <summary>
    /// Returns the Documents Panel view.
    /// </summary>
    IDocumentsPanel DocumentsPanel { get; }

    /// <summary>
    /// Returns the Inspector Panel view.
    /// </summary>
    IInspectorPanel InspectorPanel { get; }

    /// <summary>
    /// Returns the Console Panel view.
    /// Null if the console-panel feature is disabled.
    /// </summary>
    IConsolePanel? ConsolePanel { get; }

    /// <summary>
    /// Update the workspace state, for example by saving any pending workspace or document changes to disk.
    /// </summary>
    Task<Result> UpdateWorkspaceAsync(double deltaTime);
}
