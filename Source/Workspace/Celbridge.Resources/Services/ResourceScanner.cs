using System.Collections.Concurrent;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Tomlyn.Model;

namespace Celbridge.Resources.Services;

/// <summary>
/// One-shot, stateless on-demand scanner over the project's text files. The
/// rename cascade, ProjectCheckCommand, and the data_find_tag tool all consume
/// the same instance. Each call walks the registry's known files in parallel
/// via IResourceFileSystem; the OS page cache absorbs repeated reads. No
/// in-memory index, no persistent cache.
/// </summary>
public sealed class ResourceScanner : IResourceScanner
{
    private const string TagsField = "tags";

    private readonly ILogger<ResourceScanner> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ITextBinarySniffer _textBinarySniffer;

    public ResourceScanner(
        ILogger<ResourceScanner> logger,
        IWorkspaceWrapper workspaceWrapper,
        ITextBinarySniffer textBinarySniffer)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _textBinarySniffer = textBinarySniffer;
    }

    public async Task<IReadOnlyList<ResourceKey>> FindReferencersAsync(ResourceKey target)
    {
        var matches = new ConcurrentBag<ResourceKey>();

        await EnumerateProjectTextFilesAsync(async (resourceKey, _) =>
        {
            var readResult = await ReadFileTextAsync(resourceKey);
            if (readResult is null)
            {
                return;
            }

            if (ContainsReferenceTo(readResult, target))
            {
                matches.Add(resourceKey);
            }
        });

        return matches
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<ResourceKey>> FindReferencesInAsync(ResourceKey source)
    {
        var text = await ReadFileTextAsync(source);
        if (text is null)
        {
            return Array.Empty<ResourceKey>();
        }
        return ScanReferences(text).ToList();
    }

    public async Task<IReadOnlyList<ResourceKey>> FindAllReferencedTargetsAsync()
    {
        var targets = new ConcurrentDictionary<ResourceKey, byte>();

        await EnumerateProjectTextFilesAsync(async (resourceKey, _) =>
        {
            var text = await ReadFileTextAsync(resourceKey);
            if (text is null)
            {
                return;
            }

            foreach (var target in ScanReferences(text))
            {
                targets.TryAdd(target, 0);
            }
        });

        return targets.Keys
            .OrderBy(t => t.ToString(), StringComparer.Ordinal)
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

            if (!parseResult.Value.Frontmatter.TryGetValue(TagsField, out var tagsValue))
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

    // Reads a file through IResourceFileSystem so atomic-read + retry semantics
    // apply uniformly. Returns null on any read failure; the caller treats
    // unreadable files as empty (they simply don't contribute matches).
    private async Task<string?> ReadFileTextAsync(ResourceKey resource)
    {
        var fileSystem = _workspaceWrapper.WorkspaceService.ResourceFileSystem;
        var readResult = await fileSystem.ReadAllTextAsync(resource);
        if (readResult.IsFailure)
        {
            _logger.LogDebug($"scanner: read failed for {resource} ({readResult.FirstErrorMessage})");
            return null;
        }
        return readResult.Value;
    }

    // True when `text` contains a tracked "project:<target>" reference. The
    // boundary rules in ReferenceLiteralRules constrain the match to canonical
    // quoted forms.
    private static bool ContainsReferenceTo(string text, ResourceKey target)
    {
        var marker = ReferenceLiteralRules.ReferenceMarker;
        int searchStart = 0;
        while (true)
        {
            int markerIndex = text.IndexOf(marker, searchStart, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return false;
            }

            var parsed = ReferenceLiteralRules.TryParseReferenceAt(text, markerIndex);
            if (parsed is not null
                && parsed.Key.Equals(target))
            {
                return true;
            }

            searchStart = parsed?.EndIndex ?? markerIndex + marker.Length;
        }
    }

    // Returns the distinct set of "project:" keys named in `text`.
    private static HashSet<ResourceKey> ScanReferences(string text)
    {
        var references = new HashSet<ResourceKey>();
        var marker = ReferenceLiteralRules.ReferenceMarker;
        int searchStart = 0;
        while (true)
        {
            int markerIndex = text.IndexOf(marker, searchStart, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                break;
            }

            var parsed = ReferenceLiteralRules.TryParseReferenceAt(text, markerIndex);
            if (parsed is not null)
            {
                references.Add(parsed.Key);
                searchStart = parsed.EndIndex;
            }
            else
            {
                searchStart = markerIndex + marker.Length;
            }
        }
        return references;
    }

    // Walks all project: text files in parallel, invoking the visitor for
    // each. Reads the registry's snapshot directly; mutation commands carry
    // CommandFlags.UpdateResources so the snapshot reflects the latest disk
    // state by the time a tool that consults the scanner runs. Binary files
    // are skipped via the extension sniffer; unknown extensions trigger a
    // one-time content sniff cached for the scan's duration.
    private async Task EnumerateProjectTextFilesAsync(Func<ResourceKey, string, Task> visit)
    {
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var files = registry.GetAllFileResources(ResourceKey.DefaultRoot);

        var textSniffCache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        await Parallel.ForEachAsync(files, async (file, _) =>
        {
            var (resourceKey, absolutePath) = file;
            if (!IsScannableTextFile(absolutePath, textSniffCache))
            {
                return;
            }

            await visit(resourceKey, absolutePath);
        });
    }

    // Walks all .cel files paired with an existing parent file. Reads the
    // registry's snapshot directly; mutation commands carry
    // CommandFlags.UpdateResources so the snapshot reflects the latest disk
    // state. Orphans (no parent file on disk) are skipped — tag queries are
    // scoped to paired sidecars; orphans surface via data_check_project.
    private async Task EnumerateProjectSidecarFilesAsync(Func<ResourceKey, ResourceKey, Task> visit)
    {
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var fileSystem = _workspaceWrapper.WorkspaceService.ResourceFileSystem;
        var files = registry.GetAllFileResources(ResourceKey.DefaultRoot);

        await Parallel.ForEachAsync(files, async (file, _) =>
        {
            var (resourceKey, absolutePath) = file;
            if (!IsSidecarPath(absolutePath))
            {
                return;
            }

            var parentKey = StripSidecarSuffix(resourceKey);
            if (parentKey is null)
            {
                return;
            }

            var existsResult = await fileSystem.ExistsAsync(parentKey.Value);
            if (existsResult.IsFailure
                || !existsResult.Value)
            {
                return;
            }

            await visit(resourceKey, parentKey.Value);
        });
    }

    private bool IsScannableTextFile(
        string absolutePath,
        ConcurrentDictionary<string, bool> textSniffCache)
    {
        var extension = Path.GetExtension(absolutePath);
        if (ResourceScanRules.IsExcludedExtension(extension))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(extension)
            && _textBinarySniffer.IsBinaryExtension(extension))
        {
            return false;
        }

        // Unknown-extension files: content-sniff once per scan. The cache is
        // local to this scan; we don't keep it across calls because the
        // registry rebuilds frequently and the per-scan cost is small.
        if (string.IsNullOrEmpty(extension))
        {
            return textSniffCache.GetOrAdd(absolutePath, path =>
            {
                var sniff = _textBinarySniffer.IsTextFile(path);
                return sniff.IsSuccess && sniff.Value;
            });
        }

        return true;
    }

    private static bool IsSidecarPath(string absolutePath)
    {
        var fileName = Path.GetFileName(absolutePath);
        if (!fileName.EndsWith(SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        // .cel.cel files are invalid sidecars by definition.
        if (fileName.EndsWith(SidecarHelper.Extension + SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    // Strips the trailing ".cel" from a sidecar key to recover its parent
    // resource key. Returns null when the result would be empty.
    private static ResourceKey? StripSidecarSuffix(ResourceKey sidecarKey)
    {
        var fullKey = sidecarKey.FullKey;
        if (!fullKey.EndsWith(SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var trimmed = fullKey.Substring(0, fullKey.Length - SidecarHelper.Extension.Length);
        if (string.IsNullOrEmpty(trimmed)
            || trimmed.EndsWith(":", StringComparison.Ordinal))
        {
            return null;
        }
        return new ResourceKey(trimmed);
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
