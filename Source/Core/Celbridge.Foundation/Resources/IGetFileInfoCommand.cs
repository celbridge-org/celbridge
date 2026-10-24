using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Describes whether a .cel sidecar's content parses as TOML.
/// </summary>
public enum CelParseStatus
{
    /// <summary>
    /// The file parses cleanly as TOML.
    /// </summary>
    Healthy,

    /// <summary>
    /// The file failed to parse: malformed TOML, merge-conflict markers, or
    /// any other parse failure.
    /// </summary>
    Broken,
}

/// <summary>
/// Snapshot of metadata for a single file or folder resource, produced by IGetFileInfoCommand.
/// Exists is false when the resource cannot be resolved. IsFile distinguishes file from folder
/// when Exists is true. IsText and LineCount are only populated for text files. IsReadOnly
/// reflects the filesystem read-only attribute (Windows DOS bit; derived from write permission
/// on remote backends) and applies to files and folders. Sidecar* fields describe the paired
/// .cel sidecar when the file has one on disk. They remain null for files without a sidecar
/// and for folders.
/// </summary>
public record class FileInfoSnapshot(
    bool Exists,
    bool IsFile,
    long Size,
    DateTime ModifiedUtc,
    string Extension,
    bool IsText,
    int? LineCount,
    bool IsReadOnly,
    ResourceKey? SidecarKey,
    CelParseStatus? SidecarStatus);

/// <summary>
/// Read-only query that captures metadata for a single file or folder resource in a snapshot.
/// Routed through the command queue so callers observe state that is consistent with all
/// previously enqueued commands.
/// </summary>
public interface IGetFileInfoCommand : IExecutableCommand<FileInfoSnapshot>
{
    /// <summary>
    /// The file or folder resource to probe. Set by the caller before the command runs.
    /// </summary>
    ResourceKey Resource { get; set; }
}
