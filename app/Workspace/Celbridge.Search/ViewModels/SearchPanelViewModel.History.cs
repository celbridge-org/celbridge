using Celbridge.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.Search.ViewModels;

/// <summary>
/// Contains search and replace history management.
/// </summary>
public partial class SearchPanelViewModel
{
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

        if (history.SearchTerms.Count > 0)
        {
            SearchText = history.SearchTerms[0];
        }

        if (history.ReplaceTerms.Count > 0)
        {
            ReplaceText = history.ReplaceTerms[0];
        }
    }

    /// <summary>
    /// Selects a search history entry, populating the search text.
    /// </summary>
    public void SelectSearchHistoryEntry(string term)
    {
        SearchText = term;
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

        _ = SaveSearchTermToHistoryAsync();
    }

    private async Task SaveSearchTermToHistoryAsync()
    {
        await _searchService.AddSearchTermToHistoryAsync(SearchText);

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

        _ = SaveReplaceTermToHistoryAsync();
    }

    private async Task SaveReplaceTermToHistoryAsync()
    {
        await _searchService.AddReplaceTermToHistoryAsync(ReplaceText);

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
}
