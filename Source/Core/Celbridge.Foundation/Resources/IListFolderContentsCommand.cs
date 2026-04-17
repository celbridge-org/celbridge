using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// A single child entry in a folder contents snapshot.
/// </summary>
public record class FolderContentsEntry(
    string Name,
    bool IsFolder,
    long Size,
    DateTime ModifiedUtc);

/// <summary>
/// Snapshot of the immediate children of a folder resource, produced by IListFolderContentsCommand.
/// </summary>
public record class FolderContentsSnapshot(IReadOnlyList<FolderContentsEntry> Entries);

/// <summary>
/// Read-only query that captures the immediate children of a folder resource in a snapshot.
/// Routed through the command queue so callers observe state that is consistent with all
/// previously enqueued commands.
/// </summary>
public interface IListFolderContentsCommand : IExecutableCommand<FolderContentsSnapshot>
{
    ResourceKey Resource { get; set; }
}
