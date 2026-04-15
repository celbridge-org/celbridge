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
/// Snapshot of a folder tree produced by IGetFileTreeCommand. Root is null when the
/// tree was pruned by filtering (e.g. a glob that matched nothing).
/// </summary>
public record class FileTreeSnapshot(FileTreeSnapshotNode? Root);

/// <summary>
/// Read-only query that captures a recursive folder tree rooted at a given resource in
/// a snapshot. Routed through the command queue so callers observe state that is
/// consistent with all previously enqueued commands.
/// </summary>
public interface IGetFileTreeCommand : IExecutableCommand<FileTreeSnapshot>
{
    ResourceKey Resource { get; set; }
    int Depth { get; set; }
    string Glob { get; set; }
    string TypeFilter { get; set; }
}
