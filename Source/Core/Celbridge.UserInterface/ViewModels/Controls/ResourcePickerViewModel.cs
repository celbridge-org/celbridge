using System.ComponentModel;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.UserInterface.Helpers;
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
/// In save mode, a name field under the list names the file to save.
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
    private readonly ResourcePickerSearch _search;

    private ResourcePickerOptions _options = new();
    private readonly Dictionary<WritableState, string> _readOnlyMessages = [];
    // Built from the options on each setup. Null until the picker is set up.
    private ResourcePickerItemFactory? _itemFactory;
    private IRootHandlerRegistry? _rootHandlerRegistry;
    private IResourceFileSystem? _resourceFileSystem;
    private List<ResourcePickerItem> _folderItems = [];
    // False until the picker lists a folder. Until then the breadcrumb is empty, and folder mode has nothing to choose.
    private bool _isFolderListed;
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

    [ObservableProperty]
    private bool _isAskingToReplace;

    [ObservableProperty]
    private string _replaceQuestionText = string.Empty;

    public ResourcePickerViewModel(
        ILogger<ResourcePickerViewModel> logger,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        IIconService iconService,
        IStringLocalizer stringLocalizer,
        IResourceNameValidatorFactory nameValidatorFactory)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _iconService = iconService;
        _stringLocalizer = stringLocalizer;
        _search = new ResourcePickerSearch(logger);
        NameField = new ResourcePickerNameField(stringLocalizer, nameValidatorFactory);

        // Each message is looked up once here, so the walk can build items off the UI thread. The messages never
        // change afterwards, so a walk still running when the picker is set up again reads them safely.
        foreach (var writableState in Enum.GetValues<WritableState>())
        {
            var message = ReadOnlyMessageHelper.GetReadOnlyMessage(writableState, _stringLocalizer) ?? string.Empty;
            _readOnlyMessages[writableState] = message;
        }

        PropertyChanged += OnPropertyChanged;
        NameField.PropertyChanged += NameField_PropertyChanged;
    }

    /// <summary>
    /// The name field of save mode, which names the file to save in the current folder.
    /// </summary>
    public ResourcePickerNameField NameField { get; }

    /// <summary>
    /// Returns what the user chooses: a file to open, a place to save a file, or a folder.
    /// </summary>
    public ResourcePickerMode Mode => _options.Mode;

    /// <summary>
    /// Sets the picker up from the options. The list stays empty until OpenAsync opens the start folder. A picker set
    /// up again starts empty in the same way, with the search and the name field cleared.
    /// </summary>
    public void Initialize(ResourcePickerOptions options)
    {
        // The resource picker needs a loaded project. The caller checks for one first, and tells the user when no
        // project is loaded. This guard catches a caller that skips that check.
        Guard.IsTrue(_workspaceWrapper.IsWorkspaceLoaded);

        _options = options;

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceService = workspaceService.ResourceService;
        _rootHandlerRegistry = resourceService.RootHandlers;
        _resourceFileSystem = resourceService.FileSystem;

        // Folder mode lists folders only, so the extensions don't apply.
        var extensions = options.Mode == ResourcePickerMode.Folder
            ? ResourcePickerExtensions.Any
            : new ResourcePickerExtensions(options.Extensions);

        _itemFactory = new ResourcePickerItemFactory(
            options.Mode,
            extensions,
            workspaceService.BindableWorkspaceSettings.ShowHiddenFiles,
            resourceService.Policy,
            resourceService.RootHandlers,
            _iconService,
            _readOnlyMessages);

        TitleText = options.Title ?? GetDefaultTitle(options.Mode);

        ExtensionFilterText = extensions.FilterText;
        ExtensionFilterVisibility = extensions.AllowsAny
            ? Visibility.Collapsed
            : Visibility.Visible;

        var showsPreview = options.ShowPreview
            && options.Mode == ResourcePickerMode.Open;
        PreviewPanelVisibility = showsPreview
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Clear the earlier folder, selection, search and name, so the primary button chooses only from this setup.
        _folderItems = [];
        _isFolderListed = false;
        CurrentFolder = ResourceKey.Empty;
        Crumbs = [];
        SearchText = string.Empty;
        UpdateShownItems();

        NameField.Reset(extensions, isSaveMode: options.Mode == ResourcePickerMode.Save);
        UpdatePrimaryButton();
    }

    /// <summary>
    /// Opens the picker where the options say, and starts walking the project for the search. The picker tries the
    /// default resource first, then the options' start folder, and then the project folder. In open mode, the picker
    /// selects the default resource in its folder. In save mode, the default resource's name goes in the name field.
    /// The picker skips a resource outside the project, and a folder that is missing.
    /// </summary>
    public async Task OpenAsync()
    {
        _walkTask ??= WalkProjectAsync();

        if (await TryOpenDefaultResourceAsync())
        {
            return;
        }

        var startFolder = _options.StartFolder;
        if (!startFolder.IsEmpty
            && startFolder.Root == ResourceKey.DefaultRoot)
        {
            if (await TryOpenFolderAsync(startFolder))
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
    /// user's choice. In save mode, the file's name goes in the name field. For a search result, the file's folder
    /// opens too. For a file in the folder's listing, the file is the user's choice once the user agrees to replace
    /// it. Returns the chosen key, or null when the user's choice is still to come.
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

        if (Mode == ResourcePickerMode.Save)
        {
            if (IsSearching)
            {
                await OpenFolderAsync(item.Resource.GetParent());
                SelectShownItem(item.Resource);
                NameField.Suggest(item.Name);
                return null;
            }

            NameField.Suggest(item.Name);
            return GetConfirmedChoice();
        }

        return null;
    }

    /// <summary>
    /// Returns true when the name in the name field matches a listed folder. Enter in the name field then opens the
    /// folder. Always false outside save mode, and while the search box has text.
    /// </summary>
    public bool NamesListedFolder()
    {
        return FindNamedFolder() is not null;
    }

    /// <summary>
    /// Opens the listed folder with the name in the name field, and clears the field. Returns false when no listed
    /// folder has that name.
    /// </summary>
    public async Task<bool> OpenNamedFolderAsync()
    {
        var namedFolder = FindNamedFolder();
        if (namedFolder is null)
        {
            return false;
        }

        await OpenFolderAsync(namedFolder.Resource);
        NameField.Text = string.Empty;

        return true;
    }

    /// <summary>
    /// Returns the resource the primary button chooses, or null when the button has nothing to choose. In open mode,
    /// the choice is the selected file. In save mode, the choice is the typed name in the current folder. In folder
    /// mode, the choice is the current folder, whatever row is selected. The breadcrumb ends with that folder. During
    /// a search, the breadcrumb is hidden, so save and folder modes have no choice.
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

        if (Mode == ResourcePickerMode.Save)
        {
            if (!_isFolderListed
                || IsSearching
                || NameField.SaveName is not string saveName)
            {
                return null;
            }

            return CurrentFolder.Combine(saveName);
        }

        if (SelectedItem is { IsFolder: false } selectedFile)
        {
            return selectedFile.Resource;
        }

        return null;
    }

    /// <summary>
    /// Returns the choice once the user has agreed to it, or null. In save mode, a choice that names an existing file
    /// first asks the user whether to replace the file. The method returns null while the question shows, and returns
    /// the choice when called again before the user cancels the question.
    /// </summary>
    public ResourceKey? GetConfirmedChoice()
    {
        var choice = GetChoice();
        if (choice is null)
        {
            return null;
        }

        if (Mode != ResourcePickerMode.Save
            || !NameField.IsSaveNameTaken
            || IsAskingToReplace)
        {
            return choice;
        }

        ReplaceQuestionText = _stringLocalizer.GetString(
            "ResourcePickerDialog_ReplaceQuestion",
            choice.Value.ResourceName);
        IsAskingToReplace = true;

        return null;
    }

    /// <summary>
    /// Takes away the question whether to replace a file, so the user can choose again.
    /// </summary>
    public void CancelReplace()
    {
        IsAskingToReplace = false;
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
    /// Checks an automated answer. In open mode, the answer must be a file, and the picker must list the file. In
    /// folder mode, the answer must be a folder, and the picker must list the folder. In save mode, the answer must
    /// name a file in an existing folder, and the name field must accept the name. A save answer can name an existing
    /// file, and no question asks to replace the file. Returns the key to choose, or fails with the reason.
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

        if (Mode == ResourcePickerMode.Save)
        {
            return await CheckSaveAnswerAsync(resource);
        }

        Guard.IsNotNull(_itemFactory);
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

        if (!_itemFactory.IsListedKind(resourceItem.Resource, resourceItem.IsFolder))
        {
            return Result<ResourceKey>.Fail($"The picker does not list '{resource}', because of its extension.");
        }

        if (!_itemFactory.IsShown(resourceItem.Resource, resourceItem.IsFolder))
        {
            return Result<ResourceKey>.Fail($"The Explorer hides '{resource}'.");
        }

        return resourceItem.Resource;
    }

    private async Task<Result<ResourceKey>> CheckSaveAnswerAsync(ResourceKey resource)
    {
        Guard.IsNotNull(_resourceFileSystem);

        var folder = resource.GetParent();
        var listResult = await _resourceFileSystem.EnumerateFolderAsync(folder);
        if (listResult.IsFailure)
        {
            return Result<ResourceKey>.Fail($"The folder of '{resource}' does not exist.");
        }
        var folderChildren = listResult.Value;

        var name = resource.ResourceName;
        var nameError = NameField.CheckName(folder, folderChildren, name);
        if (nameError is not null)
        {
            return Result<ResourceKey>.Fail($"'{resource}' cannot be saved: {nameError}");
        }

        return folder.Combine(ResourcePickerNameField.GetStoredName(folderChildren, name));
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

            // In save mode, clicking a file in the folder's listing puts the file's name in the name field, to
            // replace the file. A search result's file is in another folder, so the name field waits until the user
            // opens the result.
            if (Mode == ResourcePickerMode.Save
                && !IsSearching
                && SelectedItem is { IsFolder: false } selectedFile)
            {
                NameField.Suggest(selectedFile.Name);
            }
        }
        else if (e.PropertyName == nameof(IsSearching))
        {
            UpdatePrimaryButton();
        }
        else if (e.PropertyName == nameof(IsAskingToReplace))
        {
            UpdatePrimaryButton();
        }
    }

    // A change to the typed name takes away the question whether to replace a file, and can change the choice.
    private void NameField_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResourcePickerNameField.Text))
        {
            IsAskingToReplace = false;
            UpdatePrimaryButton();
        }
    }

    private string GetDefaultTitle(ResourcePickerMode mode)
    {
        if (mode == ResourcePickerMode.Folder)
        {
            return _stringLocalizer.GetString("ResourcePickerDialog_FolderTitle");
        }

        if (mode == ResourcePickerMode.Save)
        {
            return _stringLocalizer.GetString("ResourcePickerDialog_SaveTitle");
        }

        return _stringLocalizer.GetString("ResourcePickerDialog_OpenTitle");
    }

    private void UpdatePrimaryButton()
    {
        if (Mode == ResourcePickerMode.Folder)
        {
            PrimaryButtonText = _stringLocalizer.GetString("DialogButton_SelectFolder");
        }
        else if (Mode == ResourcePickerMode.Save)
        {
            var buttonKey = IsAskingToReplace
                ? "DialogButton_Replace"
                : "DialogButton_Save";
            PrimaryButtonText = _stringLocalizer.GetString(buttonKey);
        }
        else
        {
            PrimaryButtonText = _stringLocalizer.GetString("DialogButton_Open");
        }

        // The button is enabled only while it has something to choose.
        IsPrimaryButtonEnabled = GetChoice() is not null;
    }

    // Opens the picker where the default resource says. Returns false when the options have no default resource, or
    // when the picker ignores the default resource.
    private async Task<bool> TryOpenDefaultResourceAsync()
    {
        if (_options.DefaultResource is not ResourceKey defaultResource
            || defaultResource.Root != ResourceKey.DefaultRoot)
        {
            return false;
        }

        if (Mode == ResourcePickerMode.Folder)
        {
            return await TryOpenFolderAsync(defaultResource);
        }

        // In open and save modes, the default resource names a file, so the project folder can't be one.
        if (defaultResource.IsEmpty)
        {
            return false;
        }

        if (!await TryOpenFolderAsync(defaultResource.GetParent()))
        {
            return false;
        }

        if (Mode == ResourcePickerMode.Open)
        {
            SelectShownItem(defaultResource);
        }
        else
        {
            NameField.Suggest(defaultResource.ResourceName);
        }

        return true;
    }

    // Returns false when the folder cannot be listed. When a later listing starts before this one ends, this listing
    // is dropped.
    private async Task<bool> TryOpenFolderAsync(ResourceKey folder)
    {
        Guard.IsNotNull(_itemFactory);
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

        // Save mode checks the typed name against this listing. The checks then always match the listed folder.
        if (Mode == ResourcePickerMode.Save)
        {
            NameField.SetFolder(folder, folderChildren);
        }

        var items = new List<ResourcePickerItem>();
        foreach (var folderChild in folderChildren)
        {
            var item = _itemFactory.CreateItem(
                folderChild.Resource,
                folderChild.IsFolder,
                folderChild.Attributes,
                showsPath: false);
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

    private ResourcePickerItem? FindNamedFolder()
    {
        if (Mode != ResourcePickerMode.Save
            || IsSearching)
        {
            return null;
        }

        return _folderItems.FirstOrDefault(item =>
            item.IsFolder
            && item.Name.Equals(NameField.Text, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateShownItems()
    {
        // A change to the list takes away the question whether to replace a file.
        IsAskingToReplace = false;

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

        if (!_search.IsWalkFinished)
        {
            SetLoading(true);
            ShowItems([], isSearchResult: true);
            return;
        }

        SetLoading(false);

        // The list shows only the first MaxShownMatches matches, so only those matches get a mark on their path.
        var matches = _search.FindMatches(search, MaxShownMatches);
        ShowItems(matches, isSearchResult: true);
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

    // Walks the project once for the search. The walk lists items by the rules of the setup that starts the walk. A
    // search typed during the walk shows its matches when the walk finishes.
    private async Task WalkProjectAsync()
    {
        Guard.IsNotNull(_itemFactory);

        var walker = _workspaceWrapper.WorkspaceService.ResourceService.Walker;
        await _search.WalkAsync(walker, _itemFactory);

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
}
