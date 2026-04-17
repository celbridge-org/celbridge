using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Snapshot of metadata for a single file or folder resource, produced by IGetFileInfoCommand.
/// Exists is false when the resource cannot be resolved. IsFile distinguishes file from folder
/// when Exists is true. IsText and LineCount are only populated for text files.
/// </summary>
public record class FileInfoSnapshot(
    bool Exists,
    bool IsFile,
    long Size,
    DateTime ModifiedUtc,
    string Extension,
    bool IsText,
    int? LineCount);

/// <summary>
/// Read-only query that captures metadata for a single file or folder resource in a snapshot.
/// Routed through the command queue so callers observe state that is consistent with all
/// previously enqueued commands.
/// </summary>
public interface IGetFileInfoCommand : IExecutableCommand<FileInfoSnapshot>
{
    ResourceKey Resource { get; set; }
}
