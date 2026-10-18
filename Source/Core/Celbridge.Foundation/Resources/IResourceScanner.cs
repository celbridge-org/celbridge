namespace Celbridge.Resources;

/// <summary>
/// On-demand scanner over the project's sidecar files. Each call
/// walks the registry's known files in parallel; no in-memory index is
/// maintained between calls.
/// </summary>
public interface IResourceScanner
{
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
