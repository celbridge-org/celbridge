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
    public async partial Task<CallToolResult> ListContents(string resource, string glob = "")
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        // Route through the command queue so the snapshot observes state after all
        // previously enqueued commands have run. The command reads directly from the
        // resource registry on the command thread, which is the synchronization point
        // for registry mutations (folder adds, renames, etc).
        var (callResult, snapshot) = await ExecuteCommandAsync<IListFolderContentsCommand, FolderContentsSnapshot>(
            command => command.Resource = resourceKey);
        if (callResult.IsError == true || snapshot is null)
        {
            return callResult;
        }

        Regex? globRegex = null;
        if (!string.IsNullOrEmpty(glob))
        {
            var regexPattern = GlobHelper.GlobToRegex(glob);
            globRegex = new Regex(regexPattern, RegexOptions.IgnoreCase);
        }

        var items = new List<object>();
        foreach (var entry in snapshot.Entries)
        {
            if (globRegex is not null && !globRegex.IsMatch(entry.Name))
            {
                continue;
            }

            if (entry.IsFolder)
            {
                items.Add(new ListContentsFolderItem(
                    entry.Name,
                    "folder",
                    entry.ModifiedUtc.ToString("o")));
            }
            else
            {
                items.Add(new ListContentsFileItem(
                    entry.Name,
                    "file",
                    entry.Size,
                    entry.ModifiedUtc.ToString("o")));
            }
        }

        return SuccessResult(SerializeJson(items));
    }
}
