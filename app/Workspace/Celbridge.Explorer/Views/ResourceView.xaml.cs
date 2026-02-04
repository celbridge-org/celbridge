using Celbridge.Explorer.Models;
using Celbridge.Explorer.ViewModels;
using Celbridge.Logging;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Explorer.Views;

/// <summary>
/// A custom tree control built on ListView, because TreeView is not flexible enough.
/// </summary>
public sealed partial class ResourceView : UserControl, IResourceTreeView
{
    private readonly ILogger<ResourceView> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private IResourceRegistry? _resourceRegistry;
    private bool _isPopulating;

    public ResourceViewViewModel ViewModel { get; }

    // Localized strings for context menu
    private string RunString => _stringLocalizer.GetString("ResourceTree_Run");
    private string OpenString => _stringLocalizer.GetString("ResourceTree_Open");
    private string AddFileString => _stringLocalizer.GetString("ResourceTree_AddFile");
    private string AddFolderString => _stringLocalizer.GetString("ResourceTree_AddFolder");
    private string CutString => _stringLocalizer.GetString("ResourceTree_Cut");
    private string CopyString => _stringLocalizer.GetString("ResourceTree_Copy");
    private string PasteString => _stringLocalizer.GetString("ResourceTree_Paste");
    private string DeleteString => _stringLocalizer.GetString("ResourceTree_Delete");
    private string RenameString => _stringLocalizer.GetString("ResourceTree_Rename");
    private string OpenFileExplorerString => _stringLocalizer.GetString("ResourceTree_OpenFileExplorer");
    private string OpenApplicationString => _stringLocalizer.GetString("ResourceTree_OpenApplication");
    private string CopyResourceKeyString => _stringLocalizer.GetString("ResourceTree_CopyResourceKey");
    private string CopyFilePathString => _stringLocalizer.GetString("ResourceTree_CopyFilePath");

    public ResourceView()
    {
        this.InitializeComponent();

        _logger = ServiceLocator.AcquireService<ILogger<ResourceView>>();
        ViewModel = ServiceLocator.AcquireService<ResourceViewViewModel>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        Loaded += ResourceTreeListView_Loaded;
        Unloaded += ResourceTreeListView_Unloaded;
    }

    private void ResourceTreeListView_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded(this);
    }

    private void ResourceTreeListView_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();
    }

    //
    // IResourceTreeView implementation
    //

    public async Task<Result> PopulateTreeView(IResourceRegistry resourceRegistry)
    {
        // Prevent concurrent population which causes duplicate resources.
        if (_isPopulating)
        {
            return Result.Ok();
        }
        _isPopulating = true;

        try
        {
            _resourceRegistry = resourceRegistry;

            // Save state before rebuilding
            var savedScrollOffset = GetScrollOffset();
            var selectedResourceKey = GetSelectedResource();

            try
            {
                // Rebuild the flat list from the resource registry
                ViewModel.RebuildTreeList();
            }
            catch (Exception ex)
            {
                return Result.Fail($"An exception occurred when populating the tree view.")
                    .WithException(ex);
            }

            // Restore selection
            if (_resourceRegistry.GetResource(selectedResourceKey) != null)
            {
                await SetSelectedResource(selectedResourceKey, scrollIntoView: false);
            }

            // Restore scroll position
            ResourceListView.UpdateLayout();
            SetScrollOffset(savedScrollOffset);

            return Result.Ok();
        }
        finally
        {
            _isPopulating = false;
        }
    }

    public ResourceKey GetSelectedResource()
    {
        return ViewModel.GetSelectedResourceKey();
    }

    public async Task<Result> SetSelectedResource(ResourceKey resource, bool scrollIntoView = true)
    {
        if (_resourceRegistry is null)
        {
            return Result.Ok();
        }

        if (resource.IsEmpty)
        {
            ViewModel.SelectedItem = null;
            return Result.Ok();
        }

        // Check if the requested resource exists
        var getResult = _resourceRegistry.GetResource(resource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to get resource from resource registry: {resource}")
                .WithErrors(getResult);
        }

        // Expand parent folders to make the resource visible
        ViewModel.ExpandPathToResource(resource);

        // Find and select the item
        if (!ViewModel.SetSelectedResource(resource))
        {
            return Result.Fail($"No matching tree item found for resource: '{resource}'");
        }

        if (scrollIntoView && ViewModel.SelectedItem != null)
        {
            ResourceListView.ScrollIntoView(ViewModel.SelectedItem);
        }

        return await Task.FromResult(Result.Ok());
    }

    //
    // Scroll position helpers
    //

    private double GetScrollOffset()
    {
        try
        {
            var scrollViewer = FindScrollViewer(ResourceListView);
            return scrollViewer?.VerticalOffset ?? 0;
        }
        catch (Exception)
        {
            // Ignore exceptions during shutdown when visual tree is being torn down
            return 0;
        }
    }

    private void SetScrollOffset(double offset)
    {
        try
        {
            var scrollViewer = FindScrollViewer(ResourceListView);
            scrollViewer?.ChangeView(null, offset, null, disableAnimation: true);
        }
        catch (Exception)
        {
            // Ignore exceptions during shutdown when visual tree is being torn down
        }
    }

    private ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        try
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
                var result = FindScrollViewer(child);
                if (result != null)
                {
                    return result;
                }
            }
        }
        catch (Exception)
        {
            // Ignore exceptions during shutdown when visual tree is being torn down
        }
        return null;
    }

    //
    // Event handlers
    //

    private void ListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // When right-clicking, select the item under the cursor before opening context menu
        // This ensures context menu operations work on the right-clicked item, not the previously selected one
        var position = e.GetPosition(ResourceListView);
        var clickedItem = FindItemAtPosition(position);

        if (clickedItem != null)
        {
            ViewModel.SelectedItem = clickedItem;
        }
    }

    private void ListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var selectedItem = ViewModel.SelectedItem;
        if (selectedItem != null)
        {
            OpenResource(selectedItem);
        }
    }


    private void ExpanderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.DataContext is not ResourceViewItem item)
        {
            return;
        }

        ViewModel.ToggleExpand(item);
    }

    private void OpenResource(ResourceViewItem item)
    {
        if (item.Resource is IFolderResource)
        {
            ViewModel.ToggleExpand(item);
        }
        else if (item.Resource is IFileResource fileResource)
        {
            ViewModel.OpenDocument(fileResource);
        }
    }

    private void ListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

        var selectedItem = ViewModel.SelectedItem;

        if (e.Key == VirtualKey.Delete)
        {
            if (selectedItem?.Resource != null)
            {
                ViewModel.ShowDeleteResourceDialog(selectedItem.Resource);
            }
        }
        else if (e.Key == VirtualKey.Right)
        {
            // Expand folder
            if (selectedItem != null && selectedItem.IsFolder && !selectedItem.IsExpanded)
            {
                ViewModel.ExpandItem(selectedItem);
                e.Handled = true;
            }
        }
        else if (e.Key == VirtualKey.Left)
        {
            // Collapse folder or move to parent
            if (selectedItem != null)
            {
                if (selectedItem.IsFolder && selectedItem.IsExpanded)
                {
                    ViewModel.CollapseItem(selectedItem);
                    e.Handled = true;
                }
                else if (selectedItem.Resource.ParentFolder != null)
                {
                    // Select the parent folder
                    var parentKey = _resourceRegistry?.GetResourceKey(selectedItem.Resource.ParentFolder);
                    if (parentKey.HasValue && !parentKey.Value.IsEmpty)
                    {
                        ViewModel.SetSelectedResource(parentKey.Value);
                        e.Handled = true;
                    }
                }
            }
        }
        else if (e.Key == VirtualKey.Enter)
        {
            if (selectedItem != null)
            {
                OpenResource(selectedItem);
                e.Handled = true;
            }
        }
        else if (control && selectedItem?.Resource != null)
        {
            if (e.Key == VirtualKey.C)
            {
                ViewModel.CopyResourceToClipboard(selectedItem.Resource);
            }
            else if (e.Key == VirtualKey.X)
            {
                ViewModel.CutResourceToClipboard(selectedItem.Resource);
            }
            else if (e.Key == VirtualKey.V)
            {
                ViewModel.PasteResourceFromClipboard(selectedItem.Resource);
            }
        }
    }

    private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Guard.IsNotNull(_resourceRegistry);

        var selectedItem = e.AddedItems.FirstOrDefault() as ResourceViewItem;
        if (selectedItem?.Resource == null)
        {
            ViewModel.OnSelectedResourceChanged(ResourceKey.Empty);
            return;
        }

        var selectedResource = _resourceRegistry.GetResourceKey(selectedItem.Resource);
        ViewModel.OnSelectedResourceChanged(selectedResource);
    }

    //
    // Context menu handlers
    //

    private void ResourceContextMenu_Opening(object sender, object e)
    {
        // With ListView, we use the currently selected item for context menu operations
        var resource = ViewModel.SelectedItem?.Resource;
        ViewModel.OnContextMenuOpening(resource);
    }

    private void ResourceContextMenu_Run(object sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;
        if (resource is IFileResource fileResource)
        {
            ViewModel.RunScript(fileResource);
        }
    }

    private void ResourceContextMenu_Open(object? sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;
        if (resource is IFileResource fileResource)
        {
            ViewModel.OpenDocument(fileResource);
        }
    }

    private void ResourceContextMenu_AddFolder(object? sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;

        if (resource is IFolderResource destFolder)
        {
            ViewModel.ShowAddResourceDialog(ResourceType.Folder, destFolder);
        }
        else if (resource is IFileResource destFile)
        {
            Guard.IsNotNull(destFile.ParentFolder);
            ViewModel.ShowAddResourceDialog(ResourceType.Folder, destFile.ParentFolder);
        }
        else
        {
            ViewModel.ShowAddResourceDialog(ResourceType.Folder, null);
        }
    }

    private void ResourceContextMenu_AddFile(object? sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;

        if (resource is IFolderResource destFolder)
        {
            ViewModel.ShowAddResourceDialog(ResourceType.File, destFolder);
        }
        else if (resource is IFileResource destFile)
        {
            Guard.IsNotNull(destFile.ParentFolder);
            ViewModel.ShowAddResourceDialog(ResourceType.File, destFile.ParentFolder);
        }
        else
        {
            ViewModel.ShowAddResourceDialog(ResourceType.File, null);
        }
    }

    private void ResourceContextMenu_Cut(object sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;
        Guard.IsNotNull(resource);
        ViewModel.CutResourceToClipboard(resource);
    }

    private void ResourceContextMenu_Copy(object sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;
        Guard.IsNotNull(resource);
        ViewModel.CopyResourceToClipboard(resource);
    }

    private void ResourceContextMenu_Paste(object sender, RoutedEventArgs e)
    {
        var destResource = ViewModel.SelectedItem?.Resource;
        ViewModel.PasteResourceFromClipboard(destResource);
    }

    private void ResourceContextMenu_Delete(object? sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;
        Guard.IsNotNull(resource);
        ViewModel.ShowDeleteResourceDialog(resource);
    }

    private void ResourceContextMenu_Rename(object? sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;
        Guard.IsNotNull(resource);
        ViewModel.ShowRenameResourceDialog(resource);
    }

    private void ResourceContextMenu_OpenFileExplorer(object sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;
        ViewModel.OpenResourceInExplorer(resource);
    }

    private void ResourceContextMenu_OpenApplication(object sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;
        ViewModel.OpenResourceInApplication(resource);
    }

    private void ResourceContextMenu_CopyResourceKey(object sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;
        Guard.IsNotNull(resource);
        ViewModel.CopyResourceKeyToClipboard(resource);
    }

    private void ResourceContextMenu_CopyFilePath(object sender, RoutedEventArgs e)
    {
        var resource = ViewModel.SelectedItem?.Resource;
        Guard.IsNotNull(resource);
        ViewModel.CopyFilePathToClipboard(resource);
    }

    //
    // Drag and Drop
    //

    private ResourceViewItem? _dragOverItem;

    private void ListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // Store the dragged items for later use
        var draggedResources = new List<IResource>();
        foreach (var item in e.Items)
        {
            if (item is ResourceViewItem treeItem)
            {
                draggedResources.Add(treeItem.Resource);
            }
        }

        e.Data.Properties["DraggedResources"] = draggedResources;
        e.Data.RequestedOperation = DataPackageOperation.Move;

        // Set text for drag visual - show count of items being dragged
        var count = draggedResources.Count;
        var text = count == 1
            ? draggedResources[0].Name
            : $"{count} items";

        e.Data.Properties.Add(StandardDataFormats.Text, text);
    }

    private void ListView_DragOver(object sender, DragEventArgs e)
    {
        // Clear previous highlight
        if (_dragOverItem != null)
        {
            _dragOverItem = null;
        }

        // Find the item under the cursor
        var position = e.GetPosition(ResourceListView);
        var targetItem = FindItemAtPosition(position);

        // Determine if drop is allowed
        bool canDrop = false;

        // Check for internal drag (from our ListView)
        if (e.Data?.Properties?.ContainsKey("DraggedResources") == true)
        {
            // Internal drag - check if target is valid
            if (targetItem?.Resource is IFolderResource)
            {
                canDrop = true;
                _dragOverItem = targetItem;
            }
            else if (targetItem?.Resource is IFileResource)
            {
                canDrop = true;
                _dragOverItem = targetItem;
            }
        }
        // Check for external drag (from File Explorer, etc.)
        else if (e.DataView?.Contains(StandardDataFormats.StorageItems) == true)
        {
            // External drag - allow drop on any folder or file (will use parent)
            if (targetItem?.Resource is IFolderResource || targetItem?.Resource is IFileResource)
            {
                canDrop = true;
                _dragOverItem = targetItem;
            }
            // Allow drop on empty space (root folder)
            canDrop = true;
        }

        // Update cursor and accepted operation
        if (canDrop)
        {
            // For internal drags, check if Ctrl is pressed for copy operation
            if (e.Data?.Properties?.ContainsKey("DraggedResources") == true)
            {
                var isControlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                    .HasFlag(CoreVirtualKeyStates.Down);

                e.AcceptedOperation = isControlPressed
                    ? DataPackageOperation.Copy
                    : DataPackageOperation.Move;
                e.DragUIOverride.Caption = e.AcceptedOperation == DataPackageOperation.Copy ? "Copy" : "Move";
            }
            else
            {
                // External drag - always copy
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Copy";
            }

            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
            e.DragUIOverride.Caption = "Cannot drop here";
            e.DragUIOverride.IsCaptionVisible = true;
        }

        e.Handled = true;
    }

    private void ListView_Drop(object sender, DragEventArgs e)
    {
        Guard.IsNotNull(_resourceRegistry);

        // Clear drag-over highlight
        _dragOverItem = null;

        e.Handled = true;

        // Find the drop target
        var position = e.GetPosition(ResourceListView);
        var dropTargetItem = FindItemAtPosition(position);

        IFolderResource destFolder;
        if (dropTargetItem?.Resource is IFileResource fileResource)
        {
            destFolder = fileResource.ParentFolder ?? _resourceRegistry.RootFolder;
        }
        else if (dropTargetItem?.Resource is IFolderResource folderResource)
        {
            destFolder = folderResource;
        }
        else
        {
            destFolder = _resourceRegistry.RootFolder;
        }

        // Check if this is an internal drag (from our ListView)
        if (e.Data?.Properties?.TryGetValue("DraggedResources", out var draggedObj) == true &&
            draggedObj is List<IResource> draggedResources)
        {
            ViewModel.MoveResourcesToFolder(draggedResources, destFolder);
            return;
        }

        // Handle external drop (from file explorer)
        if (e.DataView?.Contains(StandardDataFormats.StorageItems) == true)
        {
            _ = ProcessExternalDrop(e.DataView, destFolder);
        }
    }

    private async Task ProcessExternalDrop(DataPackageView dataView, IFolderResource destFolder)
    {
        var sourcePaths = new List<string>();
        var items = await dataView.GetStorageItemsAsync();
        if (items.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item is StorageFile storageFile)
            {
                sourcePaths.Add(Path.GetFullPath(storageFile.Path));
            }
            else if (item is StorageFolder storageFolder)
            {
                sourcePaths.Add(Path.GetFullPath(storageFolder.Path));
            }
        }

        _ = ViewModel.ImportResources(sourcePaths, destFolder);
    }

    private ResourceViewItem? FindItemAtPosition(Point position)
    {
        foreach (var item in ViewModel.TreeItems)
        {
            var container = ResourceListView.ContainerFromItem(item) as ListViewItem;
            if (container != null)
            {
                var bounds = container.TransformToVisual(ResourceListView)
                    .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                if (bounds.Contains(position))
                {
                    return item;
                }
            }
        }
        return null;
    }

    //
    // Public methods for toolbar actions (called from ExplorerPanel)
    //

    /// <summary>
    /// Adds a file to the currently selected folder (or root if nothing selected).
    /// </summary>
    public void AddFileToSelectedFolder()
    {
        var destFolder = ViewModel.GetSelectedResourceFolder();
        ViewModel.ShowAddResourceDialog(ResourceType.File, destFolder);
    }

    /// <summary>
    /// Adds a folder to the currently selected folder (or root if nothing selected).
    /// </summary>
    public void AddFolderToSelectedFolder()
    {
        var destFolder = ViewModel.GetSelectedResourceFolder();
        ViewModel.ShowAddResourceDialog(ResourceType.Folder, destFolder);
    }

    /// <summary>
    /// Collapses all folders in the tree view.
    /// </summary>
    public void CollapseAllFolders()
    {
        ViewModel.CollapseAllFolders();
    }
}

/// <summary>
/// Converter that returns the appropriate chevron glyph based on expanded state.
/// </summary>
public class ExpanderGlyphConverter : IValueConverter
{
    // Chevron right (collapsed)
    private const string CollapsedGlyph = "\uE76C";
    // Chevron down (expanded)
    private const string ExpandedGlyph = "\uE70D";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isExpanded && isExpanded)
        {
            return ExpandedGlyph;
        }
        return CollapsedGlyph;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
