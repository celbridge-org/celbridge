using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Resources.Services;

/// <summary>
/// On-disk JSON shape for the resource-metadata cache. Mirrors the in-memory
/// reference graph and frontmatter index entries plus the per-file mtime + size
/// stamp used to validate cache entries on load. Version is bumped whenever the
/// shape changes incompatibly so older caches discard cleanly on first read.
/// </summary>
internal record MetaDataCacheDocument
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("files")]
    public Dictionary<string, MetaDataCacheEntry> Files { get; init; } = new();
}

/// <summary>
/// One entry per indexed file. References / Frontmatter / IsText are optional
/// so a binary entry can be a stat-only record and a sidecar entry can carry
/// frontmatter without forcing the reference list to be present.
/// </summary>
internal record MetaDataCacheEntry
{
    [JsonPropertyName("mtimeUtcTicks")]
    public long MtimeUtcTicks { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("isText")]
    public bool IsText { get; init; }

    [JsonPropertyName("references")]
    public List<string>? References { get; init; }

    [JsonPropertyName("frontmatter")]
    public Dictionary<string, object>? Frontmatter { get; init; }
}

/// <summary>
/// Read/write logic for the .celbridge/cache/metadata.json file. The file is
/// host-private and does not flow through IResourceFileSystem — the FS layer's
/// structural operations depend on the metadata service, so routing the cache
/// through that layer would introduce a circular dependency.
/// </summary>
internal static class ResourceMetaDataCache
{
    /// <summary>
    /// Cache format version. Bump whenever the on-disk shape changes such that
    /// older readers cannot parse newer files (or vice versa). Mismatched
    /// versions are discarded silently and trigger a full rebuild.
    /// </summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Loads the cache document at the given path. Returns null on any failure
    /// (missing file, malformed JSON, version mismatch) so the caller can fall
    /// back to a full rebuild without distinguishing the failure cause.
    /// </summary>
    public static MetaDataCacheDocument? TryLoad(string cacheFilePath)
    {
        if (!File.Exists(cacheFilePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(cacheFilePath);
            var document = JsonSerializer.Deserialize<MetaDataCacheDocument>(stream, JsonOptions);
            if (document is null
                || document.Version != CurrentVersion)
            {
                return null;
            }
            return document;
        }
        catch
        {
            // Any read or parse failure is treated as a missing cache. The
            // service is correct without the cache; falling back to a full
            // rebuild is always safe.
            return null;
        }
    }

    /// <summary>
    /// Writes the cache document atomically via temp + move. Best-effort; a
    /// crash or IO failure leaves the cache untouched and the next load runs
    /// the full rebuild. Returns true on success, false otherwise.
    /// </summary>
    public static bool TrySave(string cacheFilePath, MetaDataCacheDocument document)
    {
        try
        {
            var folder = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrEmpty(folder)
                && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var tempPath = cacheFilePath + ".tmp";
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
            }
            File.Move(tempPath, cacheFilePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
