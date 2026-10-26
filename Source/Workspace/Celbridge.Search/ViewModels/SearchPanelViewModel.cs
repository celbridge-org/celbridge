using System.Collections.ObjectModel;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Utilities;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;

namespace Celbridge.Search.ViewModels;

/// <summary>
/// ViewModel for the search panel. Split into partial classes for maintainability:
/// - SearchPanelViewModel.cs (this file): Core search logic and UI state
/// - SearchPanelViewModel.Replace.cs: Replace operations
/// - SearchPanelViewModel.Selection.cs: Multi-select handling
/// - SearchPanelViewModel.History.cs: Search/replace history management
/// A child view model, Filters, keeps the search filters: the files to include and to exclude, and their toggles.
/// </summary>
public partial class SearchPanelViewModel : ObservableObject
{
    private readonly ISearchService _searchService;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDialogService _dialogService;
    private readonly IDispatcher _dispatcher;

    // The search that fills the panel's results. Only the current search updates the results and the spinner.
    private SearchRun? _currentSearch;

    // Adds the current search's results to the list while the search runs.
    private Timer? _foundResultsTimer;
    private const int FoundResultsIntervalMs = 100;

    // Unified debounce timer for all search triggers (typing, file changes, etc.)
    private Timer? _searchDebounceTimer;
    private const int SearchDebounceDelayMs = 300;

    // True when the pending search is a refresh after a change to a searched file.
    private bool _pendingSearchIsRefresh;

    // Counts the matches in the expanded files. The count decides whether the next file found expands.
    private int _expandedMatchCount;

    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _wholeWord;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusBar))]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _searchPlaceholder = string.Empty;

    [ObservableProperty]
    private string _noResultsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusBar))]
    private bool _hasResults;

    [ObservableProperty]
    private bool _showNoResults;

    [ObservableProperty]
    private bool _isReplaceModeEnabled;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _isReplacing;

    /// <summary>
    /// True while a search runs or the results show. The status bar shows the spinner while a search runs, and the
    /// count of matches after it ends.
    /// </summary>
    public bool ShowStatusBar => IsSearching || HasResults;

    // Selection tracking for multi-select (can be either a file result or a match line)
    private ISelectableSearchItem? _selectionAnchor;

    // History collections (stores only terms, not options)
    public ObservableCollection<string> SearchHistory { get; } = new();
    public ObservableCollection<string> ReplaceHistory { get; } = new();

    // Tooltip properties
    public string MatchCaseTooltip { get; private set; } = string.Empty;

    public string WholeWordTooltip { get; private set; } = string.Empty;

    public string SearchTooltip { get; private set; } = string.Empty;

    public string CollapseAllTooltip { get; private set; } = string.Empty;

    public string ReplaceToggleTooltip { get; private set; } = string.Empty;

    public string ReplacePlaceholder { get; private set; } = string.Empty;

    public string ReplaceAllTooltip { get; private set; } = string.Empty;

    public string SearchHistoryTooltip { get; private set; } = string.Empty;

    public string ReplaceHistoryTooltip { get; private set; } = string.Empty;

    public string ClearHistoryText { get; private set; } = string.Empty;

    public string SearchingText { get; private set; } = string.Empty;

    // Cached tooltip strings for child ViewModels (avoids ServiceLocator calls per item)
    internal string ReplaceInFileTooltip { get; private set; } = string.Empty;

    internal string ReplaceMatchTooltip { get; private set; } = string.Empty;

    public ObservableCollection<SearchFileResultViewModel> FileResults { get; } = new();

    /// <summary>
    /// The files to include and to exclude, Search Only in Open Documents and Apply Search Exclude Settings.
    /// </summary>
    public SearchFiltersViewModel Filters { get; }

    /// <summary>
    /// Event raised before a refresh's results replace the list.
    /// Subscribers should save scroll position or other UI state.
    /// </summary>
    public event EventHandler? BeforeResultsRefresh;

    /// <summary>
    /// Event raised after a refresh's results replace the list.
    /// Subscribers should restore scroll position or other UI state.
    /// </summary>
    public event EventHandler? AfterResultsRefresh;

    public SearchPanelViewModel(
        ISearchService searchService,
        ICommandService commandService,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer,
        IDispatcher dispatcher)
    {
        _searchService = searchService;
        _commandService = commandService;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
        _dispatcher = dispatcher;

        TitleText = _stringLocalizer.GetString("SearchPanel_Title");
        SearchPlaceholder = _stringLocalizer.GetString("SearchPanel_Placeholder");
        NoResultsText = _stringLocalizer.GetString("SearchPanel_NoResults");

        // Tooltips
        MatchCaseTooltip = _stringLocalizer.GetString("SearchPanel_MatchCaseTooltip");
        WholeWordTooltip = _stringLocalizer.GetString("SearchPanel_WholeWordTooltip");
        SearchTooltip = _stringLocalizer.GetString("SearchPanel_SearchTooltip");
        CollapseAllTooltip = _stringLocalizer.GetString("SearchPanel_CollapseAllTooltip");
        ReplaceToggleTooltip = _stringLocalizer.GetString("SearchPanel_ReplaceToggleTooltip");
        ReplacePlaceholder = _stringLocalizer.GetString("SearchPanel_ReplacePlaceholder");
        ReplaceAllTooltip = _stringLocalizer.GetString("SearchPanel_ReplaceAllTooltip");
        SearchHistoryTooltip = _stringLocalizer.GetString("SearchPanel_SearchHistoryTooltip");
        ReplaceHistoryTooltip = _stringLocalizer.GetString("SearchPanel_ReplaceHistoryTooltip");
        ClearHistoryText = _stringLocalizer.GetString("SearchPanel_ClearHistoryText");
        SearchingText = _stringLocalizer.GetString("SearchPanel_Searching");

        // Cached tooltips for child ViewModels
        ReplaceInFileTooltip = _stringLocalizer.GetString("SearchPanel_ReplaceInFileTooltip");
        ReplaceMatchTooltip = _stringLocalizer.GetString("SearchPanel_ReplaceMatchTooltip");

        // Load saved search options from the workspace settings
        var workspaceSettings = _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;
        MatchCase = workspaceSettings.SearchMatchCase;
        WholeWord = workspaceSettings.SearchWholeWord;
        IsReplaceModeEnabled = workspaceSettings.ReplaceMode;

        // Listen for workspace loaded to load search/replace history from workspace settings
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        _messengerService.Register<DocumentOpenedMessage>(this, OnDocumentOpened);
        _messengerService.Register<DocumentClosedMessage>(this, OnDocumentClosed);

        Filters = new SearchFiltersViewModel(_workspaceWrapper, _stringLocalizer);
        Filters.Changed += OnFiltersChanged;
        Filters.SearchedFilesChanged += OnSearchedFilesChanged;
    }

    // With Search Only in Open Documents on, opening or closing a document changes the searched files.
    private void OnDocumentOpened(object recipient, DocumentOpenedMessage message)
    {
        if (Filters.SearchOnlyOpenDocuments)
        {
            ScheduleSearch(isRefresh: true);
        }
    }

    private void OnDocumentClosed(object recipient, DocumentClosedMessage message)
    {
        if (Filters.SearchOnlyOpenDocuments)
        {
            ScheduleSearch(isRefresh: true);
        }
    }

    // A filter typed into a field waits for the typing to stop, the same as the search text.
    private void OnFiltersChanged(object? sender, SearchFiltersChange change)
    {
        if (change.IsTyped)
        {
            ScheduleNewSearch();
            return;
        }

        if (!string.IsNullOrEmpty(SearchText))
        {
            _ = ExecuteSearchAsync();
        }
    }

    // Search again after a change to a searched file, whether Celbridge or another program made the change.
    private void OnSearchedFilesChanged(object? sender, EventArgs e)
    {
        ScheduleSearch(isRefresh: true);
    }

    // Clears the results at once, since the old results don't match the new search, and searches once the typing
    // stops.
    private void ScheduleNewSearch()
    {
        _searchDebounceTimer?.Dispose();
        CancelSearch();
        ClearResults();

        ScheduleSearch(isRefresh: false);
    }

    /// <summary>
    /// Schedules a debounced search operation. Multiple calls within the debounce window
    /// will reset the timer, and only the final configuration will be used.
    /// </summary>
    private void ScheduleSearch(bool isRefresh)
    {
        // Only search if there's text to search for
        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        _pendingSearchIsRefresh = isRefresh;

        // Reset debounce timer
        _searchDebounceTimer?.Dispose();
        _searchDebounceTimer = new Timer(
            _ => _dispatcher.TryEnqueue(() => _ = ExecuteSearchAsync(_pendingSearchIsRefresh)),
            null,
            SearchDebounceDelayMs,
            Timeout.Infinite);
    }

    partial void OnSearchTextChanged(string value)
    {
        ScheduleNewSearch();
    }

    partial void OnMatchCaseChanged(bool value)
    {
        // Save to the workspace settings
        _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings.SearchMatchCase = value;

        if (!string.IsNullOrEmpty(SearchText))
        {
            _ = ExecuteSearchAsync();
        }
    }

    partial void OnWholeWordChanged(bool value)
    {
        // Save to the workspace settings
        _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings.SearchWholeWord = value;

        if (!string.IsNullOrEmpty(SearchText))
        {
            _ = ExecuteSearchAsync();
        }
    }

    partial void OnIsReplaceModeEnabledChanged(bool value)
    {
        // Save to the workspace settings
        _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings.ReplaceMode = value;
    }

    [RelayCommand]
    private void ExecuteSearch()
    {
        if (!string.IsNullOrEmpty(SearchText))
        {
            _ = ExecuteSearchAsync();
        }
    }

    private const int MaxSearchResults = 1000;

    // A refresh keeps the list still while the refresh searches, and the refresh's results replace the list at the
    // end. Any other search clears the list, and adds each file's result once the search finds the file. Internal
    // for the test suite.
    internal async Task ExecuteSearchAsync(bool isRefresh = false)
    {
        // An empty list has nothing to keep still, so a refresh of an empty list also shows each result once found.
        var showsResultsAsFound = !isRefresh
            || FileResults.Count == 0;

        var search = StartSearch(showsResultsAsFound);

        try
        {
            var patternResult = CompileSearchPattern();
            if (patternResult.IsFailure)
            {
                ClearResults();
                UpdateResultsStatus();
                return;
            }
            var searchPattern = patternResult.Value;

            // A refused filter shows its own line under the fields, so the results stay empty.
            var searchOptions = Filters.CreateSearchOptions(searchPattern, MaxSearchResults);
            if (searchOptions is null)
            {
                ClearResults();
                return;
            }

            var results = await _searchService.SearchAsync(searchOptions, search.FileResultFound, search.Token);
            if (search != _currentSearch
                || results.WasCancelled)
            {
                return;
            }

            if (search.ShowsResultsAsFound)
            {
                AddFoundResults();
            }
            else
            {
                ReplaceResults(results);
            }

            UpdateResultsStatus();
            if (results.ReachedMaxResults
                && HasResults)
            {
                StatusText = _stringLocalizer.GetString("SearchPanel_StatusResultsCapped", StatusText);
            }
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled, ignore
        }
        catch (Exception)
        {
            if (search == _currentSearch)
            {
                ClearResults();
                StatusText = _stringLocalizer.GetString("SearchPanel_Error");
            }
        }
        finally
        {
            EndSearch(search);
        }
    }

    // Starts a search in place of the current one, and shows the spinner in place of the count of matches.
    private SearchRun StartSearch(bool showsResultsAsFound)
    {
        CancelSearch();

        var search = new SearchRun(showsResultsAsFound);
        _currentSearch = search;
        IsSearching = true;
        StatusText = string.Empty;

        if (showsResultsAsFound)
        {
            ClearResults();
            _foundResultsTimer = new Timer(
                _ => _dispatcher.TryEnqueue(AddFoundResults),
                null,
                FoundResultsIntervalMs,
                FoundResultsIntervalMs);
        }

        return search;
    }

    // Ends a search. The spinner goes only when the search is still the current one, so a canceled search leaves the
    // newer search's spinner showing.
    private void EndSearch(SearchRun search)
    {
        if (search == _currentSearch)
        {
            _currentSearch = null;
            StopFoundResultsTimer();
            IsSearching = false;
        }

        search.Dispose();
    }

    // Cancels the current search. Its shown results stay until the caller clears them.
    private void CancelSearch()
    {
        var search = _currentSearch;
        if (search is null)
        {
            return;
        }

        _currentSearch = null;
        StopFoundResultsTimer();
        IsSearching = false;

        search.Cancel();
    }

    private void StopFoundResultsTimer()
    {
        _foundResultsTimer?.Dispose();
        _foundResultsTimer = null;
    }

    // Adds each result found by the current search since the last call. The timer can fire after its search has
    // ended, so the method checks the current search each time. Internal for the test suite.
    internal void AddFoundResults()
    {
        var search = _currentSearch;
        if (search is null
            || !search.ShowsResultsAsFound)
        {
            return;
        }

        foreach (var fileResult in search.TakeFoundResults())
        {
            AddFileResult(fileResult);
        }
    }

    // Compiles the search text as plain text. With Match Case off, the matcher refuses text that is too long to
    // match.
    private Result<SearchPattern> CompileSearchPattern()
    {
        var patternOptions = new SearchPatternOptions(Literal: true, MatchCase, WholeWord, Multiline: false);

        return SearchPattern.Compile(SearchText, patternOptions);
    }

    // The expanded files show at most this many matches, so a file with many matches doesn't bury the files after it.
    private const int MaxExpandedMatches = 100;

    // Adds a file found by a search. The file expands while the expanded files stay within MaxExpandedMatches, and
    // the first file always expands, so the user always sees a match.
    private void AddFileResult(SearchFileResult fileResult)
    {
        var fileViewModel = new SearchFileResultViewModel(fileResult, this, _workspaceWrapper);

        if (FileResults.Count == 0
            || _expandedMatchCount + fileViewModel.MatchCount <= MaxExpandedMatches)
        {
            _expandedMatchCount += fileViewModel.MatchCount;
        }
        else
        {
            fileViewModel.IsExpanded = false;
        }

        FileResults.Add(fileViewModel);
        HasResults = true;
        ShowNoResults = false;
    }

    // Replaces the list with a refresh's results. Each file stays expanded or collapsed, and the view keeps the
    // scroll position.
    private void ReplaceResults(SearchResults results)
    {
        var expandedStates = FileResults.ToDictionary(f => f.Resource, f => f.IsExpanded);

        BeforeResultsRefresh?.Invoke(this, EventArgs.Empty);

        ClearResults();
        foreach (var fileResult in results.FileResults)
        {
            var fileViewModel = new SearchFileResultViewModel(fileResult, this, _workspaceWrapper);
            if (expandedStates.TryGetValue(fileViewModel.Resource, out var wasExpanded))
            {
                fileViewModel.IsExpanded = wasExpanded;
            }

            FileResults.Add(fileViewModel);
        }

        AfterResultsRefresh?.Invoke(this, EventArgs.Empty);
    }

    public void ClearResults()
    {
        FileResults.Clear();
        HasResults = false;
        ShowNoResults = false;
        StatusText = string.Empty;
        _selectionAnchor = null;
        _expandedMatchCount = 0;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var fileResult in FileResults)
        {
            fileResult.IsExpanded = false;
        }
    }

    [RelayCommand]
    private void ToggleReplaceMode()
    {
        IsReplaceModeEnabled = !IsReplaceModeEnabled;
    }

    [RelayCommand]
    private async Task ReplaceAll()
    {
        await ReplaceAllCoreAsync();
    }

    /// <summary>
    /// Updates the status text and visibility flags based on current results.
    /// </summary>
    private void UpdateResultsStatus()
    {
        if (FileResults.Count == 0)
        {
            HasResults = false;
            ShowNoResults = true;
            StatusText = string.Empty;
            _selectionAnchor = null;
            return;
        }

        HasResults = true;
        ShowNoResults = false;

        var totalMatches = FileResults.Sum(f => f.MatchCount);
        var totalFiles = FileResults.Count;

        var statusKey = (totalMatches == 1, totalFiles == 1) switch
        {
            (true, true) => "SearchPanel_Status_1Match1File",
            (true, false) => "SearchPanel_Status_1MatchNFiles",
            (false, true) => "SearchPanel_Status_NMatches1File",
            (false, false) => "SearchPanel_Status_NMatchesNFiles"
        };

        StatusText = _stringLocalizer.GetString(statusKey, totalMatches, totalFiles);
    }

    public void NavigateToResult(ResourceKey resource, int lineNumber, int column, int endLineNumber, int endColumn)
    {
        var location = DocumentLocation.Compose(lineNumber, column, endLineNumber, endColumn);

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resource;
            command.ForceReload = false;
            command.Location = location;
        });
    }
}
