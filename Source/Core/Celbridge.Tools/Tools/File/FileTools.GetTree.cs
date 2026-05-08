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
    /// Returns a recursive folder tree as JSON, with optional glob and type filtering.
    /// </summary>
    /// <param name="resource">Resource key of the root folder.</param>
    /// <param name="depth">Maximum depth to traverse.</param>
    /// <param name="glob">Optional glob to filter files by name. Folders are kept when they have matching descendants.</param>
    /// <param name="type">Optional type filter: "file" or "folder". Empty shows both.</param>
    /// <returns>JSON tree where each node has name, type, children (folders only), and a truncated flag at the depth limit.</returns>
    [McpServerTool(Name = "file_get_tree", ReadOnly = true)]
    [ToolAlias("file.get_tree")]
    public async partial Task<CallToolResult> GetTree(string resource, int depth = 3, string glob = "", string type = "")
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        // Route through the command queue so the snapshot observes state after all
        // previously enqueued commands have run. The command reads the registry and
        // builds the filtered tree on the command thread.
        var getTreeResult = await ExecuteCommandAsync<IGetFileTreeCommand, FileTreeSnapshot>(command =>
        {
            command.Resource = resourceKey;
            command.Depth = depth;
            command.Glob = glob;
            command.TypeFilter = type;
        });

        if (getTreeResult.IsFailure)
        {
            return ToolError(getTreeResult);
        }

        var snapshot = getTreeResult.Value;
        var rootNode = snapshot.Root is not null
            ? ConvertNode(snapshot.Root)
            : new TreeFolderNode(string.Empty, "folder", new List<object>());

        return ToolSuccess(SerializeJson(rootNode));
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
