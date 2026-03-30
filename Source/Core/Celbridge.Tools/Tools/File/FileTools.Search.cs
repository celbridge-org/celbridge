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
    /// Supports ** for matching across path separators (e.g. "**/Commands/*.cs").
    /// </summary>
    /// <param name="pattern">Glob pattern to match resource paths (e.g. "*.py", "**/Commands/*.cs", "Services/**/I*.cs").</param>
    /// <param name="includeMetadata">When true, returns objects with resource, size, and modified fields instead of plain resource key strings.</param>
    /// <param name="type">Resource type to search: "file" (default) or "folder".</param>
    /// <returns>When includeMetadata is false: JSON array of matching resource key strings. When true: JSON array of objects with resource (string), size (long), modified (string, ISO 8601).</returns>
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
                return SuccessResult(SerializeJson(results));
            }

            var folderStrings = matchingFolders.Select(key => key.ToString()).ToList();
            return SuccessResult(SerializeJson(folderStrings));
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
            return SuccessResult(SerializeJson(results));
        }

        var resourceStrings = matches.Select(r => r.Resource.ToString()).ToList();
        return SuccessResult(SerializeJson(resourceStrings));
    }
}
