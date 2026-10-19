using System.Collections.Concurrent;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;
using Tomlyn.Model;

namespace Celbridge.Resources.Services;

/// <summary>
/// Stateless, on-demand scanner that searches the project's sidecar files for tags.
/// </summary>
public sealed class ResourceScanner : IResourceScanner
{
    private const string TagsField = SidecarFieldNames.Tags;

    private readonly ILogger<ResourceScanner> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceScanner(
        ILogger<ResourceScanner> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<IReadOnlyList<string>> ListAllTagsAsync()
    {
        var tags = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        await EnumerateProjectSidecarFilesAsync(async (sidecarKey, _) =>
        {
            var text = await ReadFileTextAsync(sidecarKey);
            if (text is null)
            {
                return;
            }

            var parseResult = SidecarHelper.Parse(text);
            if (parseResult.IsFailure)
            {
                return;
            }

            if (!parseResult.Value.Fields.TryGetValue(TagsField, out var tagsValue))
            {
                return;
            }

            foreach (var item in SidecarHelper.ExtractStringList(tagsValue))
            {
                tags.TryAdd(item, 0);
            }
        });

        return tags.Keys
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<ResourceKey>> FindByTagAsync(string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return Array.Empty<ResourceKey>();
        }

        var matches = new ConcurrentBag<ResourceKey>();

        await EnumerateProjectSidecarFilesAsync(async (sidecarKey, parentKey) =>
        {
            var text = await ReadFileTextAsync(sidecarKey);
            if (text is null)
            {
                return;
            }

            // Pre-filter for the literal tag bytes before invoking the TOML
            // parser. SIMD-accelerated IndexOf keeps the cost of files-without-
            // the-tag close to a single memory scan.
            if (text.IndexOf(tag, StringComparison.Ordinal) < 0)
            {
                return;
            }

            var parseResult = SidecarHelper.Parse(text);
            if (parseResult.IsFailure)
            {
                return;
            }

            if (!parseResult.Value.Fields.TryGetValue(TagsField, out var tagsValue))
            {
                return;
            }

            if (ListContainsString(tagsValue, tag))
            {
                matches.Add(parentKey);
            }
        });

        return matches
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    // Returns null on any read failure; the caller treats unreadable files as
    // empty (they simply don't contribute matches).
    private async Task<string?> ReadFileTextAsync(ResourceKey resource)
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var readResult = await resourceFileSystem.ReadAllTextAsync(resource);
        if (readResult.IsFailure)
        {
            _logger.LogDebug($"scanner: read failed for {resource} ({readResult.FirstErrorMessage})");
            return null;
        }
        return readResult.Value;
    }

    // Walks the project, and visits each sidecar with the file it describes. Tag queries cover paired sidecars
    // only, so an orphan, whose file is gone, is left out.
    private async Task EnumerateProjectSidecarFilesAsync(Func<ResourceKey, ResourceKey, Task> visit)
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var sidecarService = resourceService.Sidecars;

        var walkResult = await resourceService.Walker.WalkAsync(ResourceKey.Empty);
        if (walkResult.IsFailure)
        {
            _logger.LogWarning(walkResult, "Failed to walk the project for sidecars");
            return;
        }

        var fileKeys = walkResult.Value
            .Where(item => !item.IsFolder)
            .Select(item => item.Resource);
        var sidecarPairs = SidecarPairing.Pair(fileKeys, sidecarService);

        await Parallel.ForEachAsync(sidecarPairs.FilesWithSidecars, async (file, _) =>
        {
            var sidecarKeyResult = sidecarService.GetSidecarKey(file);
            if (sidecarKeyResult.IsFailure)
            {
                return;
            }
            var sidecarKey = sidecarKeyResult.Value;

            await visit(sidecarKey, file);
        });
    }

    private static bool ListContainsString(object value, string candidate)
    {
        if (value is string)
        {
            return false;
        }

        if (value is TomlArray tomlArray)
        {
            foreach (var item in tomlArray)
            {
                if (item is string s
                    && string.Equals(s, candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is string s
                    && string.Equals(s, candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
