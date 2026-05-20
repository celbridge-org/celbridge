namespace Celbridge.Resources;

/// <summary>
/// The outcome of the sidecar cascade attached to a structural operation.
/// </summary>
public enum SidecarOutcome
{
    /// <summary>
    /// No sidecar file existed alongside the source; nothing to cascade.
    /// </summary>
    NotPresent,

    /// <summary>
    /// A sidecar existed and the operation applied to it successfully.
    /// </summary>
    Cascaded,

    /// <summary>
    /// A sidecar existed but the cascade step failed. The parent operation still succeeded.
    /// </summary>
    Failed,
}

/// <summary>
/// Result of an integrity-aware move: the list of resources whose references
/// were rewritten and the outcome of the paired-sidecar cascade.
/// </summary>
public record MoveResult(
    IReadOnlyList<ResourceKey> UpdatedReferencers,
    SidecarOutcome Sidecar);

/// <summary>
/// Result of an integrity-aware copy: the outcome of the paired-sidecar cascade.
/// </summary>
public record CopyResult(
    SidecarOutcome Sidecar);

/// <summary>
/// Result of an integrity-aware delete: the outcome of the paired-sidecar cascade.
/// </summary>
public record DeleteResult(
    SidecarOutcome Sidecar);

/// <summary>
/// The chokepoint for disk reads, writes, and structural operations on project
/// resources. Callers pass a ResourceKey; the layer resolves it through
/// IResourceRegistry so containment and symlink validation run automatically.
/// Bytes and text writes are atomic via temp-file rename with bounded retry on
/// transient IO failures. Structural operations include reference rewrites and
/// the paired-sidecar cascade as part of their definition.
/// </summary>
public interface IResourceFileSystem
{
    /// <summary>
    /// Reads the full byte content of the resource.
    /// </summary>
    Task<Result<byte[]>> ReadAllBytesAsync(ResourceKey resource);

    /// <summary>
    /// Reads the full text content of the resource. Files are decoded as UTF-8.
    /// </summary>
    Task<Result<string>> ReadAllTextAsync(ResourceKey resource);

    /// <summary>
    /// Opens a read-only stream over the resource. The caller owns the stream lifetime.
    /// </summary>
    Task<Result<Stream>> OpenReadAsync(ResourceKey resource);

    /// <summary>
    /// Writes raw bytes to the resource. The destination's parent folder is
    /// created if it does not exist. Atomic via temp-file rename, with bounded
    /// retry on transient IOException.
    /// </summary>
    Task<Result> WriteAllBytesAsync(ResourceKey resource, byte[] bytes);

    /// <summary>
    /// Writes UTF-8 text (no BOM) to the resource. The destination's parent
    /// folder is created if it does not exist. Atomic via temp-file rename,
    /// with bounded retry on transient IOException. Callers are responsible
    /// for selecting line endings appropriate to the target file.
    /// </summary>
    Task<Result> WriteAllTextAsync(ResourceKey resource, string content);

    /// <summary>
    /// Opens an exclusive write stream over the resource. Creates the parent
    /// folder if missing and truncates the destination on open. The caller
    /// owns the stream lifetime. Writes are not atomic via this path; a crash
    /// mid-write leaves the file partially written.
    /// </summary>
    Task<Result<Stream>> OpenWriteAsync(ResourceKey resource);

    /// <summary>
    /// Moves the resource and cascades reference rewrites and the paired
    /// sidecar. Cross-root moves are not supported.
    /// </summary>
    Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey destination);

    /// <summary>
    /// Copies the resource and cascades the paired sidecar to the destination.
    /// References inside the copied content keep pointing at their original targets.
    /// </summary>
    Task<Result<CopyResult>> CopyAsync(ResourceKey source, ResourceKey destination);

    /// <summary>
    /// Deletes the resource and cascades the paired sidecar.
    /// </summary>
    Task<Result<DeleteResult>> DeleteAsync(ResourceKey source);

    /// <summary>
    /// Returns true if a file or folder exists at the resolved path of the resource key.
    /// </summary>
    Task<Result<bool>> ExistsAsync(ResourceKey resource);
}
