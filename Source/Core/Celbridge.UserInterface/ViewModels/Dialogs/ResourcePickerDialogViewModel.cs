using System.ComponentModel;
using Celbridge.Workspace;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Celbridge.UserInterface.ViewModels;

public partial class ResourcePickerDialogViewModel : ObservableObject
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private IResourceRegistry? _registry;
    private IResourceFileSystem? _fileSystem;
    private List<string> _extensions = [];
    private List<ResourcePickerItem> _allItems = [];
    private bool _showPreview;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private List<ResourcePickerItem> _filteredItems = [];

    [ObservableProperty]
    private ResourcePickerItem? _selectedItem;

    [ObservableProperty]
    private bool _isSubmitEnabled = false;

    [ObservableProperty]
    private BitmapImage? _previewImage;

    [ObservableProperty]
    private Visibility _previewPanelVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _previewImageVisibility = Visibility.Collapsed;

    public ResourcePickerDialogViewModel(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
        PropertyChanged += OnPropertyChanged;
    }

    public void Initialize(IReadOnlyList<string> extensions, bool showPreview)
    {
        // The resource picker only makes sense for a loaded project. Callers
        // (DialogService.ShowResourcePickerDialogAsync) already short-circuit
        // with a user-facing error in that case; the guard here is a
        // belt-and-braces safety net against a future caller that forgets.
        Guard.IsTrue(_workspaceWrapper.IsWorkspacePageLoaded);

        var workspaceService = _workspaceWrapper.WorkspaceService;
        _registry = workspaceService.ResourceService.Registry;
        _fileSystem = workspaceService.ResourceFileSystem;
        _showPreview = showPreview;
        _extensions = extensions
            .Select(e => e.TrimStart('.').ToLowerInvariant())
            .ToList();

        // Show the preview panel container if preview is enabled (reserves space)
        PreviewPanelVisibility = showPreview ? Visibility.Visible : Visibility.Collapsed;

        _allItems = BuildFlatList(_registry);
        UpdateFilteredItems();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchText))
        {
            UpdateFilteredItems();
        }
        else if (e.PropertyName == nameof(SelectedItem))
        {
            IsSubmitEnabled = SelectedItem is not null;
            UpdatePreview();
        }
    }

    private async void UpdatePreview()
    {
        if (!_showPreview || SelectedItem is null || _registry is null || _fileSystem is null)
        {
            PreviewImageVisibility = Visibility.Collapsed;
            PreviewImage = null;
            return;
        }

        var selectedItem = SelectedItem;
        var resolveResult = _registry.ResolveResourcePath(selectedItem.ResourceKey);
        if (resolveResult.IsFailure)
        {
            PreviewImageVisibility = Visibility.Collapsed;
            PreviewImage = null;
            return;
        }
        var resourcePath = resolveResult.Value;

        var infoResult = await _fileSystem.GetInfoAsync(selectedItem.ResourceKey);
        // The selection can change while the probe is in flight; the late
        // result must not overwrite a newer selection's preview.
        if (!ReferenceEquals(selectedItem, SelectedItem))
        {
            return;
        }
        if (infoResult.IsFailure
            || infoResult.Value.Kind != ResourceInfoKind.File)
        {
            PreviewImageVisibility = Visibility.Collapsed;
            PreviewImage = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.UriSource = new Uri(resourcePath);
            PreviewImage = bitmap;
            PreviewImageVisibility = Visibility.Visible;
        }
        catch
        {
            PreviewImageVisibility = Visibility.Collapsed;
            PreviewImage = null;
        }
    }

    private List<ResourcePickerItem> BuildFlatList(IResourceRegistry registry)
    {
        var items = new List<ResourcePickerItem>();
        CollectFileResources(registry.ProjectFolder, registry, items);
        items.Sort((a, b) => string.Compare(a.DisplayText, b.DisplayText, StringComparison.OrdinalIgnoreCase));
        return items;
    }

    private void CollectFileResources(IFolderResource folder, IResourceRegistry registry, List<ResourcePickerItem> items)
    {
        foreach (var child in folder.Children)
        {
            if (child is IFolderResource subFolder)
            {
                CollectFileResources(subFolder, registry, items);
            }
            else if (child is IFileResource fileResource)
            {
                var ext = Path.GetExtension(child.Name).TrimStart('.').ToLowerInvariant();
                if (_extensions.Count > 0 && !_extensions.Contains(ext))
                {
                    continue;
                }

                var resourceKey = registry.GetResourceKey(child);
                items.Add(new ResourcePickerItem(child, resourceKey, fileResource.Icon));
            }
        }
    }

    private void UpdateFilteredItems()
    {
        var search = SearchText.Trim();

        if (string.IsNullOrEmpty(search))
        {
            FilteredItems = [.. _allItems];
            return;
        }

        var searchLower = search.ToLowerInvariant();
        FilteredItems = _allItems
            .Where(item => item.DisplayTextLower.Contains(searchLower))
            .ToList();
    }
}
