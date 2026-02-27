using System.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Celbridge.UserInterface.ViewModels;

public partial class ResourcePickerDialogViewModel : ObservableObject
{
    private readonly IFileIconService _fileIconService;

    private IResourceRegistry? _registry;
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

    public ResourcePickerDialogViewModel(IFileIconService fileIconService)
    {
        _fileIconService = fileIconService;
        PropertyChanged += OnPropertyChanged;
    }

    public void Initialize(IResourceRegistry registry, IReadOnlyList<string> extensions, bool showPreview)
    {
        _registry = registry;
        _showPreview = showPreview;
        _extensions = extensions
            .Select(e => e.TrimStart('.').ToLowerInvariant())
            .ToList();

        // Show the preview panel container if preview is enabled (reserves space)
        PreviewPanelVisibility = showPreview ? Visibility.Visible : Visibility.Collapsed;

        _allItems = BuildFlatList(registry);
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

    private void UpdatePreview()
    {
        if (!_showPreview || SelectedItem is null || _registry is null)
        {
            PreviewImageVisibility = Visibility.Collapsed;
            PreviewImage = null;
            return;
        }

        var resourcePath = _registry.GetResourcePath(SelectedItem.ResourceKey);
        if (File.Exists(resourcePath))
        {
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
        else
        {
            PreviewImageVisibility = Visibility.Collapsed;
            PreviewImage = null;
        }
    }

    private List<ResourcePickerItem> BuildFlatList(IResourceRegistry registry)
    {
        var items = new List<ResourcePickerItem>();
        CollectFileResources(registry.RootFolder, registry, items);
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
