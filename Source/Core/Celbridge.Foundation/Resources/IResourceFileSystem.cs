namespace Celbridge.Resources;

/// <summary>
/// Options for a move. AllowCrossRoot lets the source and destination sit on
/// different roots, which a caller has to ask for: a resource keeps no identity
/// across roots, so such a move rewrites no references and announces no key change.
/// </summary>
public sealed record MoveOptions(bool AllowCrossRoot = false);

/// <summary>
/// The gateway for reads, writes, and structural operations on resources
/// addressed by ResourceKey. Structural operations on project: resources
/// cascade the paired sidecar and rewrite references in scannable file types.
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
    /// created if it does not exist.
    /// </summary>
    Task<Result> WriteAllBytesAsync(ResourceKey resource, byte[] bytes);

    /// <summary>
    /// Writes UTF-8 text (no BOM) to the resource. The destination's parent
    /// folder is created if it does not exist. Callers select line endings
    /// appropriate to the target file.
    /// </summary>
    Task<Result> WriteAllTextAsync(ResourceKey resource, string content);

    /// <summary>
    /// Opens an exclusive write stream over the resource. Creates the parent
    /// folder if missing and truncates the destination on open. The caller
    /// owns the stream lifetime.
    /// </summary>
    Task<Result<Stream>> OpenWriteAsync(ResourceKey resource);

    /// <summary>
    /// Moves the resource within its root and cascades the paired sidecar. The
    /// resource keeps its identity: references to it are rewritten and the key
    /// change is announced. A move across roots is refused unless the options
    /// allow it. Such a move is a delete from one root and a create in the other,
    /// like CopyAsync followed by DeleteAsync, so references to the source still
    /// name the resource that left.
    /// </summary>
    Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey dest, MoveOptions? options = null);

    /// <summary>
    /// Copies the resource and cascades the paired sidecar to the destination.
    /// References inside the copied content keep pointing at their original
    /// targets. Cross-root copies are supported.
    /// </summary>
    Task<Result<CopyResult>> CopyAsync(ResourceKey source, ResourceKey dest);

    /// <summary>
    /// Deletes the resource and cascades the paired sidecar.
    /// </summary>
    Task<Result<DeleteResult>> DeleteAsync(ResourceKey source);

    /// <summary>
    /// Creates a folder at the resource path, including any missing parents.
    /// Idempotent on an existing folder.
    /// </summary>
    Task<Result> CreateFolderAsync(ResourceKey folder);

    /// <summary>
    /// Probes a resource and returns its kind, size, and modified-time in a
    /// single call.
    /// </summary>
    Task<Result<StorageItemInfo>> GetInfoAsync(ResourceKey resource);

    /// <summary>
    /// Returns the immediate children of a folder resource. Single-level only;
    /// recursive callers walk per-level. Fails when the resource is not a folder.
    /// </summary>
    Task<Result<IReadOnlyList<FolderItem>>> EnumerateFolderAsync(ResourceKey folder);

    /// <summary>
    /// Returns a deterministic content fingerprint for the resource as an
    /// opaque string. Equal bytes yield equal fingerprints within the same
    /// backend; the concrete algorithm is per-implementation. Callers compare
    /// with string equality.
    /// </summary>
    Task<Result<string>> ComputeHashAsync(ResourceKey resource);

    /// <summary>
    /// Sets or clears the named filesystem attribute flags on the resource.
    /// Flags outside the mask are preserved. Routed through the resource
    /// policy gate as a write operation.
    /// </summary>
    Task<Result> SetAttributesAsync(ResourceKey resource, FileSystemAttributes mask, bool set);
}
