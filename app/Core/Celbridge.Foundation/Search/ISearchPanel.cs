namespace Celbridge.Search;

/// <summary>
/// Interface for interacting with the Search Panel view.
/// </summary>
public interface ISearchPanel
{
    /// <summary>
    /// Sets focus to the search input field.
    /// </summary>
    void FocusSearchInput();

    /// <summary>
    /// Sets the search text and triggers a search.
    /// </summary>
    void SetSearchText(string searchText);

    /// <summary>
    /// Sets the match case option.
    /// </summary>
    void SetMatchCase(bool matchCase);

    /// <summary>
    /// Sets the whole word option.
    /// </summary>
    void SetWholeWord(bool wholeWord);

    /// <summary>
    /// Enables or disables replace mode.
    /// </summary>
    void SetReplaceMode(bool enabled);

    /// <summary>
    /// Sets the replacement text.
    /// </summary>
    void SetReplaceText(string replaceText);

    /// <summary>
    /// Executes the search with current settings.
    /// </summary>
    void ExecuteSearch();

    /// <summary>
    /// Executes replace all with current search results and replace text.
    /// </summary>
    Task ExecuteReplaceAllAsync();

    /// <summary>
    /// Executes replace on currently selected matches.
    /// </summary>
    Task ExecuteReplaceSelectedAsync();
}
