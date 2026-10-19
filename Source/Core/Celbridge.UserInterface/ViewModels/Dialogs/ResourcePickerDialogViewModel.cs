using System.ComponentModel;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Celbridge.UserInterface.ViewModels;

/// <summary>
/// Lists the project's files or folders for the resource picker, filtered by what is typed in its search box.
/// </summary>
public partial class ResourcePickerDialogViewModel : ObservableObject
{
    /// <summary>
    /// The most matches the list shows. A count of the other matches shows below the list.
    /// </summary>
    public const int MaxShownMatches = 200;

    private readonly ILogger<ResourcePickerDialogViewModel> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IIconService _iconService;
    private readonly IStringLocalizer _stringLocalizer;

    private IResourceRegistry? _registry;
    private IResourceFileSystem? _resourceFileSystem;
    private List<string> _extensions = [];
    private bool _listsFolders;
    private List<ResourcePickerItem> _allItems = [];
    private List<ResourcePickerItem> _matchingItems = [];
    private bool _showPreview;
    private Task? _loadTask;

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

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private Visibility _loadingVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _moreMatchesText = string.Empty;

    [ObservableProperty]
    private Visibility _moreMatchesVisibility = Visibility.Collapsed;

    public ResourcePickerDialogViewModel(
        ILogger<ResourcePickerDialogViewModel> logger,
        IWorkspaceWrapper workspaceWrapper,
        IIconService iconService,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _iconService = iconService;
        _stringLocalizer = stringLocalizer;
        PropertyChanged += OnPropertyChanged;
    }

    /// <summary>
    /// Sets the picker up to list the project's files, keeping only those with one of the extensions when any are
    /// given. The list stays empty, and shows that it is loading, until LoadItemsAsync has walked the project.
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
        _listsFolders = false;
        _extensions = extensions
            .Select(e => e.TrimStart('.').ToLowerInvariant())
            .ToList();

        // Shown whenever preview is enabled, so the panel reserves its space either way.
        PreviewPanelVisibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        SearchPlaceholder = _stringLocalizer.GetString("ResourcePickerDialog_SearchPlaceholder");

        ShowLoading();
    }

    /// <summary>
    /// Sets the picker up to list every folder in the project, at any depth, but not the project folder itself.
    /// The list stays empty, and shows that it is loading, until LoadItemsAsync has walked the project.
    /// </summary>
    public void InitializeForFolders()
    {
        Guard.IsTrue(_workspaceWrapper.IsWorkspaceLoaded);

        var workspaceService = _workspaceWrapper.WorkspaceService;
        _registry = workspaceService.ResourceService.Registry;
        _resourceFileSystem = workspaceService.ResourceService.FileSystem;
        _showPreview = false;
        _listsFolders = true;
        _extensions = [];

        PreviewPanelVisibility = Visibility.Collapsed;
        SearchPlaceholder = _stringLocalizer.GetString("ResourcePickerDialog_FolderSearchPlaceholder");

        ShowLoading();
    }

    /// <summary>
    /// Walks the project and lists what the picker offers. The file picker leaves out what search-exclude matches.
    /// The folder picker lists every folder. Only the first call walks, and a later call waits for that walk.
    /// </summary>
    public async Task LoadItemsAsync()
    {
        _loadTask ??= WalkAndShowItemsAsync();
        await _loadTask;
    }

    /// <summary>
    /// Returns the item for the resource among every item that matches the search, including the matches beyond
    /// the shown list. Returns null when no matching item has that key.
    /// </summary>
    public ResourcePickerItem? FindMatchingItem(ResourceKey resource)
    {
        return _matchingItems.FirstOrDefault(item => item.ResourceKey.Equals(resource));
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

    private void ShowLoading()
    {
        IsLoading = true;
        LoadingVisibility = Visibility.Visible;
    }

    private async Task WalkAndShowItemsAsync()
    {
        var items = await ListItemsAsync();

        IsLoading = false;
        LoadingVisibility = Visibility.Collapsed;

        ShowItems(items);
    }

    // Returns an empty list when the walk fails, so the picker stops loading and stays usable.
    private async Task<List<ResourcePickerItem>> ListItemsAsync()
    {
        try
        {
            var walker = _workspaceWrapper.WorkspaceService.ResourceService.Walker;

            // The folder picker offers any folder, including one inside a folder that search-exclude matches.
            var applySearchExclude = !_listsFolders;
            var walkResult = await walker.WalkAsync(ResourceKey.Empty, applySearchExclude);
            if (walkResult.IsFailure)
            {
                _logger.LogWarning(walkResult, "Failed to walk the project for the resource picker");
                return [];
            }
            var walkedItems = walkResult.Value;

            var items = new List<ResourcePickerItem>();
            foreach (var walkedItem in walkedItems)
            {
                var item = CreateItem(walkedItem);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list the project for the resource picker");
            return [];
        }
    }

    private ResourcePickerItem? CreateItem(WalkedItem walkedItem)
    {
        if (walkedItem.IsFolder != _listsFolders)
        {
            return null;
        }

        if (_listsFolders)
        {
            return CreateItem(walkedItem, _iconService.DefaultFolderIcon);
        }

        var fileName = walkedItem.Resource.ResourceName;
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (_extensions.Count > 0
            && !_extensions.Contains(extension))
        {
            return null;
        }

        var getIconResult = _iconService.GetFileIconForFileName(fileName);
        var iconDefinition = getIconResult.IsSuccess
            ? getIconResult.Value
            : _iconService.DefaultFileIcon;

        return CreateItem(walkedItem, iconDefinition);
    }

    private ResourcePickerItem CreateItem(WalkedItem walkedItem, IconDefinition iconDefinition)
    {
        // The picker lists project: alone, and that root accepts writes, so only the attribute makes an item read-only.
        var writableState = WritableState.Writable;
        if ((walkedItem.Attributes & FileSystemAttributes.ReadOnly) != 0)
        {
            writableState = WritableState.ReadOnlyAttribute;
        }

        var readOnlyMessage = ReadOnlyMessageHelper.GetReadOnlyMessage(writableState, _stringLocalizer);

        return new ResourcePickerItem(walkedItem.Resource, iconDefinition, writableState, readOnlyMessage);
    }

    private void ShowItems(List<ResourcePickerItem> items)
    {
        items.Sort((a, b) => string.Compare(a.DisplayText, b.DisplayText, StringComparison.OrdinalIgnoreCase));
        _allItems = items;

        UpdateFilteredItems();
    }

    private void UpdateFilteredItems()
    {
        var search = SearchText.Trim();

        var matchingItems = _allItems;
        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.ToLowerInvariant();
            matchingItems = _allItems
                .Where(item => item.DisplayTextLower.Contains(searchLower))
                .ToList();
        }

        _matchingItems = matchingItems;
        FilteredItems = matchingItems
            .Take(MaxShownMatches)
            .ToList();

        var otherMatchCount = matchingItems.Count - FilteredItems.Count;
        if (otherMatchCount == 0)
        {
            MoreMatchesText = string.Empty;
            MoreMatchesVisibility = Visibility.Collapsed;
            return;
        }

        var moreMatchesKey = otherMatchCount == 1
            ? "ResourcePickerDialog_MoreMatches_One"
            : "ResourcePickerDialog_MoreMatches_Many";
        MoreMatchesText = _stringLocalizer.GetString(moreMatchesKey, otherMatchCount);
        MoreMatchesVisibility = Visibility.Visible;
    }
}
