using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Celbridge.Explorer.ViewModels;

public partial class SearchPanelViewModel : ObservableObject
{
    private readonly ISearchService _searchService;
    private readonly ICommandService _commandService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly DispatcherQueue _dispatcherQueue;

    private CancellationTokenSource? _searchCancellationTokenSource;
    private readonly object _searchLock = new();

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

    // Tooltip properties
    public string MatchCaseTooltip { get; private set; } = string.Empty;

    public string WholeWordTooltip { get; private set; } = string.Empty;

    public string SearchTooltip { get; private set; } = string.Empty;

    public ObservableCollection<SearchFileResultViewModel> FileResults { get; } = new();

    public SearchPanelViewModel(
        ISearchService searchService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _searchService = searchService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        TitleText = _stringLocalizer.GetString("SearchPanel_Title");
        SearchPlaceholder = _stringLocalizer.GetString("SearchPanel_Placeholder");
        NoResultsText = _stringLocalizer.GetString("SearchPanel_NoResults");

        // Tooltips
        MatchCaseTooltip = _stringLocalizer.GetString("SearchPanel_MatchCaseTooltip");
        WholeWordTooltip = _stringLocalizer.GetString("SearchPanel_WholeWordTooltip");
        SearchTooltip = _stringLocalizer.GetString("SearchPanel_SearchTooltip");
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
        if (!string.IsNullOrEmpty(SearchText))
        {
            _ = ExecuteSearchAsync();
        }
    }

    partial void OnWholeWordChanged(bool value)
    {
        if (!string.IsNullOrEmpty(SearchText))
        {
            _ = ExecuteSearchAsync();
        }
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

        // Update status text
        var matchWord = results.TotalMatches == 1 
            ? _stringLocalizer.GetString("SearchPanel_Match") 
            : _stringLocalizer.GetString("SearchPanel_Matches");
        var fileWord = results.TotalFiles == 1 
            ? _stringLocalizer.GetString("SearchPanel_File") 
            : _stringLocalizer.GetString("SearchPanel_Files");

        StatusText = $"{results.TotalMatches} {matchWord} {_stringLocalizer.GetString("SearchPanel_In")} {results.TotalFiles} {fileWord}";

        if (results.ReachedMaxResults)
        {
            StatusText += $" ({_stringLocalizer.GetString("SearchPanel_ResultsCapped")})";
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

public partial class SearchFileResultViewModel : ObservableObject
{
    internal readonly SearchPanelViewModel Parent;

    public ResourceKey Resource { get; }
    public string FileName { get; }
    public string RelativePath { get; }
    public int MatchCount { get; }
    public FileIconDefinition FileIcon { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<SearchMatchLineViewModel> Matches { get; } = new();

    public SearchFileResultViewModel(SearchFileResult result, SearchPanelViewModel parent, IWorkspaceWrapper workspaceWrapper)
    {
        Parent = parent;
        Resource = result.Resource;
        FileName = result.FileName;
        RelativePath = result.RelativePath;
        MatchCount = result.Matches.Count;

        // Get the file icon from the explorer service
        var explorerService = workspaceWrapper.WorkspaceService.ExplorerService;
        FileIcon = explorerService.GetIconForResource(result.Resource);

        foreach (var match in result.Matches)
        {
            Matches.Add(new SearchMatchLineViewModel(match, this));
        }
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void NavigateToFile()
    {
        if (Matches.Count > 0)
        {
            var firstMatch = Matches[0];
            Parent.NavigateToResult(Resource, firstMatch.LineNumber, firstMatch.OriginalMatchStart + 1);
        }
    }
}

public partial class SearchMatchLineViewModel : ObservableObject
{
    private readonly SearchFileResultViewModel _parent;

    public int LineNumber { get; }
    
    /// <summary>
    /// The full context line text (used for tooltip).
    /// </summary>
    public string LineText { get; }
    
    /// <summary>
    /// Text before the match (for display).
    /// </summary>
    public string TextBeforeMatch { get; }
    
    /// <summary>
    /// The matched text (for highlighted display).
    /// </summary>
    public string MatchedText { get; }
    
    /// <summary>
    /// Text after the match (for display).
    /// </summary>
    public string TextAfterMatch { get; }
    
    /// <summary>
    /// The position where the match starts in the display text.
    /// </summary>
    public int MatchStart { get; }
    
    /// <summary>
    /// The length of the match.
    /// </summary>
    public int MatchLength { get; }

    /// <summary>
    /// The position where the match starts in the original unformatted line (0-based).
    /// This is used for navigation to the correct column in the editor.
    /// </summary>
    public int OriginalMatchStart { get; }

    public SearchMatchLineViewModel(SearchMatchLine match, SearchFileResultViewModel parent)
    {
        _parent = parent;
        LineNumber = match.LineNumber;
        LineText = match.LineText;
        MatchStart = match.MatchStart;
        MatchLength = match.MatchLength;
        OriginalMatchStart = match.OriginalMatchStart;
        
        // Split the line text into before, match, and after segments for highlighting
        var displayText = match.LineText;
        var matchStart = match.MatchStart;
        var matchLength = match.MatchLength;
        
        // Ensure bounds are valid
        if (matchStart >= 0 && matchStart < displayText.Length)
        {
            var matchEnd = Math.Min(matchStart + matchLength, displayText.Length);
            
            TextBeforeMatch = displayText.Substring(0, matchStart);
            MatchedText = displayText.Substring(matchStart, matchEnd - matchStart);
            TextAfterMatch = matchEnd < displayText.Length ? displayText.Substring(matchEnd) : string.Empty;
        }
        else
        {
            // Fallback if match position is invalid
            TextBeforeMatch = displayText;
            MatchedText = string.Empty;
            TextAfterMatch = string.Empty;
        }
    }

    [RelayCommand]
    private void Navigate()
    {
        // Navigate to the line and column position of the match
        // Use OriginalMatchStart (0-based) + 1 to get the 1-based column position for Monaco
        _parent.Parent.NavigateToResult(_parent.Resource, LineNumber, OriginalMatchStart + 1);
    }
}
