using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A file search result entry with metadata, returned by file_search when includeMetadata is true.
/// </summary>
public record class SearchResultWithMetadata(string Resource, long Size, string Modified);

public partial class FileTools
{
    /// <summary>Find files or folders by name or path using a search filter, like "*.py" or "src/**/*.cs".</summary>
    [McpServerTool(Name = "file_search", ReadOnly = true)]
    [ToolAlias("file.search")]
    [RelatedGuides("resource_keys", "search_filters")]
    public async partial Task<CallToolResult> Search(string pattern, bool includeMetadata = false, string type = "")
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceService = workspaceWrapper.WorkspaceService.ResourceService;
        var rootHandlerRegistry = resourceService.RootHandlers;

        var isFolderSearch = string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase);

        // A pattern that starts with another root's prefix (logs:, temp:) searches that root. Any other pattern searches
        // the project, after dropping any project: prefix. The rest of the pattern is a search filter, matched against
        // each item's path within its root.
        var rootKey = ResourceKey.Empty;
        var filterPattern = pattern;
        var patternRoot = ExtractRootPrefix(pattern);
        if (patternRoot == ResourceKey.DefaultRoot)
        {
            filterPattern = pattern.Substring(patternRoot.Length + 1);
        }
        else if (patternRoot is not null
            && rootHandlerRegistry.RootHandlers.ContainsKey(patternRoot))
        {
            rootKey = new ResourceKey(patternRoot + ":");
            filterPattern = pattern.Substring(patternRoot.Length + 1);
        }

        var filterResult = SearchFilter.Compile(filterPattern, string.Empty);
        if (filterResult.IsFailure)
        {
            return ToolResponse.Error(filterResult);
        }
        var filter = filterResult.Value;

        var walkResult = await resourceService.Walker.WalkAsync(rootKey);
        if (walkResult.IsFailure)
        {
            return ToolResponse.Error(walkResult);
        }
        var walkedItems = walkResult.Value;

        var matches = new List<WalkedItem>();
        foreach (var item in walkedItems)
        {
            if (item.IsFolder != isFolderSearch)
            {
                continue;
            }

            if (filter.IsMatch(item.Resource.Path, item.IsFolder))
            {
                matches.Add(item);
            }
        }

        matches.Sort((a, b) => string.Compare(a.Resource.ToString(), b.Resource.ToString(), StringComparison.OrdinalIgnoreCase));

        if (includeMetadata)
        {
            var results = matches
                .Select(item => new SearchResultWithMetadata(
                    item.Resource.ToString(),
                    item.IsFolder ? 0 : item.Size,
                    item.ModifiedUtc.ToString("o")))
                .ToList();
            return ToolResponse.Success(SerializeJson(results));
        }

        var resourceStrings = matches.Select(item => item.Resource.ToString()).ToList();
        return ToolResponse.Success(SerializeJson(resourceStrings));
    }

    // Pulls the "logs" out of "logs:*.log". Returns null when the pattern has
    // no root prefix or the part before ':' is not a valid root identifier shape.
    private static string? ExtractRootPrefix(string pattern)
    {
        var colonIndex = pattern.IndexOf(':');
        if (colonIndex <= 0)
        {
            return null;
        }
        return pattern.Substring(0, colonIndex);
    }
}
