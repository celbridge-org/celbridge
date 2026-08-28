using Celbridge.Console;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Search;

namespace Celbridge.Workspace;

/// <summary>
/// Service for interacting with the sub-services of a loaded workspace.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Sets all workspace panel references.
    /// </summary>
    void SetPanels(
        IUtilityPanel utilityPanel,
        IDocumentsPanel documentsPanel);

    /// <summary>
    /// Returns the workspace settings service for the current project, which owns
    /// the property bag and the Workspace-scope settings store.
    /// </summary>
    IWorkspaceSettingsService WorkspaceSettings { get; }

    /// <summary>
    /// Returns the bindable Workspace-scope settings facade for the current project.
    /// </summary>
    IBindableWorkspaceSettings BindableWorkspaceSettings { get; }

    /// <summary>
    /// Returns the Package Service associated with the workspace.
    /// </summary>
    IPackageService PackageService { get; }

    /// <summary>
    /// Returns the Resource Service associated with the workspace.
    /// </summary>
    IResourceService ResourceService { get; }

    /// <summary>
    /// Returns the Explorer Service associated with the workspace.
    /// </summary>
    IExplorerService ExplorerService { get; }

    /// <summary>
    /// Returns the Documents Service associated with the workspace.
    /// </summary>
    IDocumentsService DocumentsService { get; }

    /// <summary>
    /// Returns the Utility Service associated with the workspace.
    /// </summary>
    IUtilityService UtilityService { get; }

    /// <summary>
    /// Returns the Console Service associated with the workspace.
    /// </summary>
    IConsoleService ConsoleService { get; }

    /// <summary>
    /// Gets the search service used to perform text search operations within the workspace.
    /// </summary>
    ISearchService SearchService { get; }

    /// <summary>
    /// Returns the Data Transfer Service associated with the workspace.
    /// </summary>
    IDataTransferService DataTransferService { get; }

    /// <summary>
    /// The most recently focussed workspace panel.
    /// </summary>
    FocusPanelId ActivePanel { get; }

    /// <summary>
    /// Returns the Utility Panel view.
    /// </summary>
    IUtilityPanel UtilityPanel { get; }

    /// <summary>
    /// Returns the Documents Panel view.
    /// </summary>
    IDocumentsPanel DocumentsPanel { get; }

    /// <summary>
    /// Update the workspace state, for example by saving any pending workspace or document changes to disk.
    /// </summary>
    Task<Result> UpdateWorkspaceAsync(double deltaTime);
}
