namespace Celbridge.Resources;

/// <summary>
/// Summary statistics emitted by IResourceMetaData.RebuildAsync.
/// </summary>
public record MetaDataScanReport(
    int FilesScanned,
    int FilesSkipped,
    int ReferencesFound,
    int FrontmatterEntries,
    TimeSpan Elapsed);

/// <summary>
/// Workspace-scoped service that maintains the reference graph between project
/// resources and the frontmatter index for .cel sidecars. Indexes are rebuilt
/// from disk at workspace load and updated incrementally from ResourceMonitor
/// watcher events. Persistence is a load-time optimisation; the service is
/// correct without it.
/// </summary>
public interface IResourceMetaData
{
    /// <summary>
    /// True once the initial rebuild has completed and the in-memory indexes
    /// reflect the on-disk state. Operations that depend on the indexes should
    /// await ReadyAsync before reading.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Completes once IsReady becomes true. Returns immediately if already ready.
    /// </summary>
    Task WaitUntilReadyAsync();

    /// <summary>
    /// Drains the queue of pending watcher-driven rescans before returning, so
    /// that subsequent reads reflect every change observed up to the call.
    /// Used by structural operations and integration tests that need synchronous
    /// post-write consistency.
    /// </summary>
    Task WaitForPendingUpdatesAsync();

    /// <summary>
    /// Rebuilds the in-memory indexes from disk. Replaces existing state on
    /// completion. Intended for use at workspace load and from diagnostic tools.
    /// </summary>
    Task<Result<MetaDataScanReport>> RebuildAsync();

    /// <summary>
    /// Returns the resource keys of every file that contains a project:target
    /// reference, as detected by the permissive scan.
    /// </summary>
    IReadOnlyList<ResourceKey> GetReferencers(ResourceKey target);

    /// <summary>
    /// Returns the resource keys named by every project: reference found inside
    /// the source file.
    /// </summary>
    IReadOnlyList<ResourceKey> GetReferences(ResourceKey source);

    /// <summary>
    /// Returns the union of every key that appears as a target in the graph.
    /// Used by metadata_check_project to enumerate candidate targets without
    /// walking every file's reference list.
    /// </summary>
    IReadOnlyList<ResourceKey> GetAllReferencedTargets();

    /// <summary>
    /// Returns the parsed top-level fields of the resource's sidecar frontmatter
    /// as an immutable dictionary. Fails if the resource has no sidecar or the
    /// sidecar is in a non-Healthy state.
    /// </summary>
    Result<IReadOnlyDictionary<string, object>> GetFrontmatter(ResourceKey resource);

    /// <summary>
    /// Writes a single field to the resource's sidecar frontmatter. Creates the
    /// sidecar if one does not exist.
    /// </summary>
    Task<Result> SetFrontmatterFieldAsync(ResourceKey resource, string field, object value);

    /// <summary>
    /// Removes a field from the resource's sidecar frontmatter. The sidecar file
    /// remains even if the resulting frontmatter is empty.
    /// </summary>
    Task<Result> RemoveFrontmatterFieldAsync(ResourceKey resource, string field);

    /// <summary>
    /// Returns every resource whose frontmatter contains the field matching the
    /// value. Scalar fields match by equality; list-of-scalar fields match by
    /// contains.
    /// </summary>
    IReadOnlyList<ResourceKey> FindByMetaData(string field, object value);

    /// <summary>
    /// Returns the tag list for the resource. Empty if the resource has no
    /// sidecar or no tags entry.
    /// </summary>
    IReadOnlyList<string> GetTags(ResourceKey resource);

    /// <summary>
    /// Appends a tag to the resource's sidecar frontmatter. Creates the sidecar
    /// if one does not exist. Idempotent.
    /// </summary>
    Task<Result> AddTagAsync(ResourceKey resource, string tag);

    /// <summary>
    /// Removes a tag from the resource's sidecar frontmatter. Idempotent.
    /// </summary>
    Task<Result> RemoveTagAsync(ResourceKey resource, string tag);

    /// <summary>
    /// Returns every resource whose tags list contains the specified tag.
    /// </summary>
    IReadOnlyList<ResourceKey> FindByTag(string tag);
}
