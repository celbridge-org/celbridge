using System.Text.RegularExpressions;
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
    public partial CallToolResult GetTree(string resource, int depth = 3, string glob = "", string type = "")
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(resourceKey);
        if (getResult.IsFailure)
        {
            return ErrorResult($"Resource not found: '{resource}'");
        }

        if (getResult.Value is not IFolderResource folderResource)
        {
            return ErrorResult($"Resource is not a folder: '{resource}'");
        }

        Regex? globRegex = null;
        if (!string.IsNullOrEmpty(glob))
        {
            var regexPattern = GlobHelper.GlobToRegex(glob);
            globRegex = new Regex(regexPattern, RegexOptions.IgnoreCase);
        }

        var tree = FileToolHelpers.BuildTree(folderResource, depth, globRegex, type)
            ?? new TreeFolderNode(folderResource.Name, "folder", new List<object>());
        return SuccessResult(SerializeJson(tree));
    }
}
