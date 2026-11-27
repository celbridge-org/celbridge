using System.ComponentModel;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Patterns;
using Celbridge.Projects;
using Celbridge.UserInterface.Helpers;
using Celbridge.Utilities;
using Celbridge.Validators;
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
    private readonly IResourceNameValidatorFactory _nameValidatorFactory;

    private ResourcePickerOptions _options = new();
    private List<string> _extensions = [];
    private readonly Dictionary<WritableState, string> _readOnlyMessages = [];
    private bool _showHiddenFiles;
    private IResourcePolicy? _policy;
    private IRootHandlerRegistry? _rootHandlerRegistry;
    private IResourceFileSystem? _resourceFileSystem;
    private List<ResourcePickerItem> _folderItems = [];
    // Every child of the current folder. The list leaves some of them out. Save mode finds an existing file here,
    // with the name stored on disk.
    private IReadOnlyList<FolderChild> _folderChildren = [];
    // Checks the typed name in save mode, against the current folder's listing. Null until the picker lists a folder.
    private IValidator? _nameValidator;
    // False until the picker lists a folder. Until then the breadcrumb is empty, and folder mode has nothing to choose.
    private bool _isFolderListed;
    private List<ResourcePickerItem> _searchItems = [];
    private bool _isWalkFinished;
    private Task? _walkTask;
    private int _folderListingCount;
    // In save mode, the name of the file to save, or null while the typed name can't be used.
    private string? _saveName;
    // In save mode, true when a file in the current folder already has the save name.
    private bool _isSaveNameTaken;

    /// <summary>
    /// Raised when the picker puts a name in the name field, for example when the user clicks a file. The view then
    /// selects the name without its extension.
    /// </summary>
    public event EventHandler? NameSuggested;

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
    private Visibility _nameFieldVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string _nameText = string.Empty;

    [ObservableProperty]
    private string _nameErrorText = string.Empty;

    [ObservableProperty]
    private bool _isNameErrorVisible;

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
        _nameValidatorFactory = nameValidatorFactory;

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
        _policy = resourceService.Policy;
        _rootHandlerRegistry = resourceService.RootHandlers;
        _resourceFileSystem = resourceService.FileSystem;
        _showHiddenFiles = workspaceService.BindableWorkspaceSettings.ShowHiddenFiles;

        _extensions = [];
        if (options.Mode != ResourcePickerMode.Folder)
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

        NameFieldVisibility = options.Mode == ResourcePickerMode.Save
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Clear the earlier folder, selection, search and name, so the primary button chooses only from this setup.
        _folderItems = [];
        _folderChildren = [];
        _nameValidator = null;
        _isFolderListed = false;
        CurrentFolder = ResourceKey.Empty;
        Crumbs = [];
        SearchText = string.Empty;
        UpdateShownItems();

        NameText = string.Empty;
        UpdateNameState();
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
                SuggestName(item.Name);
                return null;
            }

            SuggestName(item.Name);
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
        NameText = string.Empty;

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
                || _saveName is null)
            {
                return null;
            }

            return CurrentFolder.Combine(_saveName);
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
            || !_isSaveNameTaken
            || IsAskingToReplace)
        {
            return choice;
        }

        ReplaceQuestionText = _stringLocalizer.GetString("ResourcePickerDialog_ReplaceQuestion", choice.Value.ResourceName);
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
        var nameValidator = _nameValidatorFactory.CreateFromListing(
            folder,
            folderChildren,
            ResourceNameKind.FileToSave);

        var name = resource.ResourceName;
        var nameError = GetNameError(nameValidator, name);
        if (nameError is not null)
        {
            return Result<ResourceKey>.Fail($"'{resource}' cannot be saved: {nameError}");
        }

        return folder.Combine(GetStoredName(folderChildren, name));
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
                SuggestName(selectedFile.Name);
            }
        }
        else if (e.PropertyName == nameof(IsSearching))
        {
            UpdatePrimaryButton();
        }
        else if (e.PropertyName == nameof(NameText))
        {
            UpdateNameState();
        }
        else if (e.PropertyName == nameof(IsAskingToReplace))
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
            SuggestName(defaultResource.ResourceName);
        }

        return true;
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

        // Save mode checks the typed name against this listing. The checks then always match the listed folder.
        _nameValidator = _nameValidatorFactory.CreateFromListing(
            folder,
            folderChildren,
            ResourceNameKind.FileToSave);

        var items = new List<ResourcePickerItem>();
        foreach (var folderChild in folderChildren)
        {
            var item = CreateItem(folderChild.Resource, folderChild.IsFolder, folderChild.Attributes, showsPath: false);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        _folderItems = items;
        _folderChildren = folderChildren;
        _isFolderListed = true;
        CurrentFolder = folder;
        Crumbs = BuildCrumbs(folder);

        UpdateShownItems();
        UpdateNameState();

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
            && item.Name.Equals(NameText, StringComparison.OrdinalIgnoreCase));
    }

    private void SuggestName(string name)
    {
        NameText = name;
        NameSuggested?.Invoke(this, EventArgs.Empty);
    }

    // Checks the typed name in save mode, and sets the save name for the choice. Any change to the name or to the
    // folder takes away the question whether to replace a file.
    private void UpdateNameState()
    {
        IsAskingToReplace = false;
        _saveName = null;
        _isSaveNameTaken = false;

        // The name is checked only in save mode, once the picker has listed a folder.
        if (Mode != ResourcePickerMode.Save
            || _nameValidator is null)
        {
            ShowNameError(string.Empty);
            UpdatePrimaryButton();
            return;
        }

        var typedName = NameText;
        var name = AddSingleExtension(typedName);

        var nameError = GetNameError(_nameValidator, name);
        if (nameError is not null)
        {
            // An empty field disables Save without an error. The field starts empty when there is no suggested name.
            var shownError = string.IsNullOrEmpty(typedName)
                ? string.Empty
                : nameError;
            ShowNameError(shownError);
            UpdatePrimaryButton();
            return;
        }

        _saveName = GetStoredName(_folderChildren, name);
        _isSaveNameTaken = _folderChildren.Any(child =>
            !child.IsFolder
            && child.Resource.ResourceName == _saveName);

        ShowNameError(string.Empty);
        UpdatePrimaryButton();
    }

    // Returns why the name of a file to save can't be used, or null when the name can be used. The validator checks
    // the name against the listing of the file's folder.
    private string? GetNameError(IValidator nameValidator, string name)
    {
        var validationResult = nameValidator.Validate(name);
        if (!validationResult.IsValid)
        {
            return validationResult.Errors.FirstOrDefault() ?? string.Empty;
        }

        if (!HasAllowedExtension(name))
        {
            var extensions = _extensions.Select(extension => $".{extension}");
            var extensionList = string.Join(", ", extensions);
            return _stringLocalizer.GetString("ResourcePickerDialog_ExtensionRequired", extensionList);
        }

        return null;
    }

    private void ShowNameError(string error)
    {
        NameErrorText = error;
        IsNameErrorVisible = !string.IsNullOrEmpty(error);
    }

    // With one allowed extension, adds the extension to a name typed without it.
    private string AddSingleExtension(string typedName)
    {
        if (_extensions.Count != 1
            || string.IsNullOrWhiteSpace(typedName))
        {
            return typedName;
        }

        var extension = $".{_extensions[0]}";
        if (typedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return typedName;
        }

        return typedName + extension;
    }

    // Returns true if the name ends in one of the allowed extensions, or when every extension is allowed.
    private bool HasAllowedExtension(string name)
    {
        if (_extensions.Count == 0)
        {
            return true;
        }

        return _extensions.Any(extension => name.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase));
    }

    // Returns the name stored on disk when a file in the folder has the name in another letter case. A key names an
    // existing file only by its stored name.
    private static string GetStoredName(IReadOnlyList<FolderChild> folderChildren, string name)
    {
        var existingFile = folderChildren.FirstOrDefault(child =>
            !child.IsFolder
            && child.Resource.ResourceName.Equals(name, StringComparison.OrdinalIgnoreCase));

        return existingFile?.Resource.ResourceName ?? name;
    }

    // Returns true if the mode and the extensions let the picker list the resource. Folder mode lists folders only.
    // Open and save modes list folders and the files with an allowed extension.
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

        return HasAllowedExtension(resource.ResourceName);
    }

    // Returns true if the picker shows an item, given whether the Explorer hides it. The picker follows the
    // Explorer's Show Hidden Files toggle.
    private bool IsShown(bool isHidden)
    {
        return !isHidden
            || _showHiddenFiles;
    }

    // Returns null when the picker leaves the resource out. Runs on both the UI thread and the walk's worker thread.
    private ResourcePickerItem? CreateItem(ResourceKey resource, bool isFolder, FileSystemAttributes attributes, bool showsPath)
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

        return new ResourcePickerItem(resource, isFolder, iconDefinition, state, showsPath);
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

        if (!_isWalkFinished)
        {
            SetLoading(true);
            ShowItems([], isSearchResult: true);
            return;
        }

        SetLoading(false);

        // The list shows only the first MaxShownMatches matches, so only those matches get a mark on their path.
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
                        var match = matches.Count < MaxShownMatches
                            ? MarkPatternMatch(filter, item)
                            : item;
                        matches.Add(match);
                    }
                }
            }
        }
        else
        {
            var searchLower = search.ToLowerInvariant();
            foreach (var item in _searchItems)
            {
                var matchStart = item.PathLower.IndexOf(searchLower, StringComparison.Ordinal);
                if (matchStart >= 0)
                {
                    var match = matches.Count < MaxShownMatches
                        ? item.WithPathMark(matchStart, searchLower.Length)
                        : item;
                    matches.Add(match);
                }
            }
        }

        ShowItems(matches, isSearchResult: true);
    }

    // A pattern says whether a path matches, but not which characters matched. A matched folder brings everything
    // inside it. So this method tests each folder above the item, starting at the top, and marks the first folder to
    // match the pattern. When no folder above the item matches, the item matched by itself, and the mark goes on the
    // item's name.
    private static ResourcePickerItem MarkPatternMatch(SearchFilter filter, ResourcePickerItem item)
    {
        var path = item.Resource.Path;
        var segmentStart = 0;
        while (true)
        {
            var segmentEnd = path.IndexOf('/', segmentStart);
            if (segmentEnd < 0)
            {
                break;
            }

            if (filter.IsMatch(path[..segmentEnd], isFolder: true))
            {
                return item.WithPathMark(segmentStart, segmentEnd - segmentStart);
            }

            segmentStart = segmentEnd + 1;
        }

        return item.WithPathMark(segmentStart, path.Length - segmentStart);
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
                    var item = CreateItem(walkedItem.Resource, walkedItem.IsFolder, walkedItem.Attributes, showsPath: true);
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
