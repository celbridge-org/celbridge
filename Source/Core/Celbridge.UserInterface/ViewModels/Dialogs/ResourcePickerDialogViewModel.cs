using System.ComponentModel;
using Celbridge.Workspace;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Celbridge.UserInterface.ViewModels;

/// <summary>
/// Lists the project's files or folders for the resource picker, filtered by what is typed in its search box.
/// </summary>
public partial class ResourcePickerDialogViewModel : ObservableObject
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IIconService _iconService;
    private readonly IStringLocalizer _stringLocalizer;

    private IResourceRegistry? _registry;
    private IResourceFileSystem? _resourceFileSystem;
    private List<string> _extensions = [];
    private List<ResourcePickerItem> _allItems = [];
    private bool _showPreview;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _searchPlaceholder = string.Empty;

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
        IWorkspaceWrapper workspaceWrapper,
        IIconService iconService,
        IStringLocalizer stringLocalizer)
    {
        _workspaceWrapper = workspaceWrapper;
        _iconService = iconService;
        _stringLocalizer = stringLocalizer;
        PropertyChanged += OnPropertyChanged;
    }

    /// <summary>
    /// Lists the project's files, keeping only those with one of the extensions when any are given.
    /// </summary>
    public void Initialize(IReadOnlyList<string> extensions, bool showPreview)
    {
        // The resource picker only makes sense for a loaded project. DialogService reports that to the
        // user before getting here, so this guard only catches a caller that skips it.
        Guard.IsTrue(_workspaceWrapper.IsWorkspaceLoaded);

        var workspaceService = _workspaceWrapper.WorkspaceService;
        _registry = workspaceService.ResourceService.Registry;
        _resourceFileSystem = workspaceService.ResourceService.FileSystem;
        _showPreview = showPreview;
        _extensions = extensions
            .Select(e => e.TrimStart('.').ToLowerInvariant())
            .ToList();

        // Shown whenever preview is enabled, so the panel reserves its space either way.
        PreviewPanelVisibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        SearchPlaceholder = _stringLocalizer.GetString("ResourcePickerDialog_SearchPlaceholder");

        var items = new List<ResourcePickerItem>();
        CollectFileResources(_registry.ProjectFolder, _registry, items);
        ShowItems(items);
    }

    /// <summary>
    /// Lists every folder in the project, at any depth, but not the project folder itself.
    /// </summary>
    public void InitializeForFolders()
    {
        Guard.IsTrue(_workspaceWrapper.IsWorkspaceLoaded);

        var workspaceService = _workspaceWrapper.WorkspaceService;
        _registry = workspaceService.ResourceService.Registry;
        _resourceFileSystem = workspaceService.ResourceService.FileSystem;
        _showPreview = false;
        _extensions = [];

        PreviewPanelVisibility = Visibility.Collapsed;
        SearchPlaceholder = _stringLocalizer.GetString("ResourcePickerDialog_FolderSearchPlaceholder");

        var items = new List<ResourcePickerItem>();
        CollectFolderResources(_registry.ProjectFolder, _registry, items);
        ShowItems(items);
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
        if (!_showPreview || SelectedItem is null || _registry is null || _resourceFileSystem is null)
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

        var infoResult = await _resourceFileSystem.GetInfoAsync(selectedItem.ResourceKey);
        // The selection can change while the probe is in flight, and a late result must not overwrite a
        // newer selection's preview.
        if (!ReferenceEquals(selectedItem, SelectedItem))
        {
            return;
        }
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
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

    private void ShowItems(List<ResourcePickerItem> items)
    {
        items.Sort((a, b) => string.Compare(a.DisplayText, b.DisplayText, StringComparison.OrdinalIgnoreCase));
        _allItems = items;

        UpdateFilteredItems();
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
                var readOnlyMessage = ReadOnlyMessageHelper.GetReadOnlyMessage(child.WritableState, _stringLocalizer);
                items.Add(new ResourcePickerItem(child, resourceKey, fileResource.Icon, readOnlyMessage));
            }
        }
    }

    private void CollectFolderResources(IFolderResource folder, IResourceRegistry registry, List<ResourcePickerItem> items)
    {
        foreach (var child in folder.Children)
        {
            if (child is not IFolderResource subFolder)
            {
                continue;
            }

            var resourceKey = registry.GetResourceKey(subFolder);
            var readOnlyMessage = ReadOnlyMessageHelper.GetReadOnlyMessage(subFolder.WritableState, _stringLocalizer);
            items.Add(new ResourcePickerItem(subFolder, resourceKey, _iconService.DefaultFolderIcon, readOnlyMessage));

            CollectFolderResources(subFolder, registry, items);
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
