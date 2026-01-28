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
/// Represents the complete search results.
/// </summary>
public record SearchResults(
    string SearchTerm,
    List<SearchFileResult> FileResults,
    int TotalMatches,
    int TotalFiles,
    bool WasCancelled,
    bool ReachedMaxResults);
