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
    /// <summary>List the immediate (single-level) children of a folder, with an optional search filter.</summary>
    [McpServerTool(Name = "file_list_contents", ReadOnly = true)]
    [ToolAlias("file.list_contents")]
    [RelatedGuides("resource_keys", "search_filters")]
    public async partial Task<CallToolResult> ListContents(string resource, string glob = "")
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        // Route through the command queue so the snapshot follows every command
        // ahead of it in the queue.
        var listContentsResult = await ExecuteCommandAsync<IListFolderContentsCommand, FolderContentsSnapshot>(
            command => command.Resource = resourceKey);
        if (listContentsResult.IsFailure)
        {
            return ToolResponse.Error(listContentsResult);
        }
        var snapshot = listContentsResult.Value;

        var filterResult = SearchFilter.Compile(glob, string.Empty);
        if (filterResult.IsFailure)
        {
            return ToolResponse.Error(filterResult);
        }
        var filter = filterResult.Value;

        var items = new List<object>();
        foreach (var entry in snapshot.Entries)
        {
            var entryPath = entry.Name;
            if (!resourceKey.IsEmpty)
            {
                entryPath = $"{resourceKey.Path}/{entry.Name}";
            }

            if (!filter.IsMatch(entryPath, entry.IsFolder))
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
