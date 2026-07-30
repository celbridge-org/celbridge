namespace Celbridge.Resources;

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
    /// The text files naming the given target, sorted by key. Empty when nothing references it.
    /// </summary>
    IReadOnlyList<ResourceKey> GetReferencers(ResourceKey target);
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
