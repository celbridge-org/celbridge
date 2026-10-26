using System.Text;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;

namespace Celbridge.Search.ViewModels;

/// <summary>
/// Describes a change to the Search panel's filters. IsTyped is true for a change typed into a field.
/// </summary>
public sealed record SearchFiltersChange(bool IsTyped);

/// <summary>
/// The Search panel's filters: the files to include, the files to exclude, Search Only in Open Documents and Apply
/// Search Exclude Settings. Apply Search Exclude Settings applies the project's search list. The filters keep their
/// values in the workspace settings.
/// </summary>
public partial class SearchFiltersViewModel : ObservableObject
{
    // The characters escaped in a folder's path in the files to include, so the path stays one literal pattern.
    private const string PatternCharacters = "\\*?[]{},";

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IStringLocalizer _stringLocalizer;

    // Null while the files to include or the files to exclude are refused.
    private SearchFilter? _filter;

    // False while the constructor reads the workspace settings, so a value read back is not written again.
    private bool _isLoaded;

    // True while SetFolder changes the filters. The caller starts the search, so the changes raise no Changed event.
    private bool _isSettingFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHiddenFiltersDot))]
    private bool _areFiltersVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHiddenFiltersDot))]
    private string _filesToInclude = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHiddenFiltersDot))]
    private string _filesToExclude = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHiddenFiltersDot))]
    private bool _searchOnlyOpenDocuments;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHiddenFiltersDot))]
    private bool _applySearchExclude = true;

    [ObservableProperty]
    private bool _hasInvalidFilter;

    /// <summary>
    /// True when a filter is in force: text in either field, or either toggle away from its default.
    /// </summary>
    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(FilesToInclude)
        || !string.IsNullOrWhiteSpace(FilesToExclude)
        || SearchOnlyOpenDocuments
        || !ApplySearchExclude;

    /// <summary>
    /// True while the filters are hidden and one of them is in force. The filters button then shows a dot.
    /// </summary>
    public bool ShowHiddenFiltersDot => !AreFiltersVisible && HasFilters;

    /// <summary>
    /// Raised when the user changes a field or a toggle.
    /// </summary>
    public event EventHandler<SearchFiltersChange>? Changed;

    public string FiltersTooltip { get; }

    public string FilesToIncludeLabel { get; }

    public string FilesToIncludePlaceholder { get; }

    public string FilesToExcludeLabel { get; }

    public string FilesToExcludePlaceholder { get; }

    public string OnlyOpenDocumentsTooltip { get; }

    public string ApplySearchExcludeTooltip { get; }

    public string InvalidFilterText { get; }

    private IBindableWorkspaceSettings WorkspaceSettings =>
        _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;

    public SearchFiltersViewModel(
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer)
    {
        _workspaceWrapper = workspaceWrapper;
        _stringLocalizer = stringLocalizer;

        FiltersTooltip = _stringLocalizer.GetString("SearchPanel_FiltersTooltip");
        FilesToIncludeLabel = _stringLocalizer.GetString("SearchPanel_FilesToIncludeLabel");
        FilesToIncludePlaceholder = _stringLocalizer.GetString("SearchPanel_FilesToIncludePlaceholder");
        FilesToExcludeLabel = _stringLocalizer.GetString("SearchPanel_FilesToExcludeLabel");
        FilesToExcludePlaceholder = _stringLocalizer.GetString("SearchPanel_FilesToExcludePlaceholder");
        OnlyOpenDocumentsTooltip = _stringLocalizer.GetString("SearchPanel_OnlyOpenDocumentsTooltip");
        ApplySearchExcludeTooltip = _stringLocalizer.GetString("SearchPanel_ApplySearchExcludeSettingsTooltip");
        InvalidFilterText = _stringLocalizer.GetString("SearchPanel_InvalidFilter");

        var workspaceSettings = WorkspaceSettings;
        AreFiltersVisible = workspaceSettings.SearchFiltersVisible;
        FilesToInclude = workspaceSettings.SearchFilesToInclude;
        FilesToExclude = workspaceSettings.SearchFilesToExclude;
        SearchOnlyOpenDocuments = workspaceSettings.SearchOnlyOpenDocuments;
        ApplySearchExclude = workspaceSettings.ApplySearchExclude;

        _isLoaded = true;

        CompileFilter();
    }

    /// <summary>
    /// Sets the files to include to the folder, like "./docs/api", and shows the filters. The empty key clears the
    /// files to include. The caller starts the search.
    /// </summary>
    public void SetFolder(ResourceKey folder)
    {
        _isSettingFolder = true;
        try
        {
            FilesToInclude = folder.IsEmpty
                ? string.Empty
                : FormatFolderPattern(folder);
            AreFiltersVisible = true;
        }
        finally
        {
            _isSettingFolder = false;
        }
    }

    /// <summary>
    /// Returns the options for a search of the matcher's pattern within the filters, or null while a field is refused.
    /// With Search Only in Open Documents on, the options name the files open in documents.
    /// </summary>
    public SearchOptions? CreateSearchOptions(ITextMatcher matcher, int? maxResults)
    {
        var filter = _filter;
        if (filter is null)
        {
            return null;
        }

        IReadOnlyList<ResourceKey>? files = null;
        if (SearchOnlyOpenDocuments)
        {
            files = _workspaceWrapper.WorkspaceService.DocumentsService
                .GetOpenDocuments()
                .Select(document => document.FileResource)
                .ToList();
        }

        var options = new SearchOptions(matcher, filter)
        {
            MaxResults = maxResults,
            Files = files,
            ApplySearchExclude = ApplySearchExclude,
        };

        return options;
    }

    partial void OnAreFiltersVisibleChanged(bool value)
    {
        if (!_isLoaded)
        {
            return;
        }

        WorkspaceSettings.SearchFiltersVisible = value;
    }

    partial void OnFilesToIncludeChanged(string value)
    {
        if (!_isLoaded)
        {
            return;
        }

        WorkspaceSettings.SearchFilesToInclude = value;
        CompileFilter();
        RaiseChanged(isTyped: true);
    }

    partial void OnFilesToExcludeChanged(string value)
    {
        if (!_isLoaded)
        {
            return;
        }

        WorkspaceSettings.SearchFilesToExclude = value;
        CompileFilter();
        RaiseChanged(isTyped: true);
    }

    partial void OnSearchOnlyOpenDocumentsChanged(bool value)
    {
        if (!_isLoaded)
        {
            return;
        }

        WorkspaceSettings.SearchOnlyOpenDocuments = value;
        RaiseChanged(isTyped: false);
    }

    partial void OnApplySearchExcludeChanged(bool value)
    {
        if (!_isLoaded)
        {
            return;
        }

        WorkspaceSettings.ApplySearchExclude = value;
        RaiseChanged(isTyped: false);
    }

    private void RaiseChanged(bool isTyped)
    {
        if (_isSettingFolder)
        {
            return;
        }

        Changed?.Invoke(this, new SearchFiltersChange(isTyped));
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

    // Writes a folder as a pattern from the project folder, like "./docs/api". A pattern reads some characters
    // specially, so each of those gets a '\' first, and a name with a comma or a brace stays one literal pattern.
    private static string FormatFolderPattern(ResourceKey folder)
    {
        var pattern = new StringBuilder("./");
        foreach (var character in folder.Path)
        {
            if (PatternCharacters.Contains(character))
            {
                pattern.Append('\\');
            }

            pattern.Append(character);
        }

        return pattern.ToString();
    }
}
