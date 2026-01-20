namespace Celbridge.Explorer;

/// <summary>
/// Represents a single line match within a file.
/// </summary>
public record SearchMatchLine(
    int LineNumber,
    string LineText,
    int MatchStart,
    int MatchLength);

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
