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
    /// <summary>Find files or folders by name using a glob pattern matched against the resource path.</summary>
    [McpServerTool(Name = "file_search", ReadOnly = true)]
    [ToolAlias("file.search")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> Search(string pattern, bool includeMetadata = false, string type = "")
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceService = workspaceWrapper.WorkspaceService.ResourceService;
        var rootHandlerRegistry = resourceService.RootHandlers;

        var regexPattern = GlobHelper.PathGlobToRegex(pattern);
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

        var isFolderSearch = string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase);

        // A pattern that starts with another root's prefix (logs:, temp:) searches that root and matches the whole
        // key. Any other pattern searches the project and matches the key's path.
        var rootKey = ResourceKey.Empty;
        var matchesWholeKey = false;
        var patternRoot = ExtractRootPrefix(pattern);
        if (patternRoot is not null
            && patternRoot != ResourceKey.DefaultRoot
            && rootHandlerRegistry.RootHandlers.ContainsKey(patternRoot))
        {
            rootKey = new ResourceKey(patternRoot + ":");
            matchesWholeKey = true;
        }

        var walkResult = await resourceService.Walker.WalkAsync(rootKey);
        if (walkResult.IsFailure)
        {
            return ToolResponse.Error(walkResult);
        }

        var matches = new List<WalkedItem>();
        foreach (var item in walkResult.Value)
        {
            if (item.IsFolder != isFolderSearch)
            {
                continue;
            }

            var matchText = item.Resource.Path;
            if (matchesWholeKey)
            {
                matchText = item.Resource.ToString();
            }

            if (regex.IsMatch(matchText))
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

    // Pulls the "logs" out of "logs:**/*.log". Returns null when the pattern has
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
