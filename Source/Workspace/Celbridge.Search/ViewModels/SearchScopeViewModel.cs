using Celbridge.Dialog;
using Celbridge.Resources;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;

namespace Celbridge.Search.ViewModels;

/// <summary>
/// Describes a change to the Search panel's scope. IsTyped is true for a change typed into a file filter.
/// </summary>
public sealed record SearchScopeChange(bool IsTyped);

/// <summary>
/// Matches every path in a root.
/// </summary>
internal sealed class EveryPathMatcher : IPathMatcher
{
    public static EveryPathMatcher Instance { get; } = new();

    public bool IsMatch(string path, bool isFolder)
    {
        return true;
    }

    public bool CanMatchInside(string folderPath)
    {
        return true;
    }
}

/// <summary>
/// The Search panel's scope: the folder to search, the files to include and exclude, and the toggle that applies the
/// project's search list. The scope keeps each value in the workspace settings, and follows the changes to the
/// searched files.
/// </summary>
public partial class SearchScopeViewModel : ObservableObject
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    // Null while the file filters are refused.
    private SearchFilter? _filter;

    // Follows the changes to the searched files. The monitor drops the subscription when the workspace unloads.
    private IMatcherSubscription? _subscription;

    // False while the constructor reads the workspace settings, so a value read back is not written again.
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _folderText = string.Empty;

    [ObservableProperty]
    private bool _hasFolder;

    [ObservableProperty]
    private string _filesToInclude = string.Empty;

    [ObservableProperty]
    private string _filesToExclude = string.Empty;

    [ObservableProperty]
    private bool _applySearchExclude = true;

    [ObservableProperty]
    private bool _hasInvalidFilter;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _showSummary;

    /// <summary>
    /// The folder to search. The empty key searches the whole project.
    /// </summary>
    public ResourceKey Folder { get; private set; } = ResourceKey.Empty;

    /// <summary>
    /// Raised when the user changes the folder, a file filter or the search list toggle.
    /// </summary>
    public event EventHandler<SearchScopeChange>? Changed;

    /// <summary>
    /// Raised when a searched file changes, whether Celbridge or another program made the change.
    /// </summary>
    public event EventHandler? SearchedFilesChanged;

    public string ToggleTooltip { get; }

    public string FolderPlaceholder { get; }

    public string BrowseFolderTooltip { get; }

    public string ClearFolderTooltip { get; }

    public string FilesToIncludePlaceholder { get; }

    public string FilesToExcludePlaceholder { get; }

    public string SearchListLabel { get; }

    public string SearchListTooltip { get; }

    public string InvalidFilterText { get; }

    private IBindableWorkspaceSettings WorkspaceSettings =>
        _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;

    private IResourceService ResourceService => _workspaceWrapper.WorkspaceService.ResourceService;

    public SearchScopeViewModel(
        IWorkspaceWrapper workspaceWrapper,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _workspaceWrapper = workspaceWrapper;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;

        ToggleTooltip = _stringLocalizer.GetString("SearchPanel_ScopeToggleTooltip");
        FolderPlaceholder = _stringLocalizer.GetString("SearchPanel_FolderPlaceholder");
        BrowseFolderTooltip = _stringLocalizer.GetString("SearchPanel_FolderBrowseTooltip");
        ClearFolderTooltip = _stringLocalizer.GetString("SearchPanel_FolderClearTooltip");
        FilesToIncludePlaceholder = _stringLocalizer.GetString("SearchPanel_FilesToIncludePlaceholder");
        FilesToExcludePlaceholder = _stringLocalizer.GetString("SearchPanel_FilesToExcludePlaceholder");
        SearchListLabel = _stringLocalizer.GetString("SearchPanel_SearchListLabel");
        SearchListTooltip = _stringLocalizer.GetString("SearchPanel_SearchListTooltip");
        InvalidFilterText = _stringLocalizer.GetString("SearchPanel_InvalidFilter");

        var workspaceSettings = WorkspaceSettings;
        IsVisible = workspaceSettings.SearchScopeVisible;
        FilesToInclude = workspaceSettings.SearchFilesToInclude;
        FilesToExclude = workspaceSettings.SearchFilesToExclude;
        ApplySearchExclude = workspaceSettings.ApplySearchExclude;

        if (ResourceKey.TryCreate(workspaceSettings.SearchFolder, out var folder))
        {
            ShowFolder(folder);
        }

        _isLoaded = true;

        CompileFilter();
        UpdateSubscription();
        UpdateSummary();
    }

    /// <summary>
    /// Sets the folder to search. The empty key searches the whole project.
    /// </summary>
    public void SetFolder(ResourceKey folder)
    {
        if (folder == Folder)
        {
            return;
        }

        StoreFolder(folder);

        Changed?.Invoke(this, new SearchScopeChange(IsTyped: false));
    }

    /// <summary>
    /// Returns the options for a search of the matcher's pattern over the scope, or null while a file filter is
    /// refused. A folder that is missing from the project is cleared first, so the search covers the whole project.
    /// </summary>
    public async Task<SearchOptions?> CreateSearchOptionsAsync(ITextMatcher matcher, int? maxResults)
    {
        var filter = _filter;
        if (filter is null)
        {
            return null;
        }

        await ClearMissingFolderAsync();

        var options = new SearchOptions(matcher, filter)
        {
            MaxResults = maxResults,
            Scope = Folder.IsEmpty ? string.Empty : Folder.ToString(),
            ApplySearchExclude = ApplySearchExclude,
        };

        return options;
    }

    [RelayCommand]
    private void Show()
    {
        IsVisible = true;
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        var pickerTitle = _stringLocalizer.GetString("SearchPanel_FolderPickerTitle");
        var pickResult = await _dialogService.ShowFolderPickerDialogAsync(pickerTitle);

        // The dialog reports a dismissal as a failure, and the folder stays unchanged. Picking the project folder
        // searches the whole project.
        if (pickResult.IsFailure)
        {
            return;
        }
        var folder = pickResult.Value;

        SetFolder(folder);
    }

    [RelayCommand]
    private void ClearFolder()
    {
        SetFolder(ResourceKey.Empty);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (!_isLoaded)
        {
            return;
        }

        WorkspaceSettings.SearchScopeVisible = value;
        UpdateSummary();
    }

    partial void OnFilesToIncludeChanged(string value)
    {
        if (!_isLoaded)
        {
            return;
        }

        WorkspaceSettings.SearchFilesToInclude = value;
        OnFiltersChanged();
    }

    partial void OnFilesToExcludeChanged(string value)
    {
        if (!_isLoaded)
        {
            return;
        }

        WorkspaceSettings.SearchFilesToExclude = value;
        OnFiltersChanged();
    }

    partial void OnApplySearchExcludeChanged(bool value)
    {
        if (!_isLoaded)
        {
            return;
        }

        WorkspaceSettings.ApplySearchExclude = value;
        UpdateSubscription();
        UpdateSummary();

        Changed?.Invoke(this, new SearchScopeChange(IsTyped: false));
    }

    private void OnFiltersChanged()
    {
        CompileFilter();
        UpdateSummary();

        Changed?.Invoke(this, new SearchScopeChange(IsTyped: true));
    }

    private void CompileFilter()
    {
        var compileResult = SearchFilter.Compile(FilesToInclude, FilesToExclude);
        if (compileResult.IsFailure)
        {
            _filter = null;
            HasInvalidFilter = true;
            return;
        }

        _filter = compileResult.Value;
        HasInvalidFilter = false;
    }

    private async Task ClearMissingFolderAsync()
    {
        if (Folder.IsEmpty)
        {
            return;
        }

        var infoResult = await ResourceService.FileSystem.GetInfoAsync(Folder);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.Folder)
        {
            return;
        }

        StoreFolder(ResourceKey.Empty);
    }

    private void StoreFolder(ResourceKey folder)
    {
        ShowFolder(folder);

        WorkspaceSettings.SearchFolder = folder.IsEmpty ? string.Empty : folder.ToString();

        UpdateSubscription();
        UpdateSummary();
    }

    private void ShowFolder(ResourceKey folder)
    {
        Folder = folder;
        FolderText = FormatFolder(folder);
        HasFolder = !folder.IsEmpty;
    }

    // The search list matches each folder on a path. When the list excludes the folder, or one of the folder's
    // parents, the search reads the whole folder, so the subscription follows every path.
    private IPathMatcher ChooseSubscriptionMatcher()
    {
        var policy = ResourceService.Policy;
        if (!ApplySearchExclude
            || policy.IsSearchExcluded(Folder, isFolder: true))
        {
            return EveryPathMatcher.Instance;
        }

        return policy.SearchScope;
    }

    // Subscribes with the new matcher before disposing of the old subscription, so no change is missed.
    private void UpdateSubscription()
    {
        var matcher = ChooseSubscriptionMatcher();
        if (_subscription is not null
            && ReferenceEquals(_subscription.Matcher, matcher))
        {
            return;
        }

        var handlers = new ResourceChangeHandlers(OnSearchedFilesChanged, OnSearchedFilesResync);
        var subscribeResult = ResourceService.Monitor.SubscribeToMatcher(ResourceKey.DefaultRoot, matcher, handlers);
        if (subscribeResult.IsFailure)
        {
            return;
        }

        var previousSubscription = _subscription;
        _subscription = subscribeResult.Value;
        previousSubscription?.Dispose();
    }

    private void OnSearchedFilesChanged(ResourceChangeBatch batch)
    {
        SearchedFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    // The lost events may have changed any searched file.
    private void OnSearchedFilesResync(ResourceResync resync)
    {
        SearchedFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    // Writes the line shown while the scope is hidden. The line names the folder, and says when the file filters or
    // the search list toggle differ from their defaults.
    private void UpdateSummary()
    {
        var hasFolder = !Folder.IsEmpty;
        var hasFilters = !string.IsNullOrWhiteSpace(FilesToInclude)
            || !string.IsNullOrWhiteSpace(FilesToExclude)
            || !ApplySearchExclude;

        if (hasFolder
            && hasFilters)
        {
            SummaryText = _stringLocalizer.GetString("SearchPanel_ScopeSummary_FolderAndFilters", FolderText);
        }
        else if (hasFolder)
        {
            SummaryText = _stringLocalizer.GetString("SearchPanel_ScopeSummary_Folder", FolderText);
        }
        else if (hasFilters)
        {
            SummaryText = _stringLocalizer.GetString("SearchPanel_ScopeSummary_Filters");
        }
        else
        {
            SummaryText = string.Empty;
        }

        ShowSummary = !IsVisible
            && SummaryText.Length > 0;
    }

    // A folder in the project shows as its path, like "docs/api". A folder in another root shows its full key.
    private static string FormatFolder(ResourceKey folder)
    {
        if (folder.IsEmpty)
        {
            return string.Empty;
        }

        if (folder.Root == ResourceKey.DefaultRoot)
        {
            return folder.Path;
        }

        return folder.ToString();
    }
}
