using System.Collections.ObjectModel;
using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Dispatching;

namespace Celbridge.Search.ViewModels;

/// <summary>
/// ViewModel for the search panel. Split into partial classes for maintainability:
/// - SearchPanelViewModel.cs (this file): Core search logic and UI state
/// - SearchPanelViewModel.Replace.cs: Replace operations
/// - SearchPanelViewModel.Selection.cs: Multi-select handling
/// - SearchPanelViewModel.History.cs: Search/replace history management
/// </summary>
public partial class SearchPanelViewModel : ObservableObject
{
    private readonly ISearchService _searchService;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IEditorSettings _editorSettings;
    private readonly IDialogService _dialogService;
    private readonly DispatcherQueue _dispatcherQueue;

    private CancellationTokenSource? _searchCancellationTokenSource;
    private readonly Lock _searchLock = new();

    // Unified debounce timer for all search triggers (typing, file changes, etc.)
    private Timer? _searchDebounceTimer;
    private const int SearchDebounceDelayMs = 300;

    // Flags for pending search behavior
    private bool _pendingSearchPreserveExpandedState;
    private bool _pendingSearchRaiseRefreshEvents;

    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _wholeWord;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _searchPlaceholder = string.Empty;

    [ObservableProperty]
    private string _noResultsText = string.Empty;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _showNoResults;

    [ObservableProperty]
    private bool _isReplaceModeEnabled;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _isReplacing;

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

    // Cached tooltip strings for child ViewModels (avoids ServiceLocator calls per item)
    internal string ReplaceInFileTooltip { get; private set; } = string.Empty;

    internal string ReplaceMatchTooltip { get; private set; } = string.Empty;

    public ObservableCollection<SearchFileResultViewModel> FileResults { get; } = new();

    /// <summary>
    /// Event raised before search results are refreshed.
    /// Subscribers should save scroll position or other UI state.
    /// </summary>
    public event EventHandler? BeforeResultsRefresh;

    /// <summary>
    /// Event raised after search results are refreshed.
    /// Subscribers should restore scroll position or other UI state.
    /// </summary>
    public event EventHandler? AfterResultsRefresh;

    public SearchPanelViewModel(
        ISearchService searchService,
        ICommandService commandService,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IEditorSettings editorSettings,
        IDialogService dialogService)
    {
        _searchService = searchService;
        _commandService = commandService;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _editorSettings = editorSettings;
        _dialogService = dialogService;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

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

        // Cached tooltips for child ViewModels
        ReplaceInFileTooltip = _stringLocalizer.GetString("SearchPanel_ReplaceInFileTooltip");
        ReplaceMatchTooltip = _stringLocalizer.GetString("SearchPanel_ReplaceMatchTooltip");

        // Load saved search options from editor settings
        MatchCase = _editorSettings.SearchMatchCase;
        WholeWord = _editorSettings.SearchWholeWord;
        IsReplaceModeEnabled = _editorSettings.ReplaceMode;

        // Listen for workspace loaded to load search/replace history from workspace settings
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);

        // Listen for file system changes to refresh search results
        // This catches all modifications: user edits, external editors, scripts, agents, etc.
        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnResourceChanged);
        _messengerService.Register<MonitoredResourceCreatedMessage>(this, OnResourceCreated);
        _messengerService.Register<MonitoredResourceDeletedMessage>(this, OnResourceDeleted);
        _messengerService.Register<MonitoredResourceRenamedMessage>(this, OnResourceRenamed);
    }

    private void OnResourceChanged(object recipient, MonitoredResourceChangedMessage message)
    {
        ScheduleSearch(preserveExpandedState: true, raiseRefreshEvents: true);
    }

    private void OnResourceCreated(object recipient, MonitoredResourceCreatedMessage message)
    {
        ScheduleSearch(preserveExpandedState: true, raiseRefreshEvents: true);
    }

    private void OnResourceDeleted(object recipient, MonitoredResourceDeletedMessage message)
    {
        ScheduleSearch(preserveExpandedState: true, raiseRefreshEvents: true);
    }

    private void OnResourceRenamed(object recipient, MonitoredResourceRenamedMessage message)
    {
        ScheduleSearch(preserveExpandedState: true, raiseRefreshEvents: true);
    }

    /// <summary>
    /// Schedules a debounced search operation. Multiple calls within the debounce window
    /// will reset the timer, and only the final configuration will be used.
    /// </summary>
    private void ScheduleSearch(bool preserveExpandedState, bool raiseRefreshEvents)
    {
        // Only search if there's text to search for
        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        // Store behavior flags for when the search executes
        _pendingSearchPreserveExpandedState = preserveExpandedState;
        _pendingSearchRaiseRefreshEvents = raiseRefreshEvents;

        // Reset debounce timer
        _searchDebounceTimer?.Dispose();
        _searchDebounceTimer = new Timer(
            _ => _dispatcherQueue.TryEnqueue(() => _ = ExecutePendingSearchAsync()),
            null,
            SearchDebounceDelayMs,
            Timeout.Infinite);
    }

    private async Task ExecutePendingSearchAsync()
    {
        if (_pendingSearchRaiseRefreshEvents)
        {
            BeforeResultsRefresh?.Invoke(this, EventArgs.Empty);
        }

        await ExecuteSearchAsync(preserveExpandedState: _pendingSearchPreserveExpandedState);

        if (_pendingSearchRaiseRefreshEvents)
        {
            AfterResultsRefresh?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        // Cancel any pending debounce timer
        _searchDebounceTimer?.Dispose();

        if (string.IsNullOrEmpty(value))
        {
            ClearResults();
            return;
        }

        // Schedule search without preserving state (user is typing a new query)
        ScheduleSearch(preserveExpandedState: false, raiseRefreshEvents: false);
    }

    partial void OnMatchCaseChanged(bool value)
    {
        // Save to editor settings
        _editorSettings.SearchMatchCase = value;

        if (!string.IsNullOrEmpty(SearchText))
        {
            _ = ExecuteSearchAsync();
        }
    }

    partial void OnWholeWordChanged(bool value)
    {
        // Save to editor settings
        _editorSettings.SearchWholeWord = value;

        if (!string.IsNullOrEmpty(SearchText))
        {
            _ = ExecuteSearchAsync();
        }
    }

    partial void OnIsReplaceModeEnabledChanged(bool value)
    {
        // Save to editor settings
        _editorSettings.ReplaceMode = value;
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

    private async Task ExecuteSearchAsync(bool preserveExpandedState = false)
    {
        // Cancel any in-progress search
        lock (_searchLock)
        {
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource = new CancellationTokenSource();
        }

        var cancellationToken = _searchCancellationTokenSource!.Token;

        // Capture current expanded state if preserving
        Dictionary<ResourceKey, bool>? expandedStates = null;
        if (preserveExpandedState && FileResults.Count > 0)
        {
            expandedStates = FileResults.ToDictionary(f => f.Resource, f => f.IsExpanded);
        }

        try
        {
            IsSearching = true;
            StatusText = _stringLocalizer.GetString("SearchPanel_Searching");

            var results = await _searchService.SearchAsync(
                SearchText,
                MatchCase,
                WholeWord,
                maxResults: MaxSearchResults,
                cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Update UI with results
            UpdateResults(results, expandedStates);
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled, ignore
        }
        catch (Exception)
        {
            ClearResults();
            StatusText = _stringLocalizer.GetString("SearchPanel_Error");
        }
        finally
        {
            IsSearching = false;
        }
    }

    // When total matches exceed this threshold, use smart collapse behavior
    private const int CollapseThreshold = 100;

    // Maximum number of matches to show expanded when auto-collapsing
    private const int MaxExpandedMatches = 50;

    private void UpdateResults(
        SearchResults results,
        Dictionary<ResourceKey, bool>? expandedStates)
    {
        FileResults.Clear();

        if (results.TotalMatches == 0)
        {
            HasResults = false;
            ShowNoResults = true;
            StatusText = string.Empty;
            return;
        }

        ShowNoResults = false;
        HasResults = true;

        // Update status immediately so user sees feedback
        var statusKey = (results.TotalMatches == 1, results.TotalFiles == 1) switch
        {
            (true, true) => "SearchPanel_Status_1Match1File",
            (true, false) => "SearchPanel_Status_1MatchNFiles",
            (false, true) => "SearchPanel_Status_NMatches1File",
            (false, false) => "SearchPanel_Status_NMatchesNFiles"
        };
        StatusText = _stringLocalizer.GetString(statusKey, results.TotalMatches, results.TotalFiles);

        if (results.ReachedMaxResults)
        {
            StatusText = _stringLocalizer.GetString("SearchPanel_StatusResultsCapped", StatusText);
        }

        // When there are many matches and no previous state to restore,
        // expand first files up to a limit, then collapse the rest
        var useSmartCollapse = expandedStates == null && results.TotalMatches > CollapseThreshold;
        var expandedMatchCount = 0;
        var isFirstFile = true;

        foreach (var fileResult in results.FileResults)
        {
            var fileVm = new SearchFileResultViewModel(fileResult, this, _workspaceWrapper);

            // Restore expanded state if available
            if (expandedStates != null && expandedStates.TryGetValue(fileVm.Resource, out var wasExpanded))
            {
                fileVm.IsExpanded = wasExpanded;
            }
            else if (useSmartCollapse)
            {
                // Always expand the first file to guarantee visual feedback.
                // For subsequent files, only expand if it keeps us under the limit.
                if (isFirstFile || expandedMatchCount + fileVm.MatchCount <= MaxExpandedMatches)
                {
                    fileVm.IsExpanded = true;
                    expandedMatchCount += fileVm.MatchCount;
                }
                else
                {
                    fileVm.IsExpanded = false;
                }
            }

            isFirstFile = false;
            FileResults.Add(fileVm);
        }
    }

    public void ClearResults()
    {
        FileResults.Clear();
        HasResults = false;
        ShowNoResults = false;
        StatusText = string.Empty;
        _selectionAnchor = null;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        ClearResults();
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
        // Ensure the selection range is valid (end should not be before start)
        if (endLineNumber > 0)
        {
            // Swap if end is before start
            if (endLineNumber < lineNumber || (endLineNumber == lineNumber && endColumn < column))
            {
                (lineNumber, endLineNumber) = (endLineNumber, lineNumber);
                (column, endColumn) = (endColumn, column);
            }
        }

        // Create location JSON for text document navigation
        var location = JsonSerializer.Serialize(new { lineNumber, column, endLineNumber, endColumn });

        // Open the document and navigate to the specific location
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resource;
            command.ForceReload = false;
            command.Location = location;
        });
    }
}
