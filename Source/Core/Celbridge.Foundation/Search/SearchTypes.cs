namespace Celbridge.Search;

/// <summary>
/// Represents a single line match within a file.
/// LineText is the formatted display text (may be trimmed/truncated with "...").
/// MatchStart is the position in the formatted display text (for UI highlighting).
/// OriginalMatchStart is the position in the original unformatted file line (for editor navigation).
/// </summary>
public record SearchMatchLine(
    int LineNumber,
    string LineText,
    int MatchStart,
    int MatchLength,
    int OriginalMatchStart);

/// <summary>
/// Represents all matches within a single file.
/// </summary>
public record SearchFileResult(
    ResourceKey Resource,
    string FileName,
    string RelativePath,
    List<SearchMatchLine> Matches);

/// <summary>
/// What a text search looks for and where. Matcher finds the matches in each line. Filter matches the files to search,
/// by their paths in the project. MaxResults stops the search after that many matches, and null finds every match.
/// Scope names the folder or file to search, and empty searches the project. IncludeMetadataFiles also searches the
/// text of .cel sidecars. ApplySearchExclude, true by default, leaves out what the project's search list excludes.
/// </summary>
public sealed record SearchOptions(ITextMatcher Matcher, IPathMatcher Filter)
{
    public int? MaxResults { get; init; }

    public string Scope { get; init; } = string.Empty;

    public bool IncludeMetadataFiles { get; init; }

    public bool ApplySearchExclude { get; init; } = true;
}

/// <summary>
/// Represents the complete search results. FilesMatchingFilters counts the files in scope that the include and
/// exclude filters let through.
/// </summary>
public record SearchResults(
    string SearchTerm,
    List<SearchFileResult> FileResults,
    int TotalMatches,
    int TotalFiles,
    bool WasCancelled,
    bool ReachedMaxResults,
    int FilesMatchingFilters);

/// <summary>
/// Contains the search and replace history for a workspace.
/// Stores only the search/replace terms, not the search options.
/// </summary>
public record SearchHistory(
    List<string> SearchTerms,
    List<string> ReplaceTerms);
