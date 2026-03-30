using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A file entry in the file_list_contents output.
/// </summary>
public record class ListContentsFileItem(string Name, string Type, long Size, string Modified);

/// <summary>
/// A folder entry in the file_list_contents output.
/// </summary>
public record class ListContentsFolderItem(string Name, string Type, string Modified);

public partial class FileTools
{
    /// <summary>
    /// Lists the immediate children of a folder with their type, size, and modification date.
    /// Optionally filters children by a glob pattern.
    /// </summary>
    /// <param name="resource">Resource key of the folder to list.</param>
    /// <param name="glob">Optional glob pattern to filter children by name (e.g. "*.py", "readme*"). When empty, all children are returned.</param>
    /// <returns>JSON array of objects with fields: name (string), type (string: "file" or "folder"), size (long, files only), modified (string, ISO 8601).</returns>
    [McpServerTool(Name = "file_list_contents", ReadOnly = true)]
    [ToolAlias("file.list_contents")]
    public partial CallToolResult ListContents(string resource, string glob = "")
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

        var items = new List<object>();
        foreach (var child in folderResource.Children)
        {
            if (globRegex is not null && !globRegex.IsMatch(child.Name))
            {
                continue;
            }

            var childKey = resourceRegistry.GetResourceKey(child);

            if (child is IFolderResource)
            {
                var resolveChildResult = resourceRegistry.ResolveResourcePath(childKey);
                if (resolveChildResult.IsFailure)
                {
                    continue;
                }
                var directoryInfo = new DirectoryInfo(resolveChildResult.Value);
                var folderItem = new ListContentsFolderItem(
                    child.Name,
                    "folder",
                    directoryInfo.LastWriteTimeUtc.ToString("o"));
                items.Add(folderItem);
            }
            else
            {
                var resolveChildResult = resourceRegistry.ResolveResourcePath(childKey);
                if (resolveChildResult.IsFailure)
                {
                    continue;
                }
                var fileInfo = new FileInfo(resolveChildResult.Value);
                var fileItem = new ListContentsFileItem(
                    child.Name,
                    "file",
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc.ToString("o"));
                items.Add(fileItem);
            }
        }

        return SuccessResult(SerializeJson(items));
    }
}
