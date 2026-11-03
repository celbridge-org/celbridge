using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// A node in a file tree snapshot. Children is empty for file nodes. Truncated is
/// true on folder nodes that have children beyond the depth limit.
/// </summary>
public record class FileTreeSnapshotNode(
    string Name,
    bool IsFolder,
    IReadOnlyList<FileTreeSnapshotNode> Children,
    bool Truncated);

/// <summary>
/// Snapshot of a folder tree produced by IGetFileTreeCommand. Root is the starting
/// folder, with an empty Children list when the filters match nothing.
/// </summary>
public record class FileTreeSnapshot(FileTreeSnapshotNode Root);

/// <summary>
/// Read-only query that captures a recursive folder tree rooted at a given resource in
/// a snapshot. Routed through the command queue so callers observe state that is
/// consistent with all previously enqueued commands.
/// </summary>
public interface IGetFileTreeCommand : IExecutableCommand<FileTreeSnapshot>
{
    /// <summary>
    /// Resource key of the folder to start from.
    /// </summary>
    ResourceKey Resource { get; set; }

    /// <summary>
    /// Number of folder levels to expand, counted from the starting folder.
    /// </summary>
    int Depth { get; set; }

    /// <summary>
    /// Search filter for files. A glob keeps a folder only when a matching file is below it, or when the depth limit
    /// cuts the folder off.
    /// </summary>
    string Glob { get; set; }

    /// <summary>
    /// Node type to list. "file" lists files and the folders that contain them. "folder" lists only folders. An
    /// empty string lists both, and any other value fails the command. Letter case is ignored.
    /// </summary>
    string TypeFilter { get; set; }
}
