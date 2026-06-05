namespace Celbridge.FileSystem;

/// <summary>
/// Portable file attribute flags surfaced through ILocalFileSystem. Each backend
/// supports the subset that maps to its native concepts.
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
/// Metadata returned by GetInfoAsync: the item's kind, size in bytes (0 for
/// folders and missing items), last-modified time, and portable attribute flags.
/// </summary>
public record StorageItemInfo(
    StorageItemKind Kind,
    long Size,
    DateTime ModifiedUtc,
    FileSystemAttributes Attributes);

/// <summary>
/// A file or folder entry returned by EnumerateAsync, with its absolute path,
/// the size, modified-time, and portable attribute flags from the directory
/// walk. IsFolder is false for anything that is not a directory, so a
/// non-folder is not guaranteed to be a readable regular file. Size is 0 for
/// folders.
/// </summary>
public record FileSystemEntry(
    string FullPath,
    bool IsFolder,
    long Size,
    DateTime ModifiedUtc,
    FileSystemAttributes Attributes);

/// <summary>
/// Path-based gateway for local-substrate filesystem reads and writes. The
/// resource layer composes it for raw IO against project: and other on-disk
/// roots. Remote-substrate backends bypass this interface and implement
/// IResourceFileSystem directly against their API client.
/// </summary>
public interface ILocalFileSystem
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
    /// Enumerates the files and folders matching the pattern under the given folder,
    /// either the immediate level or the full subtree. Results are deterministic
    /// across platforms: folders first, then files, each group ordered by ordinal
    /// full path.
    /// </summary>
    Task<Result<IReadOnlyList<FileSystemEntry>>> EnumerateAsync(string path, string pattern, bool recursive);

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
    /// folder must exist. Recursive folder copy has no gateway counterpart and is
    /// composed in the resource layer.
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
