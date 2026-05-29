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
        var resourceRegistry = resourceService.Registry;
        var rootHandlerRegistry = resourceService.RootHandlerRegistry;
        var fileStorage = workspaceWrapper.WorkspaceService.FileStorage;

        var regexPattern = GlobHelper.PathGlobToRegex(pattern);
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

        var isFolderSearch = string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase);

        // When the pattern carries a non-default root prefix (logs:, temp:), walk
        // that root's filesystem tree via the chokepoint. Patterns with no prefix
        // or the project: prefix fall through to the existing in-memory tree path.
        var patternRoot = ExtractRootPrefix(pattern);
        if (patternRoot is not null
            && patternRoot != ResourceKey.DefaultRoot
            && rootHandlerRegistry.RootHandlers.ContainsKey(patternRoot))
        {
            return await SearchNonDefaultRootAsync(
                fileStorage, patternRoot, regex, isFolderSearch, includeMetadata);
        }

        if (isFolderSearch)
        {
            var folderKeys = new List<ResourceKey>();
            CollectFolderResources(resourceRegistry.ProjectFolder, resourceRegistry, folderKeys);

            var matchingFolders = folderKeys
                .Where(key => regex.IsMatch(key.ToString()))
                .ToList();

            if (includeMetadata)
            {
                var results = new List<SearchResultWithMetadata>();
                foreach (var folderKey in matchingFolders)
                {
                    var infoResult = await fileStorage.GetInfoAsync(folderKey);
                    if (infoResult.IsFailure
                        || infoResult.Value.Kind != StorageItemKind.Folder)
                    {
                        continue;
                    }
                    results.Add(new SearchResultWithMetadata(
                        folderKey.ToString(),
                        0,
                        infoResult.Value.ModifiedUtc.ToString("o")));
                }
                return ToolResponse.Success(SerializeJson(results));
            }

            var folderStrings = matchingFolders.Select(key => key.ToString()).ToList();
            return ToolResponse.Success(SerializeJson(folderStrings));
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
                var infoResult = await fileStorage.GetInfoAsync(match.Resource);
                if (infoResult.IsFailure
                    || infoResult.Value.Kind != StorageItemKind.File)
                {
                    continue;
                }
                results.Add(new SearchResultWithMetadata(
                    match.Resource.ToString(),
                    infoResult.Value.Size,
                    infoResult.Value.ModifiedUtc.ToString("o")));
            }
            return ToolResponse.Success(SerializeJson(results));
        }

        var resourceStrings = matches.Select(r => r.Resource.ToString()).ToList();
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

    private async Task<CallToolResult> SearchNonDefaultRootAsync(
        IFileStorage fileStorage,
        string rootName,
        Regex regex,
        bool isFolderSearch,
        bool includeMetadata)
    {
        var rootKey = new ResourceKey(rootName + ":");
        var allEntries = new List<FolderItem>();
        await CollectRecursiveAsync(fileStorage, rootKey, allEntries);

        var matches = allEntries
            .Where(entry => entry.IsFolder == isFolderSearch)
            .Where(entry => regex.IsMatch(entry.Resource.ToString()))
            .ToList();

        if (includeMetadata)
        {
            var results = matches
                .Select(entry => new SearchResultWithMetadata(
                    entry.Resource.ToString(),
                    entry.IsFolder ? 0 : entry.Size,
                    entry.ModifiedUtc.ToString("o")))
                .ToList();
            return ToolResponse.Success(SerializeJson(results));
        }

        var resourceStrings = matches.Select(entry => entry.Resource.ToString()).ToList();
        return ToolResponse.Success(SerializeJson(resourceStrings));
    }

    private static async Task CollectRecursiveAsync(
        IFileStorage fileStorage,
        ResourceKey folder,
        List<FolderItem> entries)
    {
        var enumerateResult = await fileStorage.EnumerateFolderAsync(folder);
        if (enumerateResult.IsFailure)
        {
            return;
        }

        foreach (var entry in enumerateResult.Value)
        {
            entries.Add(entry);
            if (entry.IsFolder)
            {
                await CollectRecursiveAsync(fileStorage, entry.Resource, entries);
            }
        }
    }
}
