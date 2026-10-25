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
/// What a text search looks for and where. Pattern is a search pattern in ripgrep's regex dialect, or plain text when
/// Literal is true. MaxResults stops the search after that many matches, and null finds every match. Include and
/// Exclude are search filters, relative to the project folder. Scope names the folder or file to search, and empty
/// searches the project. IncludeMetadataFiles also searches the text of .cel sidecars.
/// </summary>
public sealed record SearchOptions(string Pattern, bool Literal, bool MatchCase, bool WholeWord)
{
    public int? MaxResults { get; init; }

    public string Include { get; init; } = string.Empty;

    public string Exclude { get; init; } = string.Empty;

    public string Scope { get; init; } = string.Empty;

    public bool IncludeMetadataFiles { get; init; }
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
