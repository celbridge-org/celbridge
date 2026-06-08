namespace Celbridge.Resources;

/// <summary>
/// On-demand scanner over the project's text and sidecar files. Each call
/// walks the registry's known files in parallel; no in-memory index is
/// maintained between calls.
/// </summary>
public interface IResourceScanner
{
    /// <summary>
    /// Returns the resource keys of every text file in the project that
    /// contains a tracked "project:target" reference literal for the given
    /// target. Results are sorted by key.
    /// </summary>
    Task<IReadOnlyList<ResourceKey>> FindReferencersAsync(ResourceKey target);

    /// <summary>
    /// Returns the project keys named by every "project:" reference inside the
    /// source file. Returns an empty list when the source file cannot be read.
    /// </summary>
    Task<IReadOnlyList<ResourceKey>> FindReferencesInAsync(ResourceKey source);

    /// <summary>
    /// Returns every distinct "project:" target named anywhere in the project,
    /// sorted by key. Used by ProjectCheckCommand to enumerate candidate
    /// targets without walking every referencer.
    /// </summary>
    Task<IReadOnlyList<ResourceKey>> FindAllReferencedTargetsAsync();

    /// <summary>
    /// Returns every paired-sidecar parent resource whose .cel tag list
    /// contains the given tag value. Results are sorted by key.
    /// </summary>
    Task<IReadOnlyList<ResourceKey>> FindByTagAsync(string tag);
}
