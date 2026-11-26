using System.ComponentModel;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Patterns;
using Celbridge.Projects;
using Celbridge.UserInterface.Helpers;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// One part of the resource picker's breadcrumb: a folder on the path from the project folder to the current folder.
/// The project folder's part shows as an icon, with the project's name as its tooltip.
/// </summary>
public record ResourcePickerCrumb(string Name, ResourceKey Folder)
{
    /// <summary>
    /// Returns true for the first part, which stands for the project folder. A resource key never includes the
    /// project's name, so the part shows as an icon in place of the name.
    /// </summary>
    public bool IsProjectFolder => Folder.IsEmpty;

    /// <summary>
    /// Returns the folder's name, in place of a record's default text, which lists every property.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>
/// Browses the project one folder at a time for the resource picker. Text typed in the search box lists the matches
/// from the whole project in place of the folder. The picker lists what the Explorer lists, in the Explorer's order.
/// </summary>
public partial class ResourcePickerViewModel : ObservableObject
{
    /// <summary>
    /// The most items a folder's listing shows. A count of the other items shows below the list.
    /// </summary>
    public const int MaxShownFolderItems = 200;

    /// <summary>
    /// The most matches a search shows. A count of the other matches shows below the list. The list draws its rows
    /// again on every keystroke, so a search shows fewer rows than a folder's listing. Typing then stays quick in a
    /// large project.
    /// </summary>
    public const int MaxShownMatches = 50;

    private readonly ILogger<ResourcePickerViewModel> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly IIconService _iconService;
    private readonly IStringLocalizer _stringLocalizer;

    private ResourcePickerOptions _options = new();
    private List<string> _extensions = [];
    private readonly Dictionary<WritableState, string> _readOnlyMessages = [];
    private bool _showHiddenFiles;
    private IResourcePolicy? _policy;
    private IRootHandlerRegistry? _rootHandlerRegistry;
    private IResourceFileSystem? _resourceFileSystem;
    private List<ResourcePickerItem> _folderItems = [];
    // False until the picker lists a folder. Until then the breadcrumb is empty, and folder mode has nothing to choose.
    private bool _isFolderListed;
    private List<ResourcePickerItem> _searchItems = [];
    private bool _isWalkFinished;
    private Task? _walkTask;
    private int _folderListingCount;

    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private ResourceKey _currentFolder = ResourceKey.Empty;

    [ObservableProperty]
    private List<ResourcePickerCrumb> _crumbs = [];

    [ObservableProperty]
    private bool _canGoUp;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private Visibility _breadcrumbVisibility = Visibility.Visible;

    [ObservableProperty]
    private Visibility _searchLocationVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _extensionFilterText = string.Empty;

    [ObservableProperty]
    private Visibility _extensionFilterVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private List<ResourcePickerItem> _shownItems = [];

    [ObservableProperty]
    private ResourcePickerItem? _selectedItem;

    [ObservableProperty]
    private string _primaryButtonText = string.Empty;

    [ObservableProperty]
    private bool _isPrimaryButtonEnabled;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private Visibility _loadingVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _moreItemsText = string.Empty;

    [ObservableProperty]
    private Visibility _moreItemsVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private BitmapImage? _previewImage;

    [ObservableProperty]
    private Visibility _previewPanelVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _previewImageVisibility = Visibility.Collapsed;

    public ResourcePickerViewModel(
        ILogger<ResourcePickerViewModel> logger,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        IIconService iconService,
        IStringLocalizer stringLocalizer)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _iconService = iconService;
        _stringLocalizer = stringLocalizer;

        // Each message is looked up once here, so the walk can build items off the UI thread. The messages never
        // change afterwards, so a walk still running when the picker is set up again reads them safely.
        foreach (var writableState in Enum.GetValues<WritableState>())
        {
            var message = ReadOnlyMessageHelper.GetReadOnlyMessage(writableState, _stringLocalizer) ?? string.Empty;
            _readOnlyMessages[writableState] = message;
        }

        PropertyChanged += OnPropertyChanged;
    }

    /// <summary>
    /// Returns what the user chooses: a file to open, or a folder.
    /// </summary>
    public ResourcePickerMode Mode => _options.Mode;

    /// <summary>
    /// Sets the picker up from the options. The list stays empty until OpenAsync opens the start folder. A picker set
    /// up again starts empty in the same way, with the search cleared.
    /// </summary>
    public void Initialize(ResourcePickerOptions options)
    {
        // The resource picker needs a loaded project. The caller checks for one first, and tells the user when no
        // project is loaded. This guard catches a caller that skips that check.
        Guard.IsTrue(_workspaceWrapper.IsWorkspaceLoaded);

        _options = options;

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceService = workspaceService.ResourceService;
        _policy = resourceService.Policy;
        _rootHandlerRegistry = resourceService.RootHandlers;
        _resourceFileSystem = resourceService.FileSystem;
        _showHiddenFiles = workspaceService.BindableWorkspaceSettings.ShowHiddenFiles;

        _extensions = [];
        if (options.Mode == ResourcePickerMode.Open)
        {
            _extensions = options.Extensions
                .Select(extension => extension.TrimStart('.').ToLowerInvariant())
                .Distinct()
                .ToList();
        }

        TitleText = options.Title ?? GetDefaultTitle(options.Mode);

        if (_extensions.Count > 0)
        {
            var filters = _extensions.Select(extension => $"*.{extension}");
            ExtensionFilterText = string.Join(", ", filters);
            ExtensionFilterVisibility = Visibility.Visible;
        }
        else
        {
            ExtensionFilterText = string.Empty;
            ExtensionFilterVisibility = Visibility.Collapsed;
        }

        var showsPreview = options.ShowPreview
            && options.Mode == ResourcePickerMode.Open;
        PreviewPanelVisibility = showsPreview
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Clear the earlier folder, selection and search, so the primary button chooses only a folder from this setup.
        _folderItems = [];
        _isFolderListed = false;
        CurrentFolder = ResourceKey.Empty;
        Crumbs = [];
        SearchText = string.Empty;
        UpdateShownItems();

        UpdatePrimaryButton();
    }

    /// <summary>
    /// Opens the start folder, and starts walking the project for the search. In open mode, the picker tries the
    /// folder of the initial selection first, and selects the initial selection there. Then the picker tries the
    /// options' start folder, and then the project folder. The picker skips a resource outside the project, and a
    /// folder that is missing.
    /// </summary>
    public async Task OpenAsync()
    {
        _walkTask ??= WalkProjectAsync();

        var initialSelection = _options.InitialSelection;
        if (Mode == ResourcePickerMode.Open
            && !initialSelection.IsEmpty
            && initialSelection.Root == ResourceKey.DefaultRoot)
        {
            var startFolder = initialSelection.GetParent();
            if (await TryOpenFolderAsync(startFolder))
            {
                SelectShownItem(initialSelection);
                return;
            }
        }

        var optionsFolder = _options.StartFolder;
        if (!optionsFolder.IsEmpty
            && optionsFolder.Root == ResourceKey.DefaultRoot)
        {
            if (await TryOpenFolderAsync(optionsFolder))
            {
                return;
            }
        }

        if (!await TryOpenFolderAsync(ResourceKey.Empty))
        {
            _logger.LogWarning("The resource picker could not list the project folder");
        }
    }

    /// <summary>
    /// Lists the folder in place of the current folder, and clears the search. A folder that cannot be listed leaves
    /// the picker where it is.
    /// </summary>
    public async Task OpenFolderAsync(ResourceKey folder)
    {
        if (!await TryOpenFolderAsync(folder))
        {
            _logger.LogWarning($"The resource picker could not list the folder '{folder}'");
            return;
        }

        SearchText = string.Empty;
    }

    /// <summary>
    /// Opens the folder above the current folder. Does nothing in the project folder, or while the search box has
    /// text.
    /// </summary>
    public async Task GoUpAsync()
    {
        if (!CanGoUp)
        {
            return;
        }

        var parent = CurrentFolder.GetParent();
        await OpenFolderAsync(parent);
    }

    /// <summary>
    /// Handles Enter or a double-click on an item. A folder opens, and the search clears. In open mode, a file is the
    /// user's choice, and the method returns the file's key. Returns null when the user's choice is still to come.
    /// </summary>
    public async Task<ResourceKey?> InvokeItemAsync(ResourcePickerItem item)
    {
        if (item.IsFolder)
        {
            await OpenFolderAsync(item.Resource);
            return null;
        }

        if (Mode == ResourcePickerMode.Open)
        {
            return item.Resource;
        }

        return null;
    }

    /// <summary>
    /// Returns the resource the primary button chooses, or null when the button has nothing to choose. In open mode,
    /// the choice is the selected file. In folder mode, the choice is the current folder, whatever row is selected.
    /// The breadcrumb ends with that folder. During a search, the breadcrumb is hidden, so folder mode has no choice.
    /// </summary>
    public ResourceKey? GetChoice()
    {
        if (Mode == ResourcePickerMode.Folder)
        {
            if (!_isFolderListed
                || IsSearching)
            {
                return null;
            }

            return CurrentFolder;
        }

        if (SelectedItem is { IsFolder: false } selectedFile)
        {
            return selectedFile.Resource;
        }

        return null;
    }

    /// <summary>
    /// Clears the search box. Returns false when the box was already empty.
    /// </summary>
    public bool ClearSearch()
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            return false;
        }

        SearchText = string.Empty;
        return true;
    }

    /// <summary>
    /// Waits until the search's walk of the project finishes.
    /// </summary>
    public async Task LoadSearchItemsAsync()
    {
        _walkTask ??= WalkProjectAsync();
        await _walkTask;
    }

    /// <summary>
    /// Checks an automated answer. The picker must list the resource, and the resource must be a file in open mode or
    /// a folder in folder mode. Returns the resource's key with the casing of the names on disk, or fails with the
    /// reason.
    /// </summary>
    public async Task<Result<ResourceKey>> CheckAnswerAsync(ResourceKey resource)
    {
        if (resource.Root != ResourceKey.DefaultRoot)
        {
            return Result<ResourceKey>.Fail($"'{resource}' is outside the project.");
        }

        if (resource.IsEmpty)
        {
            if (Mode == ResourcePickerMode.Folder)
            {
                return resource;
            }

            return Result<ResourceKey>.Fail("The project folder is not a file.");
        }

        Guard.IsNotNull(_resourceFileSystem);
        var getItemResult = await _resourceFileSystem.GetResourceItemAsync(resource);
        if (getItemResult.IsFailure)
        {
            return Result<ResourceKey>.Fail($"'{resource}' does not exist.");
        }
        var resourceItem = getItemResult.Value;

        if (Mode == ResourcePickerMode.Folder
            && !resourceItem.IsFolder)
        {
            return Result<ResourceKey>.Fail($"'{resource}' is a file, and the picker asks for a folder.");
        }

        if (Mode == ResourcePickerMode.Open
            && resourceItem.IsFolder)
        {
            return Result<ResourceKey>.Fail($"'{resource}' is a folder, and the picker asks for a file.");
        }

        if (!IsListedKind(resourceItem.Resource, resourceItem.IsFolder))
        {
            return Result<ResourceKey>.Fail($"The picker does not list '{resource}', because of its extension.");
        }

        Guard.IsNotNull(_policy);
        var isHidden = _policy.IsExplorerHidden(resourceItem.Resource, resourceItem.IsFolder);
        if (!IsShown(isHidden))
        {
            return Result<ResourceKey>.Fail($"The Explorer hides '{resource}'.");
        }

        return resourceItem.Resource;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchText))
        {
            UpdateShownItems();
        }
        else if (e.PropertyName == nameof(SelectedItem))
        {
            UpdatePrimaryButton();
            UpdatePreview();
        }
        else if (e.PropertyName == nameof(IsSearching))
        {
            UpdatePrimaryButton();
        }
    }

    private string GetDefaultTitle(ResourcePickerMode mode)
    {
        if (mode == ResourcePickerMode.Folder)
        {
            return _stringLocalizer.GetString("ResourcePickerDialog_FolderTitle");
        }

        return _stringLocalizer.GetString("ResourcePickerDialog_OpenTitle");
    }

    private void UpdatePrimaryButton()
    {
        if (Mode == ResourcePickerMode.Folder)
        {
            PrimaryButtonText = _stringLocalizer.GetString("DialogButton_SelectFolder");
        }
        else
        {
            PrimaryButtonText = _stringLocalizer.GetString("DialogButton_Open");
        }

        // The button is enabled only while it has something to choose.
        IsPrimaryButtonEnabled = GetChoice() is not null;
    }

    // Returns false when the folder cannot be listed. When a later listing starts before this one ends, this listing
    // is dropped.
    private async Task<bool> TryOpenFolderAsync(ResourceKey folder)
    {
        Guard.IsNotNull(_resourceFileSystem);

        _folderListingCount++;
        var listingNumber = _folderListingCount;

        var listResult = await _resourceFileSystem.EnumerateFolderAsync(folder);
        if (listingNumber != _folderListingCount)
        {
            return true;
        }

        if (listResult.IsFailure)
        {
            return false;
        }
        var folderChildren = listResult.Value;

        var items = new List<ResourcePickerItem>();
        foreach (var folderChild in folderChildren)
        {
            var item = CreateItem(folderChild.Resource, folderChild.IsFolder, folderChild.Attributes, showsFolder: false);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        _folderItems = items;
        _isFolderListed = true;
        CurrentFolder = folder;
        Crumbs = BuildCrumbs(folder);

        UpdateShownItems();
        UpdatePrimaryButton();

        return true;
    }

    private List<ResourcePickerCrumb> BuildCrumbs(ResourceKey folder)
    {
        var projectName = _projectService.CurrentProject?.ProjectName ?? string.Empty;
        var crumbs = new List<ResourcePickerCrumb>
        {
            new ResourcePickerCrumb(projectName, ResourceKey.Empty)
        };

        if (folder.IsEmpty)
        {
            return crumbs;
        }

        var crumbFolder = ResourceKey.Empty;
        foreach (var segment in folder.Path.Split('/'))
        {
            crumbFolder = crumbFolder.Combine(segment);
            crumbs.Add(new ResourcePickerCrumb(segment, crumbFolder));
        }

        return crumbs;
    }

    private void SelectShownItem(ResourceKey resource)
    {
        var item = ShownItems.FirstOrDefault(candidate => candidate.Resource == resource);
        if (item is not null)
        {
            SelectedItem = item;
        }
    }

    // Returns true if the mode and the extensions let the picker list the resource. Folder mode lists folders only,
    // and open mode lists folders and the files with an allowed extension.
    private bool IsListedKind(ResourceKey resource, bool isFolder)
    {
        if (isFolder)
        {
            return true;
        }

        if (Mode == ResourcePickerMode.Folder)
        {
            return false;
        }

        if (_extensions.Count == 0)
        {
            return true;
        }

        var extension = Path.GetExtension(resource.ResourceName).TrimStart('.').ToLowerInvariant();
        return _extensions.Contains(extension);
    }

    // Returns true if the picker shows an item, given whether the Explorer hides it. The picker follows the
    // Explorer's Show Hidden Files toggle.
    private bool IsShown(bool isHidden)
    {
        return !isHidden
            || _showHiddenFiles;
    }

    // Returns null when the picker leaves the resource out. Runs on both the UI thread and the walk's worker thread.
    private ResourcePickerItem? CreateItem(ResourceKey resource, bool isFolder, FileSystemAttributes attributes, bool showsFolder)
    {
        if (!IsListedKind(resource, isFolder))
        {
            return null;
        }

        Guard.IsNotNull(_policy);
        Guard.IsNotNull(_rootHandlerRegistry);

        var isHidden = _policy.IsExplorerHidden(resource, isFolder);
        if (!IsShown(isHidden))
        {
            return null;
        }

        var writableState = WritableStatePriority.Compute(resource, attributes, _rootHandlerRegistry);
        var readOnlyMessage = _readOnlyMessages.GetValueOrDefault(writableState, string.Empty);
        var state = new ResourcePickerItemState(isHidden, writableState, readOnlyMessage);

        var iconDefinition = GetIcon(resource, isFolder);

        return new ResourcePickerItem(resource, isFolder, iconDefinition, state, showsFolder);
    }

    private IconDefinition GetIcon(ResourceKey resource, bool isFolder)
    {
        if (isFolder)
        {
            return _iconService.DefaultFolderIcon;
        }

        var getIconResult = _iconService.GetFileIconForFileName(resource.ResourceName);
        if (getIconResult.IsSuccess)
        {
            return getIconResult.Value;
        }

        return _iconService.DefaultFileIcon;
    }

    private void UpdateShownItems()
    {
        var search = SearchText.Trim();
        IsSearching = !string.IsNullOrEmpty(search);

        // Up is disabled during a search. The location bar then hides the current folder. Going up would leave a
        // folder the user can't see.
        CanGoUp = !CurrentFolder.IsEmpty
            && !IsSearching;

        BreadcrumbVisibility = IsSearching
            ? Visibility.Collapsed
            : Visibility.Visible;
        SearchLocationVisibility = IsSearching
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!IsSearching)
        {
            SetLoading(false);
            ShowItems(_folderItems, isSearchResult: false);
            return;
        }

        if (!_isWalkFinished)
        {
            SetLoading(true);
            ShowItems([], isSearchResult: true);
            return;
        }

        SetLoading(false);

        var matches = new List<ResourcePickerItem>();
        if (IsWildcardPattern(search))
        {
            // A pattern that is still being typed, like one with an open brace, fails to compile. Such a pattern
            // matches nothing until the user completes it.
            var compileResult = SearchFilter.Compile(search, string.Empty);
            if (compileResult.IsSuccess)
            {
                var filter = compileResult.Value;
                foreach (var item in _searchItems)
                {
                    if (filter.IsMatch(item.Resource.Path, item.IsFolder))
                    {
                        matches.Add(item);
                    }
                }
            }
        }
        else
        {
            var searchLower = search.ToLowerInvariant();
            foreach (var item in _searchItems)
            {
                if (item.PathLower.Contains(searchLower, StringComparison.Ordinal))
                {
                    matches.Add(item);
                }
            }
        }

        ShowItems(matches, isSearchResult: true);
    }

    // Text with a star or a question mark is a file filter in SearchFilter's dialect. Other text matches any path
    // that contains it, so a name with brackets or braces is found as typed.
    private static bool IsWildcardPattern(string search)
    {
        return search.IndexOfAny(['*', '?']) >= 0;
    }

    private void ShowItems(List<ResourcePickerItem> items, bool isSearchResult)
    {
        var maxShownItems = isSearchResult
            ? MaxShownMatches
            : MaxShownFolderItems;

        SelectedItem = null;
        ShownItems = items
            .Take(maxShownItems)
            .ToList();

        var otherItemCount = items.Count - ShownItems.Count;
        if (otherItemCount == 0)
        {
            MoreItemsText = string.Empty;
            MoreItemsVisibility = Visibility.Collapsed;
            return;
        }

        string moreItemsKey;
        if (isSearchResult)
        {
            moreItemsKey = otherItemCount == 1
                ? "ResourcePickerDialog_MoreMatches_One"
                : "ResourcePickerDialog_MoreMatches_Many";
        }
        else
        {
            moreItemsKey = otherItemCount == 1
                ? "ResourcePickerDialog_MoreItems_One"
                : "ResourcePickerDialog_MoreItems_Many";
        }

        MoreItemsText = _stringLocalizer.GetString(moreItemsKey, otherItemCount);
        MoreItemsVisibility = Visibility.Visible;
    }

    private void SetLoading(bool isLoading)
    {
        IsLoading = isLoading;
        LoadingVisibility = isLoading
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // Walks the project once on a worker thread, and keeps the items the search can find. A walk that fails leaves
    // the search with nothing to find, and the picker stays usable.
    private async Task WalkProjectAsync()
    {
        try
        {
            var walker = _workspaceWrapper.WorkspaceService.ResourceService.Walker;

            IPathMatcher? filter = null;
            if (!_showHiddenFiles)
            {
                Guard.IsNotNull(_policy);
                filter = new ExplorerShownPathMatcher(_policy);
            }

            _searchItems = await Task.Run(async () =>
            {
                var walkResult = await walker.WalkAsync(ResourceKey.Empty, applySearchExclude: false, filter);
                if (walkResult.IsFailure)
                {
                    _logger.LogWarning(walkResult, "Failed to walk the project for the resource picker");
                    return [];
                }
                var walkedItems = walkResult.Value;

                var items = new List<ResourcePickerItem>();
                foreach (var walkedItem in walkedItems)
                {
                    var item = CreateItem(walkedItem.Resource, walkedItem.IsFolder, walkedItem.Attributes, showsFolder: true);
                    if (item is not null)
                    {
                        items.Add(item);
                    }
                }

                return items;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to walk the project for the resource picker");
            _searchItems = [];
        }

        _isWalkFinished = true;

        if (IsSearching)
        {
            UpdateShownItems();
        }
    }

    private async void UpdatePreview()
    {
        if (PreviewPanelVisibility != Visibility.Visible
            || SelectedItem is null
            || SelectedItem.IsFolder
            || _rootHandlerRegistry is null
            || _resourceFileSystem is null)
        {
            HidePreview();
            return;
        }

        var selectedItem = SelectedItem;
        var resolveResult = _rootHandlerRegistry.ResolveResourcePath(selectedItem.Resource);
        if (resolveResult.IsFailure)
        {
            HidePreview();
            return;
        }
        var resourcePath = resolveResult.Value;

        var infoResult = await _resourceFileSystem.GetInfoAsync(selectedItem.Resource);
        // The selection can change while the file's details load. A late result leaves the newer selection's
        // preview alone.
        if (!ReferenceEquals(selectedItem, SelectedItem))
        {
            return;
        }
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            HidePreview();
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
            HidePreview();
        }
    }

    private void HidePreview()
    {
        PreviewImageVisibility = Visibility.Collapsed;
        PreviewImage = null;
    }

    // Matches each path shown in the Explorer. The walk calls only CanMatchInside, so the walk skips the contents of
    // each hidden folder.
    private sealed class ExplorerShownPathMatcher : IPathMatcher
    {
        private readonly IResourcePolicy _policy;

        public ExplorerShownPathMatcher(IResourcePolicy policy)
        {
            _policy = policy;
        }

        public bool IsMatch(string path, bool isFolder)
        {
            var resource = new ResourceKey(path);
            return !_policy.IsExplorerHidden(resource, isFolder);
        }

        public bool CanMatchInside(string folderPath)
        {
            var folder = new ResourceKey(folderPath);
            return !_policy.IsExplorerHidden(folder, isFolder: true);
        }
    }
}
