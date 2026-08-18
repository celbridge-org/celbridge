namespace Celbridge.Resources;

/// <summary>
/// One place a resource is referenced: the file holding the reference literal, and where in that
/// file it sits, in one-based line and column numbers.
/// </summary>
public record ResourceReferenceSite(ResourceKey Source, int Line, int Column);

/// <summary>
/// Every tracked "project:" reference in the project, gathered in one walk.
/// </summary>
public interface IResourceReferenceIndex
{
    /// <summary>
    /// Every resource named by a reference somewhere in the project, sorted by key.
    /// </summary>
    IReadOnlyList<ResourceKey> ReferencedTargets { get; }

    /// <summary>
    /// Every place the given target is named, sorted by source key then position. Empty when nothing
    /// references it. A file naming the same target twice appears once per reference.
    /// </summary>
    IReadOnlyList<ResourceReferenceSite> GetReferencers(ResourceKey target);
}

/// <summary>
/// On-demand scanner over the project's text and sidecar files. Each call
/// walks the registry's known files in parallel; no in-memory index is
/// maintained between calls.
/// </summary>
public interface IResourceScanner
{
    /// <summary>
    /// Returns every reference in the project from a single walk over its text files.
    /// </summary>
    Task<IResourceReferenceIndex> BuildReferenceIndexAsync();

    /// <summary>
    /// Returns the project keys named by every "project:" reference inside the
    /// source file. Returns an empty list when the source file cannot be read.
    /// </summary>
    Task<IReadOnlyList<ResourceKey>> FindReferencesInAsync(ResourceKey source);

    /// <summary>
    /// Returns every paired-sidecar parent resource whose .cel tag list
    /// contains the given tag value. Results are sorted by key.
    /// </summary>
    Task<IReadOnlyList<ResourceKey>> FindByTagAsync(string tag);

    /// <summary>
    /// Returns the unique tag values across every paired-sidecar .cel file in
    /// the project. Broken sidecars are skipped. Results are sorted ordinal
    /// for diff stability.
    /// </summary>
    Task<IReadOnlyList<string>> ListAllTagsAsync();
}
