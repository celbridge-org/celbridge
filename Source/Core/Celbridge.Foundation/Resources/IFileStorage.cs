namespace Celbridge.Resources;

/// <summary>
/// The chokepoint for disk reads, writes, and structural operations against any
/// resource addressable by a ResourceKey — files under the project tree as well
/// as files under registered non-project roots (e.g. temp:, logs:). Callers pass
/// a ResourceKey; the layer dispatches via the registered root handlers so
/// containment and symlink validation run automatically. Reads and writes have
/// bounded retry on transient IO failures; the buffered-bytes write paths
/// (WriteAllBytesAsync / WriteAllTextAsync) are additionally atomic via
/// temp-file rename within a single volume — the streaming OpenWriteAsync
/// path is not atomic. Structural operations on project: resources additionally
/// cascade the paired sidecar, and rewrite references that live inside
/// scannable file types (see ResourceScanner for the current allowlist);
/// operations on non-project roots are pure byte moves.
/// </summary>
public interface IFileStorage
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
    Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey dest);

    /// <summary>
    /// Copies the resource and cascades the paired sidecar to the destination.
    /// References inside the copied content keep pointing at their original targets.
    /// </summary>
    Task<Result<CopyResult>> CopyAsync(ResourceKey source, ResourceKey dest);

    /// <summary>
    /// Deletes the resource and cascades the paired sidecar.
    /// </summary>
    Task<Result<DeleteResult>> DeleteAsync(ResourceKey source);

    /// <summary>
    /// Creates a folder at the resource path, including any missing parents.
    /// Idempotent: succeeds without error when the folder already exists.
    /// </summary>
    Task<Result> CreateFolderAsync(ResourceKey folder);

    /// <summary>
    /// Probes a resource and returns its kind (NotFound, File, or Folder) along
    /// with its size and modified-time in a single roundtrip. Callers that only
    /// need existence check Kind != NotFound; callers that need to discriminate
    /// file vs folder switch on Kind. Size and ModifiedUtc are populated for
    /// the File case; Folder yields Size = 0 with ModifiedUtc set.
    /// </summary>
    Task<Result<StorageItemInfo>> GetInfoAsync(ResourceKey resource);

    /// <summary>
    /// Returns the immediate children of a folder resource as FolderItem records.
    /// Works for any registered root. Single-level only; recursive callers walk per-level.
    /// Fails when the resource does not resolve to an existing folder.
    /// </summary>
    Task<Result<IReadOnlyList<FolderItem>>> EnumerateFolderAsync(ResourceKey folder);

    /// <summary>
    /// Reads the resource and returns a SHA256 hex digest of its bytes. Use this
    /// when the caller only needs the hash (e.g. external-change detection) and
    /// would otherwise read bytes purely to feed them through SHA256. Callers
    /// that already have the bytes in hand should hash directly via
    /// FileHashHelper.HashBytes rather than re-reading through this method.
    /// </summary>
    Task<Result<string>> ComputeHashAsync(ResourceKey resource);
}
