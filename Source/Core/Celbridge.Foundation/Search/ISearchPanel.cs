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
    /// Whether replace mode is currently enabled, which governs whether the replace controls are shown.
    /// </summary>
    bool IsReplaceModeEnabled { get; }

    /// <summary>
    /// Enables or disables replace mode.
    /// </summary>
    void SetReplaceMode(bool enabled);

    /// <summary>
    /// Sets the replacement text.
    /// </summary>
    void SetReplaceText(string replaceText);

    /// <summary>
    /// Sets the files to include to the folder, like "./docs/api", and shows the search filters. The empty key clears
    /// the files to include. The caller starts the search.
    /// </summary>
    void SetFolder(ResourceKey folder);

    /// <summary>
    /// Returns true when the search filters are shown: the files to include and the files to exclude.
    /// </summary>
    bool AreFiltersVisible { get; }

    /// <summary>
    /// Shows or hides the search filters.
    /// </summary>
    void SetFiltersVisible(bool visible);

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
