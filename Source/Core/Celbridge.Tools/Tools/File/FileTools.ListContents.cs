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
    /// <summary>List the immediate (single-level) children of a folder, with optional glob filter.</summary>
    [McpServerTool(Name = "file_list_contents", ReadOnly = true)]
    [ToolAlias("file.list_contents")]
    public async partial Task<CallToolResult> ListContents(string resource, string glob = "")
    {
        const string ToolGuide = "file_list_contents";

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        // Route through the command queue so the snapshot observes state after all
        // previously enqueued commands have run. The command reads directly from the
        // resource registry on the command thread, which is the synchronization point
        // for registry mutations (folder adds, renames, etc).
        var listContentsResult = await ExecuteCommandAsync<IListFolderContentsCommand, FolderContentsSnapshot>(
            command => command.Resource = resourceKey);
        if (listContentsResult.IsFailure)
        {
            return ToolResponse.Error(listContentsResult, ToolGuide);
        }
        var snapshot = listContentsResult.Value;

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

        return ToolResponse.Success(SerializeJson(items));
    }
}
