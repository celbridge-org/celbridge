namespace Celbridge.FileSystem;

/// <summary>
/// Portable file attribute bits surfaced through IFileSystem. Each backend
/// supports the subset that maps to its native concepts: the local backend
/// models ReadOnly and Hidden as DOS file-attribute bits; remote backends
/// translate to ACL or permission probes.
/// </summary>
[Flags]
public enum FileSystemAttributes
{
    /// <summary>
    /// No attribute flags set.
    /// </summary>
    None = 0,

    /// <summary>
    /// The item is read-only. On Windows, the DOS read-only bit; on remote
    /// backends, derived from write-access permission.
    /// </summary>
    ReadOnly = 1 << 0,

    /// <summary>
    /// The item is hidden from default enumeration. On Windows, the DOS hidden
    /// bit; on POSIX-style backends, conventionally a leading-dot name.
    /// </summary>
    Hidden = 1 << 1,
}

/// <summary>
/// Behaviour of OpenWriteAsync when the target file does or does not exist.
/// </summary>
public enum WriteMode
{
    /// <summary>
    /// Create the file if missing; truncate any existing content to zero
    /// before the caller writes.
    /// </summary>
    Truncate,

    /// <summary>
    /// Create the file if missing; open at the end of existing content so the
    /// caller's writes append.
    /// </summary>
    Append,

    /// <summary>
    /// Create the file; fail if it already exists.
    /// </summary>
    CreateNew,
}

/// <summary>
/// Discriminates the outcome of a GetInfoAsync probe.
/// </summary>
public enum StorageItemKind
{
    /// <summary>
    /// The item does not exist at the resolved path.
    /// </summary>
    NotFound,

    /// <summary>
    /// The item exists and is a file.
    /// </summary>
    File,

    /// <summary>
    /// The item exists and is a folder.
    /// </summary>
    Folder,
}

/// <summary>
/// Metadata for a single storage item returned by GetInfoAsync. Size is the
/// file size in bytes for File, 0 for Folder and NotFound. ModifiedUtc is the
/// last-modified timestamp for File and Folder, default(DateTime) for NotFound.
/// Attributes carries the portable flags the backend resolved; flags the
/// backend does not model are absent.
/// </summary>
public record StorageItemInfo(
    StorageItemKind Kind,
    long Size,
    DateTime ModifiedUtc,
    FileSystemAttributes Attributes);

/// <summary>
/// Single gateway for every product-code filesystem read and write. Path-based
/// and ignorant of ResourceKey; the resource layer composes this for raw IO.
/// Async throughout so a future remote backend (S3, Google Drive) can honor the
/// contract over the network.
/// </summary>
public interface IFileSystem
{
    /// <summary>
    /// Reads the full byte content of the file at the given absolute path.
    /// </summary>
    Task<Result<byte[]>> ReadAllBytesAsync(string path);

    /// <summary>
    /// Reads the full text content of the file at the given absolute path,
    /// decoded as UTF-8.
    /// </summary>
    Task<Result<string>> ReadAllTextAsync(string path);

    /// <summary>
    /// Opens a read-only stream over the file. The caller owns the stream lifetime.
    /// </summary>
    Task<Result<Stream>> OpenReadAsync(string path);

    /// <summary>
    /// Opens a write-only stream over the file with the given creation /
    /// truncation / append behaviour. The caller owns the stream lifetime.
    /// </summary>
    Task<Result<Stream>> OpenWriteAsync(string path, WriteMode mode);

    /// <summary>
    /// Writes raw bytes to the target path, replacing any existing content.
    /// </summary>
    Task<Result> WriteAllBytesAsync(string path, byte[] bytes);

    /// <summary>
    /// Writes UTF-8 text (no BOM) to the target path, replacing any existing content.
    /// </summary>
    Task<Result> WriteAllTextAsync(string path, string content);

    /// <summary>
    /// Probes a path and returns its kind, size, modified-time, and attribute
    /// flags in a single stat.
    /// </summary>
    Task<Result<StorageItemInfo>> GetInfoAsync(string path);

    /// <summary>
    /// Enumerates files matching the pattern under the given folder, either the
    /// immediate level or the full subtree. Entries are filtered by validateEntry
    /// when provided.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> EnumerateFilesAsync(string path, string pattern, bool recursive, Func<string, bool>? validateEntry = null);

    /// <summary>
    /// Enumerates the immediate child folders of the given folder.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> EnumerateFoldersAsync(string path);

    /// <summary>
    /// Moves a file from source to destination. The destination's parent folder
    /// must exist; fails if the destination is already present.
    /// </summary>
    Task<Result> MoveFileAsync(string source, string dest);

    /// <summary>
    /// Moves a folder from source to destination. The destination's parent folder
    /// must exist.
    /// </summary>
    Task<Result> MoveFolderAsync(string source, string dest);

    /// <summary>
    /// Copies a single file from source to destination. The destination's parent
    /// folder must exist.
    /// </summary>
    Task<Result> CopyFileAsync(string source, string dest);

    /// <summary>
    /// Deletes the file at the given path.
    /// </summary>
    Task<Result> DeleteFileAsync(string path);

    /// <summary>
    /// Deletes a folder. When recursive is false, fails if the folder is not empty.
    /// </summary>
    Task<Result> DeleteFolderAsync(string path, bool recursive);

    /// <summary>
    /// Creates a folder at the given path, including any missing parents.
    /// Idempotent on an existing folder.
    /// </summary>
    Task<Result> CreateFolderAsync(string path);

    /// <summary>
    /// Sets or clears the attribute flags named in mask; set true turns them on,
    /// false clears them. Flags outside the mask are preserved.
    /// </summary>
    Task<Result> SetAttributesAsync(string path, FileSystemAttributes mask, bool set);
}
