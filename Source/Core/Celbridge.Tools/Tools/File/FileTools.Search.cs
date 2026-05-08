using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A file search result entry with metadata, returned by file_search when includeMetadata is true.
/// </summary>
public record class SearchResultWithMetadata(string Resource, long Size, string Modified);

public partial class FileTools
{
    /// <summary>
    /// Searches for resources by name using a glob pattern matched against the full resource path.
    /// </summary>
    /// <param name="pattern">Glob pattern. See guides_read(['file_search']) for recursive vs. anchored semantics.</param>
    /// <param name="includeMetadata">When true, returns objects with size and modified instead of plain resource keys.</param>
    /// <param name="type">"file" (default) or "folder".</param>
    /// <returns>JSON array of matching resource keys, or objects with metadata when includeMetadata is true.</returns>
    [McpServerTool(Name = "file_search", ReadOnly = true)]
    [ToolAlias("file.search")]
    public partial CallToolResult Search(string pattern, bool includeMetadata = false, string type = "")
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var regexPattern = GlobHelper.PathGlobToRegex(pattern);
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

        var isFolderSearch = string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase);

        if (isFolderSearch)
        {
            var folderKeys = new List<ResourceKey>();
            CollectFolderResources(resourceRegistry.RootFolder, resourceRegistry, folderKeys);

            var matchingFolders = folderKeys
                .Where(key => regex.IsMatch(key.ToString()))
                .ToList();

            if (includeMetadata)
            {
                var results = new List<SearchResultWithMetadata>();
                foreach (var folderKey in matchingFolders)
                {
                    var resolvePathResult = resourceRegistry.ResolveResourcePath(folderKey);
                    if (resolvePathResult.IsFailure)
                    {
                        continue;
                    }
                    var directoryInfo = new DirectoryInfo(resolvePathResult.Value);
                    results.Add(new SearchResultWithMetadata(
                        folderKey.ToString(),
                        0,
                        directoryInfo.LastWriteTimeUtc.ToString("o")));
                }
                return ToolSuccess(SerializeJson(results));
            }

            var folderStrings = matchingFolders.Select(key => key.ToString()).ToList();
            return ToolSuccess(SerializeJson(folderStrings));
        }

        var allResources = resourceRegistry.GetAllFileResources();

        var matches = allResources
            .Where(r => regex.IsMatch(r.Resource.ToString()))
            .ToList();

        if (includeMetadata)
        {
            var results = new List<SearchResultWithMetadata>();
            foreach (var match in matches)
            {
                var fileInfo = new FileInfo(match.Path);
                results.Add(new SearchResultWithMetadata(
                    match.Resource.ToString(),
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc.ToString("o")));
            }
            return ToolSuccess(SerializeJson(results));
        }

        var resourceStrings = matches.Select(r => r.Resource.ToString()).ToList();
        return ToolSuccess(SerializeJson(resourceStrings));
    }
}
