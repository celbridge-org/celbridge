using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A folder node in the file_get_tree output. Includes a truncated flag when the node
/// is at the depth limit and has children that were not expanded.
/// </summary>
public record class TreeFolderNode(string Name, string Type, List<object> Children, bool? Truncated = null);

/// <summary>
/// A file node in the file_get_tree output.
/// </summary>
public record class TreeFileNode(string Name, string Type);

public partial class FileTools
{
    /// <summary>
    /// Returns a recursive folder tree as JSON with configurable depth.
    /// Supports optional glob filtering to show only files matching a pattern, and type filtering to show only files or folders.
    /// Folder nodes at the depth limit that have children include a truncated flag.
    /// </summary>
    /// <param name="resource">Resource key of the root folder.</param>
    /// <param name="depth">Maximum depth to traverse. Default is 3.</param>
    /// <param name="glob">Optional glob pattern to filter files by name (e.g. "*.cs", "*.py"). Folders are always included if they have matching descendants. When empty, all files are shown.</param>
    /// <param name="type">Optional type filter: "file" to show only files, "folder" to show only folders. When empty, both are shown.</param>
    /// <returns>JSON tree where each node has: name (string), type (string), children (array, folders only), and truncated (bool, present on folder nodes at depth limit that have children).</returns>
    [McpServerTool(Name = "file_get_tree", ReadOnly = true)]
    [ToolAlias("file.get_tree")]
    public async partial Task<CallToolResult> GetTree(string resource, int depth = 3, string glob = "", string type = "")
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        // Route through the command queue so the snapshot observes state after all
        // previously enqueued commands have run. The command reads the registry and
        // builds the filtered tree on the command thread.
        var (callResult, snapshot) = await ExecuteCommandAsync<IGetFileTreeCommand, FileTreeSnapshot>(command =>
        {
            command.Resource = resourceKey;
            command.Depth = depth;
            command.Glob = glob;
            command.TypeFilter = type;
        });

        if (callResult.IsError == true || snapshot is null)
        {
            return callResult;
        }

        var rootNode = snapshot.Root is not null
            ? ConvertNode(snapshot.Root)
            : new TreeFolderNode(string.Empty, "folder", new List<object>());

        return SuccessResult(SerializeJson(rootNode));
    }

    private static TreeFolderNode ConvertNode(FileTreeSnapshotNode node)
    {
        var children = new List<object>();
        foreach (var child in node.Children)
        {
            if (child.IsFolder)
            {
                children.Add(ConvertNode(child));
            }
            else
            {
                children.Add(new TreeFileNode(child.Name, "file"));
            }
        }

        if (node.Truncated)
        {
            return new TreeFolderNode(node.Name, "folder", children, Truncated: true);
        }

        return new TreeFolderNode(node.Name, "folder", children);
    }
}
