using Celbridge.UserInterface;

namespace Celbridge.Explorer;

/// <summary>
/// Provides functionality to support the explorer panel in the workspace UI.
/// </summary>
public interface IExplorerService
{
    /// <summary>
    /// Returns the Folder State Service that manages folder expanded state in the resource tree.
    /// </summary>
    IFolderStateService FolderStateService { get; }

    /// <summary>
    /// The currently selected resource in the Explorer Panel (the anchor item).
    /// </summary>
    ResourceKey SelectedResource { get; }

    /// <summary>
    /// All currently selected resources in the Explorer Panel.
    /// </summary>
    List<ResourceKey> SelectedResources { get; }

    /// <summary>
    /// Select a resource in the explorer panel.
    /// </summary>
    Task<Result> SelectResource(ResourceKey resource);

    /// <summary>
    /// Stores the selected resources in persistent storage.
    /// These resources will be selected at the start of the next editing session.
    /// </summary>
    Task StoreSelectedResources();

    /// <summary>
    /// Restores the state of the panel from the previous session.
    /// </summary>
    Task RestorePanelState();

    /// <summary>
    /// Open the specified resource in the system file manager.
    /// </summary>
    Task<Result> OpenFileManager(ResourceKey resource);

    /// <summary>
    /// Open the specified resource in the associated application.
    /// </summary>
    Task<Result> OpenApplication(ResourceKey resource);

    /// <summary>
    /// Open the specified URL in the system default browser.
    /// </summary>
    Task<Result> OpenBrowser(string uRL);

    /// <summary>
    /// Get an icon definition for the specified resource.
    /// </summary>
    FileIconDefinition GetIconForResource(ResourceKey resource);
}
