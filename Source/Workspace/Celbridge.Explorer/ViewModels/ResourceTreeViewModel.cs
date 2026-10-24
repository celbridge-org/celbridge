using System.Collections.ObjectModel;
using Celbridge.DataTransfer;
using Celbridge.Explorer.Models;
using Celbridge.Explorer.Services;
using Celbridge.Logging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Explorer.ViewModels;

/// <summary>
/// View model for the control that displays the resource tree in the Explorer panel.
/// </summary>
public partial class ResourceTreeViewModel : ObservableObject
{
    private readonly ILogger<ResourceTreeViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IDataTransferService _dataTransferService;
    private readonly ResourceTreeModel _model;

    /// <summary>
    /// The flat list of items to display in the ListView.
    /// Replacing the entire collection ensures a single UI update with no flicker.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ResourceViewItem> _treeItems = [];

    /// <summary>
    /// The selected item in the tree (the anchor). Required for ListView two-way binding
    /// and serves as the keyboard focus/anchor item in multi-select scenarios.
    /// </summary>
    [ObservableProperty]
    private ResourceViewItem? _selectedItem;

    /// <summary>
    /// All selected items for multi-select operations. Updated from ListView.SelectedItems
    /// which is read-only and cannot be bound directly.
    /// </summary>
    public List<ResourceViewItem> SelectedItems { get; private set; } = [];

    /// <summary>
    /// Raised when the view should update the selected resources.
    /// </summary>
    public event Action<List<ResourceKey>>? SelectionRequested;

    /// <summary>
    /// Raised before the tree is rebuilt. View should save scroll position.
    /// </summary>
    public event Action? PreBuildResourceTree;

    /// <summary>
    /// Raised after the tree is rebuilt. View should restore scroll position.
    /// </summary>
    public event Action? PostBuildResourceTree;

    public ResourceTreeViewModel(
        ILogger<ResourceTreeViewModel> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ResourceTreeModel model)
    {
        _logger = logger;
        _messengerService = messengerService;
        _dataTransferService = workspaceWrapper.WorkspaceService.DataTransferService;
        _model = model;
    }

    //
    // Lifecycle
    //

    public async Task OnLoadedAsync()
    {
        _messengerService.Register<ClipboardContentChangedMessage>(this, OnClipboardContentChangedMessage);
        _model.RowsChanged += OnModelRowsChanged;

        await RefreshAsync();
    }

    public void OnUnloaded()
    {
        _messengerService.UnregisterAll(this);
        _model.RowsChanged -= OnModelRowsChanged;
        _model.Stop();
    }

    private void OnClipboardContentChangedMessage(object recipient, ClipboardContentChangedMessage message)
    {
        var contentDescription = _dataTransferService.GetClipboardContentDescription();

        if (contentDescription.ContentType == ClipboardContentType.Resource)
        {
            // Todo: Clear previously faded resources
        }

        if (contentDescription.ContentOperation == ClipboardContentOperation.Move)
        {
            // Todo: Fade cut resources in the tree view
        }
    }

    private void OnModelRowsChanged()
    {
        ShowRows(selectedResources: null);
    }

    //
    // Tree population
    //

    /// <summary>
    /// Returns true if the tree draws the resources it hides by default.
    /// </summary>
    public bool ShowHiddenFiles => _model.ShowHiddenFiles;

    /// <summary>
    /// Shows or stops showing the hidden resources. Each collapsed subfolder is checked again, each folder that
    /// comes into view is listed, and the rows are rebuilt.
    /// </summary>
    public async Task ToggleShowHiddenFilesAsync()
    {
        await _model.SetShowHiddenFilesAsync(!_model.ShowHiddenFiles);
        OnPropertyChanged(nameof(ShowHiddenFiles));

        ShowRows(selectedResources: null);
    }

    /// <summary>
    /// Lists each folder the tree shows that the model has no listing of yet, and updates the rows.
    /// </summary>
    public async Task RefreshAsync()
    {
        await _model.RefreshAsync();

        ShowRows(selectedResources: null);
    }

    /// <summary>
    /// Shows the model's rows. If selectedResources is provided, those resources are selected afterwards.
    /// Otherwise, the current selection is preserved.
    /// </summary>
    private void ShowRows(List<ResourceKey>? selectedResources)
    {
        var rows = _model.Rows;

        // A row is replaced only when its appearance changes, so unchanged rows mean an unchanged tree. Swapping
        // the collection anyway would destroy and regenerate every row for nothing.
        if (TreeItems.SequenceEqual(rows))
        {
            if (selectedResources is not null)
            {
                SelectionRequested?.Invoke(selectedResources);
            }

            return;
        }

        // Notify view to save scroll position before rebuild
        PreBuildResourceTree?.Invoke();

        // Use provided selection, or preserve current selection
        var resourcesToSelect = selectedResources ?? GetSelectedResourceKeys();

        // Replace the entire collection to trigger a single UI update (no flicker)
        TreeItems = new ObservableCollection<ResourceViewItem>(rows);

        if (resourcesToSelect.Count > 0)
        {
            SelectionRequested?.Invoke(resourcesToSelect);
        }

        // Notify view to restore scroll position after rebuild
        PostBuildResourceTree?.Invoke();
    }

    /// <summary>
    /// Gets the resource key for the currently selected item (anchor).
    /// </summary>
    public ResourceKey GetSelectedResourceKey()
    {
        if (SelectedItem is not null)
        {
            return SelectedItem.Resource;
        }
        return ResourceKey.Empty;
    }

    /// <summary>
    /// Gets the list of selected resource keys.
    /// </summary>
    public List<ResourceKey> GetSelectedResourceKeys()
    {
        var selectedResources = SelectedItems
            .Select(item => item.Resource)
            .ToList();

        return selectedResources;
    }

    /// <summary>
    /// Sets the selected item by resource key.
    /// </summary>
    public bool SetSelectedResource(ResourceKey resourceKey)
    {
        if (resourceKey.IsEmpty)
        {
            SelectedItem = null;
            return true;
        }

        foreach (var item in TreeItems)
        {
            if (item.Resource == resourceKey)
            {
                SelectedItem = item;
                return true;
            }
        }

        _logger.LogDebug($"SetSelectedResource: Item '{resourceKey}' not found in tree (count = {TreeItems.Count})");
        return false;
    }

    /// <summary>
    /// Selects the parent folder of the currently selected item.
    /// Returns true if a parent was selected, false otherwise.
    /// </summary>
    public bool SelectParentFolder()
    {
        if (SelectedItem is null)
        {
            return false;
        }

        var parentKey = SelectedItem.Resource.GetParent();
        if (parentKey.IsEmpty)
        {
            return false;
        }

        return SetSelectedResource(parentKey);
    }

    //
    // Expand/Collapse
    //

    /// <summary>
    /// Toggles the expansion state of a folder item (except the project folder).
    /// </summary>
    public async Task ToggleExpandAsync(ResourceViewItem item)
    {
        // Don't allow toggling project folder expansion
        if (!item.IsFolder || !item.HasChildren || item.IsProjectFolder)
        {
            return;
        }

        if (item.IsExpanded)
        {
            await CollapseItemAsync(item);
        }
        else
        {
            await ExpandItemAsync(item);
        }
    }

    /// <summary>
    /// Expands a folder item if it's collapsed.
    /// Preserves all existing selections.
    /// </summary>
    public async Task ExpandItemAsync(ResourceViewItem item)
    {
        if (!item.IsFolder || !item.HasChildren || item.IsExpanded)
        {
            return;
        }

        var selectedKeys = GetSelectedResourceKeys();

        await _model.SetExpandedAsync(item.Resource, true);

        ShowRows(selectedKeys);
        RequestWorkspaceSave();
    }

    /// <summary>
    /// Collapses a folder item if it's expanded.
    /// When collapsing, any selected items inside the folder cause the folder to become selected.
    /// Items selected outside the folder remain selected.
    /// </summary>
    public async Task CollapseItemAsync(ResourceViewItem item)
    {
        // Don't allow collapsing the project folder
        if (!item.IsFolder || !item.IsExpanded || item.IsProjectFolder)
        {
            return;
        }

        var folderKey = item.Resource;

        // Compute new selection: items inside folder transfer to the folder, others remain
        var selectedKeys = GetSelectedResourceKeys();
        var keysInsideFolder = selectedKeys.Where(key => key.IsDescendantOf(folderKey)).ToList();
        var keysOutsideFolder = selectedKeys.Where(key => !key.IsDescendantOf(folderKey)).ToList();

        var newSelectedKeys = new List<ResourceKey>(keysOutsideFolder);
        if (keysInsideFolder.Count > 0)
        {
            newSelectedKeys.Add(folderKey);
        }

        await _model.SetExpandedAsync(folderKey, false);

        ShowRows(newSelectedKeys);
        RequestWorkspaceSave();
    }

    /// <summary>
    /// Expands or collapses a folder, whether or not the tree shows it.
    /// </summary>
    public async Task SetFolderExpandedAsync(ResourceKey folder, bool isExpanded)
    {
        await _model.SetExpandedAsync(folder, isExpanded);

        ShowRows(selectedResources: null);
    }

    /// <summary>
    /// Collapses all folders in the tree.
    /// </summary>
    public async Task CollapseAllFoldersAsync()
    {
        // Map selected items to their top-level parent folders so we can
        // preserve the selection (to an extent) after collapsing everything.
        var selectedKeys = GetSelectedResourceKeys();
        var topLevelFolderKeys = new HashSet<ResourceKey>();
        foreach (var key in selectedKeys)
        {
            var path = key.ToString();
            if (!string.IsNullOrEmpty(path))
            {
                var firstSlash = path.IndexOf('/');
                var topLevelPath = firstSlash > 0 ? path.Substring(0, firstSlash) : path;
                topLevelFolderKeys.Add(new ResourceKey(topLevelPath));
            }
        }

        await _model.CollapseAllAsync();

        ShowRows(topLevelFolderKeys.ToList());
        RequestWorkspaceSave();
    }

    /// <summary>
    /// Expands every folder above each resource so the tree shows each resource. Turns Show Hidden Files on when
    /// the tree hides a resource or a folder above it. Fails when a resource does not exist.
    /// </summary>
    public async Task<Result> RevealResourcesAsync(IReadOnlyList<ResourceKey> resources)
    {
        var showedHiddenFiles = _model.ShowHiddenFiles;

        var failedResults = new List<Result>();
        foreach (var resource in resources)
        {
            var revealResult = await _model.RevealAsync(resource);
            if (revealResult.IsFailure)
            {
                failedResults.Add(revealResult);
            }
        }

        if (_model.ShowHiddenFiles != showedHiddenFiles)
        {
            OnPropertyChanged(nameof(ShowHiddenFiles));
        }

        ShowRows(selectedResources: null);
        RequestWorkspaceSave();

        if (failedResults.Count > 0)
        {
            var failure = Result.Fail("Failed to reveal resources");
            foreach (var failedResult in failedResults)
            {
                failure.WithErrors(failedResult);
            }

            return failure;
        }

        return Result.Ok();
    }

    private void RequestWorkspaceSave()
    {
        var message = new WorkspaceStateDirtyMessage();
        _messengerService.Send(message);
    }

    //
    // Multi-selection support
    //

    /// <summary>
    /// Updates the SelectedItems collection from the view and sends the selection changed notification.
    /// Called when selection changes in the ListView.
    /// </summary>
    public void UpdateSelectedItems(List<ResourceViewItem> selectedItems)
    {
        SelectedItems.Clear();
        SelectedItems.AddRange(selectedItems);

        // Send the selection changed notification
        var selectedResourceKey = GetSelectedResourceKey();
        OnSelectedResourceChanged(selectedResourceKey);
    }

    /// <summary>
    /// Gets the list of selected resources.
    /// </summary>
    public List<ResourceItem> GetSelectedResources()
    {
        return SelectedItems.Select(item => item.Item).ToList();
    }

    /// <summary>
    /// Finds tree items matching the given resource keys.
    /// </summary>
    public List<ResourceViewItem> FindItemsByResourceKeys(List<ResourceKey> resourceKeys)
    {
        var keySet = new HashSet<ResourceKey>(resourceKeys);

        var items = TreeItems
            .Where(item => keySet.Contains(item.Resource))
            .ToList();

        return items;
    }

    //
    // Selection notification
    //

    public void OnSelectedResourceChanged(ResourceKey resource)
    {
        var message = new SelectedResourceChangedMessage(resource);
        _messengerService.Send(message);
    }

    /// <summary>
    /// Returns the folder for new resources: the selected folder, the selected file's parent folder, or the
    /// project folder when nothing is selected.
    /// </summary>
    public ResourceKey GetSelectedResourceFolder()
    {
        if (SelectedItem is null)
        {
            return ResourceKey.Empty;
        }

        return SelectedItem.IsFolder
            ? SelectedItem.Resource
            : SelectedItem.Resource.GetParent();
    }

    /// <summary>
    /// Gets all sibling items of the specified item (or root-level items if nothing specified).
    /// </summary>
    public List<ResourceViewItem> GetSiblingItems(ResourceViewItem? selectedItem = null)
    {
        // Use the provided item, or fall back to the current selection
        var item = selectedItem ?? SelectedItem;

        // The parent of the item, or the project folder for project-level items and for no selection
        var targetParentKey = item is not null
            ? item.Resource.GetParent()
            : ResourceKey.Empty;

        return TreeItems
            .Where(i => !i.IsProjectFolder && i.Resource.GetParent() == targetParentKey)
            .ToList();
    }
}
