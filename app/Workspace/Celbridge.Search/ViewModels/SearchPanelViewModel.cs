using System.Collections.ObjectModel;
using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents;
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
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IEditorSettings _editorSettings;
    private readonly IDialogService _dialogService;
    private readonly DispatcherQueue _dispatcherQueue;

    private CancellationTokenSource? _searchCancellationTokenSource;
    private readonly Lock _searchLock = new();

    // Debounce timer
    private Timer? _debounceTimer;
    private const int DebounceDelayMs = 300;

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

    // Tooltip properties
    public string MatchCaseTooltip { get; private set; } = string.Empty;

    public string WholeWordTooltip { get; private set; } = string.Empty;

    public string SearchTooltip { get; private set; } = string.Empty;

    public string CollapseAllTooltip { get; private set; } = string.Empty;

    public string ReplaceToggleTooltip { get; private set; } = string.Empty;

    public string ReplacePlaceholder { get; private set; } = string.Empty;

    public string ReplaceAllTooltip { get; private set; } = string.Empty;

    public ObservableCollection<SearchFileResultViewModel> FileResults { get; } = new();

    public SearchPanelViewModel(
        ISearchService searchService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IEditorSettings editorSettings,
        IDialogService dialogService)
    {
        _searchService = searchService;
        _commandService = commandService;
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

        // Load saved search options from editor settings
        MatchCase = _editorSettings.SearchMatchCase;
        WholeWord = _editorSettings.SearchWholeWord;
        IsReplaceModeEnabled = _editorSettings.ReplaceMode;
    }

    partial void OnSearchTextChanged(string value)
    {
        // Cancel any pending debounce timer
        _debounceTimer?.Dispose();

        if (string.IsNullOrEmpty(value))
        {
            ClearResults();
            return;
        }

        // Start debounce timer - callback runs on thread pool, so we need to dispatch to UI thread
        _debounceTimer = new Timer(
            _ => _dispatcherQueue.TryEnqueue(() => _ = ExecuteSearchAsync()),
            null,
            DebounceDelayMs,
            Timeout.Infinite);
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

    private async Task ExecuteSearchAsync()
    {
        // Cancel any in-progress search
        lock (_searchLock)
        {
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource = new CancellationTokenSource();
        }

        var cancellationToken = _searchCancellationTokenSource!.Token;

        try
        {
            IsSearching = true;
            StatusText = _stringLocalizer.GetString("SearchPanel_Searching");

            var results = await _searchService.SearchAsync(
                SearchText,
                MatchCase,
                WholeWord,
                cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Update UI on UI thread
            UpdateResults(results);
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

    private void UpdateResults(SearchResults results)
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

        foreach (var fileResult in results.FileResults)
        {
            var fileVm = new SearchFileResultViewModel(fileResult, this, _workspaceWrapper);
            FileResults.Add(fileVm);
        }

        // Update status text using localized format strings
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
    }

    public void ClearResults()
    {
        FileResults.Clear();
        HasResults = false;
        ShowNoResults = false;
        StatusText = string.Empty;
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

    public async Task ReplaceInFileAsync(SearchFileResultViewModel fileResult)
    {
        if (string.IsNullOrEmpty(SearchText))
        {
            return;
        }

        IsReplacing = true;

        try
        {
            var result = await _searchService.ReplaceInFileAsync(
                fileResult.Resource,
                SearchText,
                ReplaceText,
                MatchCase,
                WholeWord,
                CancellationToken.None);

            if (result.Success && result.ReplacementsCount > 0)
            {
                // Refresh search results to reflect the changes
                // The ResourceMonitor will handle notifying open documents about file changes
                await ExecuteSearchAsync();
            }
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

        IsReplacing = true;

        try
        {
            var fileResult = matchLine.Parent;
            var result = await _searchService.ReplaceMatchAsync(
                fileResult.Resource,
                SearchText,
                ReplaceText,
                matchLine.LineNumber,
                matchLine.OriginalMatchStart,
                MatchCase,
                WholeWord,
                CancellationToken.None);

            if (result.Success)
            {
                // Refresh search results to reflect the changes
                await ExecuteSearchAsync();
            }
        }
        finally
        {
            IsReplacing = false;
        }
    }

    [RelayCommand]
    private async Task ReplaceAll()
    {
        if (string.IsNullOrEmpty(SearchText) || FileResults.Count == 0)
        {
            return;
        }

        // Build the confirmation message
        var totalMatches = FileResults.Sum(f => f.MatchCount);
        var totalFiles = FileResults.Count;
        var titleText = _stringLocalizer.GetString("SearchPanel_ReplaceAllConfirmTitle");
        var messageText = _stringLocalizer.GetString(
            "SearchPanel_ReplaceAllConfirmMessage",
            totalMatches,
            totalFiles);

        var confirmResult = await _dialogService.ShowConfirmationDialogAsync(titleText, messageText);
        if (!confirmResult.IsSuccess || !confirmResult.Value)
        {
            return;
        }

        IsReplacing = true;

        // Snapshot the current file results as data for the replace operation
        var fileResultsData = FileResults
            .Select(f => new SearchFileResult(
                f.Resource,
                f.FileName,
                f.RelativePath,
                f.Matches.Select(m => new SearchMatchLine(
                    m.LineNumber,
                    m.LineText,
                    m.MatchStart,
                    m.MatchLength,
                    m.OriginalMatchStart)).ToList()))
            .ToList();

        var progressTitle = _stringLocalizer.GetString("SearchPanel_ReplaceAllProgress");
        var progressToken = _dialogService.AcquireProgressDialog(progressTitle);

        try
        {
            var result = await _searchService.ReplaceAllAsync(
                fileResultsData,
                SearchText,
                ReplaceText,
                MatchCase,
                WholeWord,
                CancellationToken.None);

            // Refresh search results to reflect the changes
            await ExecuteSearchAsync();
        }
        finally
        {
            progressToken.Dispose();
            IsReplacing = false;
        }
    }

    public void NavigateToResult(ResourceKey resource, int lineNumber, int column)
    {
        // Create location JSON for text document navigation
        var location = JsonSerializer.Serialize(new { lineNumber, column });

        // Open the document and navigate to the specific location
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resource;
            command.ForceReload = false;
            command.Location = location;
        });
    }
}
