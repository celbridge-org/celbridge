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

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        _ = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        var history = await _searchService.GetHistoryAsync();

        SearchHistory.Clear();
        foreach (var term in history.SearchTerms)
        {
            SearchHistory.Add(term);
        }

        ReplaceHistory.Clear();
        foreach (var term in history.ReplaceTerms)
        {
            ReplaceHistory.Add(term);
        }

        // Restore the most recent search/replace terms (options are already restored from EditorSettings)
        if (history.SearchTerms.Count > 0)
        {
            SearchText = history.SearchTerms[0];
        }

        if (history.ReplaceTerms.Count > 0)
        {
            ReplaceText = history.ReplaceTerms[0];
        }
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

    /// <summary>
    /// Handles selection of a match line item with support for Ctrl and Shift modifiers.
    /// </summary>
    public void SelectMatchLine(SearchMatchLineViewModel matchLine, bool isCtrlPressed, bool isShiftPressed)
    {
        if (isCtrlPressed)
        {
            // Toggle selection of the clicked item
            matchLine.IsSelected = !matchLine.IsSelected;
            if (matchLine.IsSelected)
            {
                _selectionAnchor = matchLine;
            }
        }
        else if (isShiftPressed && _selectionAnchor != null)
        {
            // Range selection from anchor to clicked item
            SelectRange(_selectionAnchor, matchLine);
        }
        else
        {
            // Single selection - clear all others and select this one
            ClearAllSelections();
            matchLine.IsSelected = true;
            _selectionAnchor = matchLine;
        }
    }

    /// <summary>
    /// Handles selection of a file result item with support for Ctrl and Shift modifiers.
    /// </summary>
    public void SelectFileResult(SearchFileResultViewModel fileResult, bool isCtrlPressed, bool isShiftPressed)
    {
        if (isCtrlPressed)
        {
            // Toggle selection of the clicked item
            fileResult.IsSelected = !fileResult.IsSelected;
            if (fileResult.IsSelected)
            {
                _selectionAnchor = fileResult;
            }
        }
        else if (isShiftPressed && _selectionAnchor != null)
        {
            // Range selection from anchor to clicked item
            SelectRange(_selectionAnchor, fileResult);
        }
        else
        {
            // Single selection - clear all others and select this one
            ClearAllSelections();
            fileResult.IsSelected = true;
            _selectionAnchor = fileResult;
        }
    }

    private void ClearAllSelections()
    {
        foreach (var fileResult in FileResults)
        {
            fileResult.IsSelected = false;
            foreach (var match in fileResult.Matches)
            {
                match.IsSelected = false;
            }
        }
    }

    private void SelectRange(ISelectableSearchItem from, ISelectableSearchItem to)
    {
        // Build a flat list of all selectable items (file headers and match lines)
        var allItems = new List<ISelectableSearchItem>();
        foreach (var fileResult in FileResults)
        {
            allItems.Add(fileResult);
            foreach (var match in fileResult.Matches)
            {
                allItems.Add(match);
            }
        }

        var fromIndex = allItems.IndexOf(from);
        var toIndex = allItems.IndexOf(to);

        if (fromIndex == -1 || toIndex == -1)
        {
            return;
        }

        // Ensure fromIndex <= toIndex
        if (fromIndex > toIndex)
        {
            (fromIndex, toIndex) = (toIndex, fromIndex);
        }

        // Clear existing selection and select the range
        ClearAllSelections();
        for (var i = fromIndex; i <= toIndex; i++)
        {
            allItems[i].IsSelected = true;
        }
    }

    /// <summary>
    /// Gets all currently selected match lines.
    /// </summary>
    public List<SearchMatchLineViewModel> GetSelectedMatches()
    {
        return FileResults
            .SelectMany(f => f.Matches)
            .Where(m => m.IsSelected)
            .ToList();
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

    /// <summary>
    /// Selects a search history entry, populating the search text.
    /// </summary>
    public void SelectSearchHistoryEntry(string term)
    {
        SearchText = term;

        // Trigger a search - no need to save since it's already in history
        _ = ExecuteSearchAsync();
    }

    /// <summary>
    /// Selects a replace history entry, populating the replace text.
    /// </summary>
    public void SelectReplaceHistoryEntry(string term)
    {
        ReplaceText = term;
    }

    [RelayCommand]
    private async Task ClearSearchHistory()
    {
        await _searchService.ClearHistoryAsync();
        SearchHistory.Clear();
        ReplaceHistory.Clear();
    }

    /// <summary>
    /// Saves the current search term to history.
    /// Called when the search textbox loses focus.
    /// </summary>
    public void SaveSearchTermToHistory()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        // Fire and forget - don't block the UI
        _ = SaveSearchTermToHistoryAsync();
    }

    private async Task SaveSearchTermToHistoryAsync()
    {
        await _searchService.AddSearchTermToHistoryAsync(SearchText);

        // Update the observable collection on the UI thread
        _dispatcherQueue.TryEnqueue(() =>
        {
            var existingIndex = SearchHistory.IndexOf(SearchText);
            if (existingIndex >= 0)
            {
                SearchHistory.RemoveAt(existingIndex);
            }

            SearchHistory.Insert(0, SearchText);
        });
    }

    /// <summary>
    /// Saves the current replace term to history.
    /// Called when the replace textbox loses focus.
    /// </summary>
    public void SaveReplaceTermToHistory()
    {
        if (string.IsNullOrWhiteSpace(ReplaceText))
        {
            return;
        }

        // Fire and forget - don't block the UI
        _ = SaveReplaceTermToHistoryAsync();
    }

    private async Task SaveReplaceTermToHistoryAsync()
    {
        await _searchService.AddReplaceTermToHistoryAsync(ReplaceText);

        // Update the observable collection on the UI thread
        _dispatcherQueue.TryEnqueue(() =>
        {
            var existingIndex = ReplaceHistory.IndexOf(ReplaceText);
            if (existingIndex >= 0)
            {
                ReplaceHistory.RemoveAt(existingIndex);
            }

            ReplaceHistory.Insert(0, ReplaceText);
        });
    }

    public async Task ReplaceInFileAsync(SearchFileResultViewModel fileResult)
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        IsReplacing = true;

        try
        {
            // Build edit list from all matches in the file
            // Process matches in reverse order to maintain correct positions
            var textEdits = fileResult.Matches
                .OrderByDescending(m => m.LineNumber)
                .ThenByDescending(m => m.OriginalMatchStart)
                .Select(m => new TextEdit(
                    Line: m.LineNumber,
                    Column: m.OriginalMatchStart + 1, // Convert to 1-based column
                    EndLine: m.LineNumber,
                    EndColumn: m.OriginalMatchStart + 1 + SearchText.Length,
                    NewText: ReplaceText))
                .ToList();

            var documentEdit = new DocumentEdit(fileResult.Resource, textEdits);
            var documentEdits = new List<DocumentEdit> { documentEdit };

            // Execute the ApplyEditsCommand to apply edits via Monaco (supports undo)
            // Using ExecuteAsync to wait for completion so documents are opened before updating UI
            await _commandService.ExecuteAsync<IApplyEditsCommand>(command =>
            {
                command.Edits = documentEdits;
            });

            // Save replace term to history (fire and forget)
            _ = SaveReplaceTermToHistoryAsync();

            // Remove the file from results since all its matches are replaced
            FileResults.Remove(fileResult);
            UpdateResultsStatus();
        }
        finally
        {
            IsReplacing = false;
        }
    }

    public async Task ReplaceMatchAsync(SearchMatchLineViewModel matchLine)
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        // If the clicked item is selected and there are multiple selections, replace all selected
        var selectedMatches = GetSelectedMatches();
        if (matchLine.IsSelected && selectedMatches.Count > 1)
        {
            await ReplaceSelectedMatchesAsync(selectedMatches);
            return;
        }

        // Save replace term to history (fire and forget)
        _ = SaveReplaceTermToHistoryAsync();

        IsReplacing = true;

        try
        {
            var fileResult = matchLine.Parent;

            // Build a single edit for this match
            var textEdit = new TextEdit(
                Line: matchLine.LineNumber,
                Column: matchLine.OriginalMatchStart + 1, // Convert to 1-based column
                EndLine: matchLine.LineNumber,
                EndColumn: matchLine.OriginalMatchStart + 1 + SearchText.Length,
                NewText: ReplaceText);

            var documentEdit = new DocumentEdit(fileResult.Resource, new List<TextEdit> { textEdit });
            var documentEdits = new List<DocumentEdit> { documentEdit };

            // Execute the ApplyEditsCommand to apply edits via Monaco (supports undo)
            // Using ExecuteAsync to wait for completion so documents are opened before updating UI
            await _commandService.ExecuteAsync<IApplyEditsCommand>(command =>
            {
                command.Edits = documentEdits;
            });

            // Remove this match from the file's results
            fileResult.RemoveMatch(matchLine);

            // If the file has no more matches, remove it from results
            if (fileResult.MatchCount == 0)
            {
                FileResults.Remove(fileResult);
            }

            UpdateResultsStatus();
        }
        finally
        {
            IsReplacing = false;
        }
    }

    private async Task ReplaceSelectedMatchesAsync(List<SearchMatchLineViewModel> selectedMatches)
    {
        if (selectedMatches.Count == 0)
        {
            return;
        }

        IsReplacing = true;

        try
        {
            // Group selected matches by file
            var matchesByFile = selectedMatches
                .GroupBy(m => m.Parent.Resource)
                .ToList();

            var documentEdits = new List<DocumentEdit>();

            foreach (var fileGroup in matchesByFile)
            {
                // Process matches in reverse order to maintain correct positions
                var textEdits = fileGroup
                    .OrderByDescending(m => m.LineNumber)
                    .ThenByDescending(m => m.OriginalMatchStart)
                    .Select(m => new TextEdit(
                        Line: m.LineNumber,
                        Column: m.OriginalMatchStart + 1, // Convert to 1-based column
                        EndLine: m.LineNumber,
                        EndColumn: m.OriginalMatchStart + 1 + SearchText.Length,
                        NewText: ReplaceText))
                    .ToList();

                var documentEdit = new DocumentEdit(fileGroup.Key, textEdits);
                documentEdits.Add(documentEdit);
            }

            // Execute the ApplyEditsCommand to apply all edits via Monaco (supports undo)
            // Using ExecuteAsync to wait for completion so documents are opened before updating UI
            await _commandService.ExecuteAsync<IApplyEditsCommand>(command =>
            {
                command.Edits = documentEdits;
            });

            // Remove replaced matches from results
            foreach (var match in selectedMatches)
            {
                var fileResult = match.Parent;
                fileResult.RemoveMatch(match);

                // If the file has no more matches, remove it from results
                if (fileResult.MatchCount == 0)
                {
                    FileResults.Remove(fileResult);
                }
            }

            UpdateResultsStatus();
        }
        finally
        {
            IsReplacing = false;
        }
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

    [RelayCommand]
    private async Task ReplaceAll()
    {
        if (string.IsNullOrEmpty(SearchText) || FileResults.Count == 0)
        {
            return;
        }

        IsReplacing = true;

        var progressTitle = _stringLocalizer.GetString("SearchPanel_ReplaceAllProgress");
        var progressToken = _dialogService.AcquireProgressDialog(progressTitle);

        try
        {
            // Re-run search WITHOUT limits to get ALL matches in the project
            // This ensures Replace All replaces everything, not just displayed results
            var allResults = await _searchService.SearchAsync(
                SearchText,
                MatchCase,
                WholeWord,
                maxResults: null,
                CancellationToken.None);

            if (allResults.TotalMatches == 0)
            {
                return;
            }

            // Build the confirmation message with the ACTUAL total count
            var totalMatches = allResults.TotalMatches;
            var totalFiles = allResults.TotalFiles;
            var titleText = _stringLocalizer.GetString("SearchPanel_ReplaceAllConfirmTitle");
            var messageText = _stringLocalizer.GetString(
                "SearchPanel_ReplaceAllConfirmMessage",
                totalMatches,
                totalFiles);

            // Hide progress dialog temporarily to show confirmation
            progressToken.Dispose();

            var confirmResult = await _dialogService.ShowConfirmationDialogAsync(titleText, messageText);
            if (!confirmResult.IsSuccess || !confirmResult.Value)
            {
                return;
            }

            // Re-acquire progress dialog for the actual replacement
            progressToken = _dialogService.AcquireProgressDialog(progressTitle);

            // Build edit list from ALL search results (not just displayed)
            // Process matches in reverse order within each file to maintain correct positions
            var documentEdits = new List<DocumentEdit>();

            foreach (var fileResult in allResults.FileResults)
            {
                var textEdits = fileResult.Matches
                    .OrderByDescending(m => m.LineNumber)
                    .ThenByDescending(m => m.OriginalMatchStart)
                    .Select(m => new TextEdit(
                        Line: m.LineNumber,
                        Column: m.OriginalMatchStart + 1, // Convert to 1-based column
                        EndLine: m.LineNumber,
                        EndColumn: m.OriginalMatchStart + 1 + SearchText.Length,
                        NewText: ReplaceText))
                    .ToList();

                var documentEdit = new DocumentEdit(fileResult.Resource, textEdits);
                documentEdits.Add(documentEdit);
            }

            // Execute the ApplyEditsCommand to apply all edits via Monaco (supports undo)
            // Using ExecuteAsync to wait for completion so documents are opened before clearing results
            await _commandService.ExecuteAsync<IApplyEditsCommand>(command =>
            {
                command.Edits = documentEdits;
            });

            // Save replace term to history (fire and forget)
            _ = SaveReplaceTermToHistoryAsync();

            // Clear all results since all matches are replaced
            FileResults.Clear();
            UpdateResultsStatus();
        }
        finally
        {
            progressToken.Dispose();
            IsReplacing = false;
        }
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
