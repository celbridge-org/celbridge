using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;
using Tomlyn.Model;

namespace Celbridge.Resources.Services;

/// <summary>
/// Workspace-scoped reference graph and frontmatter index for project resources.
/// </summary>
public sealed class ResourceMetaData : IResourceMetaData, IDisposable
{
    // Files larger than this byte budget are skipped during the scan.
    private const long MaxScanFileSizeBytes = 10 * 1024 * 1024;

    // The standardised list-of-string field exposed via metadata_add_tag /
    // metadata_remove_tag / FindByTag.
    private const string TagsField = "tags";

    // Re-queue delays for a transient rescan failure (file locked by external
    // writer, antivirus, etc.). The retry attempt counter resets when any
    // watcher event arrives for the resource, so normal user activity always
    // gets a fresh budget. After MaxScanRetryAttempts consecutive transient
    // failures the rescan is dropped (logged) until the next watcher event.
    private const int MaxScanRetryAttempts = 3;
    private static readonly TimeSpan[] ScanRetryDelays =
    {
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
    };

    // Delimiter and boundary rules live in ReferenceLiteralRules so the scanner
    // and the rewrite cascade in ResourceFileSystem cannot drift on what
    // constitutes a valid reference.

    private readonly ILogger<ResourceMetaData> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ITextBinarySniffer _textBinarySniffer;

    private readonly object _indexLock = new();
    private readonly Dictionary<ResourceKey, HashSet<ResourceKey>> _referencersByTarget = new();
    private readonly Dictionary<ResourceKey, HashSet<ResourceKey>> _referencesBySource = new();

    // Per-resource snapshot of the parsed sidecar frontmatter as a top-level
    // field map. Keyed on the parent resource (e.g. "foo.png"), not the sidecar
    // key. Absent entries mean "no frontmatter indexed for this parent" — either
    // the parent has no sidecar, the sidecar is unparseable, or the sidecar's
    // top-level fields are all non-indexable shapes.
    private readonly Dictionary<ResourceKey, IReadOnlyDictionary<string, object>> _frontmatterByResource = new();

    // Inverted index from field -> indexed value -> set of resources carrying
    // that value in their sidecar frontmatter. Scalar fields contribute their
    // value directly; list-of-scalar fields contribute each element. Object /
    // nested fields are stored in _frontmatterByResource but not indexed here.
    private readonly Dictionary<string, Dictionary<object, HashSet<ResourceKey>>> _resourcesByMetaDataField =
        new(StringComparer.Ordinal);

    // The pending-rescan queue. Watcher events push file keys onto this; the
    // background worker drains them. WaitForPendingUpdatesAsync awaits the
    // worker when it sees a non-empty queue.
    private readonly ConcurrentQueue<ResourceKey> _pendingRescans = new();
    private readonly SemaphoreSlim _workerSignal = new(0);
    private Task? _workerTask;

    // Per-resource counter for consecutive transient rescan failures.
    // Cleared on a successful scan, a permanent exclusion, or a new watcher
    // event. Used by ScheduleRetryAfterTransientFailure to cap the retry chain.
    private readonly ConcurrentDictionary<ResourceKey, int> _transientFailureCounts = new();

    // Per-file mtime + size + isText stamp, captured at scan time and persisted
    // to the cache file. The dictionary key is the file's resource key relative
    // to the project root; the value is the stamp at the last successful scan.
    // Kept in sync with the index dictionaries (entries are added when a file
    // is indexed, removed when it is dropped). Guarded by _indexLock.
    private readonly Dictionary<ResourceKey, CacheStamp> _cacheStamps = new();

    private TaskCompletionSource<bool> _readyCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isReady;
    private volatile bool _isShuttingDown;
    private bool _isDisposed;

    // Debounced cache-save tracker. _isDirty is set to true on every index
    // mutation; the worker checks it after draining the pending-rescan queue
    // and, if enough time has passed since the last save, persists a snapshot.
    private volatile bool _isDirty;
    private DateTime _lastCacheSaveUtc = DateTime.MinValue;
    private static readonly TimeSpan MinCacheSaveInterval = TimeSpan.FromSeconds(30);

    private record CacheStamp(long MtimeUtcTicks, long Size, bool IsText);

    public bool IsReady => _isReady;

    public ResourceMetaData(
        ILogger<ResourceMetaData> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ITextBinarySniffer textBinarySniffer)
    {
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _textBinarySniffer = textBinarySniffer;

        _messengerService.Register<ResourceCreatedMessage>(this, OnResourceCreated);
        _messengerService.Register<ResourceChangedMessage>(this, OnResourceChanged);
        _messengerService.Register<ResourceDeletedMessage>(this, OnResourceDeleted);
        _messengerService.Register<ResourceRenamedMessage>(this, OnResourceRenamed);

        _workerTask = Task.Run(WorkerLoopAsync);
    }

    public Task WaitUntilReadyAsync()
    {
        if (_isReady)
        {
            return Task.CompletedTask;
        }

        return _readyCompletionSource.Task;
    }

    public async Task WaitForPendingUpdatesAsync()
    {
        // Spin-wait while the queue still has items or the worker is mid-flight.
        // The worker drains items in order, so once the queue is empty and the
        // worker is idle, every prior watcher event has been applied.
        while (!_pendingRescans.IsEmpty)
        {
            await Task.Delay(10);
        }
    }

    public async Task<Result<MetaDataScanReport>> RebuildAsync()
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // WorkspaceService is available as soon as the wrapper has been populated,
            // which happens before the workspace page UI loads. The rebuild can run
            // during that window. WorkspaceService throws InvalidOperationException if
            // no workspace is present; that's caught by the outer try.
            var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
            var files = registry.GetAllFileResources(ResourceKey.DefaultRoot);

            // Try to hydrate from the persisted cache first. Entries whose
            // mtime + size match the on-disk stat populate the indexes
            // directly; entries that don't validate fall through to a full
            // scan. The cache is host-private and may be missing or stale —
            // both cases produce correct behaviour after fallback.
            var cacheDocument = LoadCacheDocument(registry);

            var newReferencersByTarget = new Dictionary<ResourceKey, HashSet<ResourceKey>>();
            var newReferencesBySource = new Dictionary<ResourceKey, HashSet<ResourceKey>>();
            var newFrontmatterByResource = new Dictionary<ResourceKey, IReadOnlyDictionary<string, object>>();
            var newResourcesByMetaDataField = new Dictionary<string, Dictionary<object, HashSet<ResourceKey>>>(StringComparer.Ordinal);
            var newCacheStamps = new Dictionary<ResourceKey, CacheStamp>();
            var transientFailures = new List<ResourceKey>();

            int filesScanned = 0;
            int filesSkipped = 0;
            int filesHydratedFromCache = 0;
            int referencesFound = 0;
            int frontmatterEntries = 0;

            foreach (var (resourceKey, absolutePath) in files)
            {
                // Cache hit path: stat the file and compare against the cached
                // mtime+size. A match means the cached data is good and we can
                // skip the scan entirely.
                if (TryHydrateFromCache(
                        cacheDocument,
                        registry,
                        resourceKey,
                        absolutePath,
                        newReferencersByTarget,
                        newReferencesBySource,
                        newFrontmatterByResource,
                        newResourcesByMetaDataField,
                        newCacheStamps,
                        ref referencesFound,
                        ref frontmatterEntries))
                {
                    filesHydratedFromCache++;
                    continue;
                }

                var scanResult = await ScanTextFileAsync(resourceKey, absolutePath);
                switch (scanResult.Outcome)
                {
                    case ScanOutcome.TransientFailure:
                        // Re-queue once the swap below is complete so the worker
                        // picks it up. Don't include in the new index yet.
                        transientFailures.Add(resourceKey);
                        filesSkipped++;
                        continue;

                    case ScanOutcome.ExcludedPermanently:
                        filesSkipped++;
                        // Capture the stamp even for excluded files so the
                        // cache can record "this is binary, don't re-sniff"
                        // and the next load skips the sniff cost.
                        TryRecordExcludedStamp(absolutePath, resourceKey, newCacheStamps);
                        continue;

                    case ScanOutcome.Indexed:
                        filesScanned++;
                        if (scanResult.References.Count > 0)
                        {
                            referencesFound += scanResult.References.Count;
                            ApplyReferences(newReferencersByTarget, newReferencesBySource, resourceKey, scanResult.References);
                        }

                        if (IsSidecarPath(absolutePath))
                        {
                            // For sidecars, the frontmatter is indexed against
                            // the parent resource (the file the sidecar
                            // describes), not the sidecar key itself. The
                            // pairing pass in ResourceRegistry derives the
                            // parent for us — but only ".cel" files (not
                            // ".cel.cel") are valid sidecars.
                            var parentResult = registry.GetSidecarParent(resourceKey);
                            if (parentResult.IsSuccess)
                            {
                                var parentKey = registry.GetResourceKey(parentResult.Value);
                                var parsed = TryParseSidecarFrontmatter(absolutePath, scanResult.SidecarText);
                                if (parsed is not null
                                    && parsed.Count > 0)
                                {
                                    newFrontmatterByResource[parentKey] = parsed;
                                    ApplyFrontmatter(newResourcesByMetaDataField, parentKey, parsed);
                                    frontmatterEntries++;
                                }
                            }
                        }

                        TryRecordScannedStamp(absolutePath, resourceKey, isText: true, newCacheStamps);
                        break;
                }
            }

            lock (_indexLock)
            {
                _referencersByTarget.Clear();
                foreach (var entry in newReferencersByTarget)
                {
                    _referencersByTarget[entry.Key] = entry.Value;
                }
                _referencesBySource.Clear();
                foreach (var entry in newReferencesBySource)
                {
                    _referencesBySource[entry.Key] = entry.Value;
                }

                _frontmatterByResource.Clear();
                foreach (var entry in newFrontmatterByResource)
                {
                    _frontmatterByResource[entry.Key] = entry.Value;
                }
                _resourcesByMetaDataField.Clear();
                foreach (var entry in newResourcesByMetaDataField)
                {
                    _resourcesByMetaDataField[entry.Key] = entry.Value;
                }

                _cacheStamps.Clear();
                foreach (var entry in newCacheStamps)
                {
                    _cacheStamps[entry.Key] = entry.Value;
                }
            }

            stopwatch.Stop();

            // Enqueue the transient failures after the index swap so the
            // worker's retry attempts mutate the freshly-installed index, not
            // the prior one.
            foreach (var failed in transientFailures)
            {
                QueueRescan(failed);
            }

            MarkReady();

            var report = new MetaDataScanReport(
                FilesScanned: filesScanned,
                FilesSkipped: filesSkipped,
                ReferencesFound: referencesFound,
                FrontmatterEntries: frontmatterEntries,
                Elapsed: stopwatch.Elapsed);

            _logger.LogInformation($"Metadata rebuild complete: {filesScanned} scanned, {filesHydratedFromCache} hydrated from cache, {filesSkipped} skipped ({transientFailures.Count} transient retries queued), {referencesFound} references, {frontmatterEntries} sidecars in {stopwatch.ElapsedMilliseconds}ms");

            // First save after rebuild — schedule for the next worker tick so
            // we don't block the project-load path on disk I/O.
            MarkDirty();

            return Result<MetaDataScanReport>.Ok(report);
        }
        catch (Exception ex)
        {
            return Result<MetaDataScanReport>.Fail("An exception occurred during the metadata rebuild.")
                .WithException(ex);
        }
    }

    // Returns the on-disk cache file path for the current project. Null when
    // the workspace has no project folder configured.
    private string? GetCacheFilePath()
    {
        try
        {
            var projectFolder = _workspaceWrapper.WorkspaceService.ResourceService.Registry.ProjectFolderPath;
            if (string.IsNullOrEmpty(projectFolder))
            {
                return null;
            }
            return Path.Combine(
                projectFolder,
                Celbridge.Projects.ProjectConstants.CelbridgeFolder,
                Celbridge.Projects.ProjectConstants.CelbridgeCacheFolder,
                Celbridge.Projects.ProjectConstants.MetaDataCacheFileName);
        }
        catch
        {
            return null;
        }
    }

    private MetaDataCacheDocument? LoadCacheDocument(IResourceRegistry registry)
    {
        var path = GetCacheFilePath();
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }
        return ResourceMetaDataCache.TryLoad(path);
    }

    // Looks up the file's cache entry, validates mtime + size, and applies the
    // cached references / frontmatter into the new index dictionaries. Returns
    // true on a clean hydration; false otherwise (the caller falls back to a
    // fresh scan).
    private bool TryHydrateFromCache(
        MetaDataCacheDocument? cacheDocument,
        IResourceRegistry registry,
        ResourceKey resourceKey,
        string absolutePath,
        Dictionary<ResourceKey, HashSet<ResourceKey>> referencersByTarget,
        Dictionary<ResourceKey, HashSet<ResourceKey>> referencesBySource,
        Dictionary<ResourceKey, IReadOnlyDictionary<string, object>> frontmatterByResource,
        Dictionary<string, Dictionary<object, HashSet<ResourceKey>>> resourcesByMetaDataField,
        Dictionary<ResourceKey, CacheStamp> cacheStamps,
        ref int referencesFound,
        ref int frontmatterEntries)
    {
        if (cacheDocument is null)
        {
            return false;
        }

        if (!cacheDocument.Files.TryGetValue(resourceKey.ToString(), out var entry))
        {
            return false;
        }

        long mtimeTicks;
        long size;
        try
        {
            var fileInfo = new FileInfo(absolutePath);
            if (!fileInfo.Exists)
            {
                return false;
            }
            mtimeTicks = fileInfo.LastWriteTimeUtc.Ticks;
            size = fileInfo.Length;
        }
        catch
        {
            return false;
        }

        if (entry.MtimeUtcTicks != mtimeTicks
            || entry.Size != size)
        {
            return false;
        }

        cacheStamps[resourceKey] = new CacheStamp(mtimeTicks, size, entry.IsText);

        if (!entry.IsText)
        {
            // Binary entry: stamp only, no index population.
            return true;
        }

        if (entry.References is { Count: > 0 })
        {
            var references = new HashSet<ResourceKey>();
            foreach (var raw in entry.References)
            {
                if (ResourceKey.TryCreate(raw, out var key))
                {
                    references.Add(key);
                }
            }
            if (references.Count > 0)
            {
                referencesFound += references.Count;
                ApplyReferences(referencersByTarget, referencesBySource, resourceKey, references);
            }
        }

        if (entry.Frontmatter is { Count: > 0 })
        {
            // Frontmatter index entries are keyed against the parent resource.
            var parentResult = registry.GetSidecarParent(resourceKey);
            if (parentResult.IsSuccess)
            {
                var parentKey = registry.GetResourceKey(parentResult.Value);
                var normalised = NormaliseJsonFrontmatter(entry.Frontmatter);
                if (normalised.Count > 0)
                {
                    frontmatterByResource[parentKey] = normalised;
                    ApplyFrontmatter(resourcesByMetaDataField, parentKey, normalised);
                    frontmatterEntries++;
                }
            }
        }

        return true;
    }

    // Walks the cached frontmatter dictionary (deserialised from JSON, so
    // numbers come out as JsonElement or boxed long/double) and normalises
    // each value into the same CLR shape produced by the live TOML parse.
    private static IReadOnlyDictionary<string, object> NormaliseJsonFrontmatter(IReadOnlyDictionary<string, object> raw)
    {
        var normalised = new Dictionary<string, object>(raw.Count, StringComparer.Ordinal);
        foreach (var (key, value) in raw)
        {
            var converted = NormaliseJsonValue(value);
            if (converted is not null)
            {
                normalised[key] = converted;
            }
        }
        return normalised;
    }

    private static object? NormaliseJsonValue(object? value)
    {
        if (value is null)
        {
            return null;
        }
        if (value is JsonElement element)
        {
            return NormaliseJsonElement(element);
        }
        return value;
    }

    private static object? NormaliseJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                {
                    return l;
                }
                if (element.TryGetDouble(out var d))
                {
                    return d;
                }
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Array:
                var list = new List<object>();
                foreach (var item in element.EnumerateArray())
                {
                    var converted = NormaliseJsonElement(item);
                    if (converted is not null)
                    {
                        list.Add(converted);
                    }
                }
                return list;
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    var converted = NormaliseJsonElement(property.Value);
                    if (converted is not null)
                    {
                        dict[property.Name] = converted;
                    }
                }
                return dict;
            default:
                return null;
        }
    }

    // Records a stamp for a file that was excluded permanently (binary or
    // oversize). Stat failures here are non-fatal; the file simply isn't
    // stamped and the next load re-sniffs it.
    private static void TryRecordExcludedStamp(
        string absolutePath,
        ResourceKey resourceKey,
        Dictionary<ResourceKey, CacheStamp> stamps)
    {
        try
        {
            var info = new FileInfo(absolutePath);
            if (info.Exists)
            {
                stamps[resourceKey] = new CacheStamp(info.LastWriteTimeUtc.Ticks, info.Length, IsText: false);
            }
        }
        catch
        {
            // No stamp recorded.
        }
    }

    private static void TryRecordScannedStamp(
        string absolutePath,
        ResourceKey resourceKey,
        bool isText,
        Dictionary<ResourceKey, CacheStamp> stamps)
    {
        try
        {
            var info = new FileInfo(absolutePath);
            if (info.Exists)
            {
                stamps[resourceKey] = new CacheStamp(info.LastWriteTimeUtc.Ticks, info.Length, isText);
            }
        }
        catch
        {
            // No stamp recorded.
        }
    }

    // Marks the in-memory state as ahead of the persisted cache. The worker
    // checks this flag periodically and persists when the debounce window has
    // elapsed.
    private void MarkDirty()
    {
        _isDirty = true;
    }

    // Persists the current in-memory state to the cache file. Skipped when a
    // transient-failure retry is queued so the cache never reflects partial
    // state. Best-effort: any failure logs a warning and leaves the existing
    // cache file untouched.
    private void PersistCache()
    {
        var path = GetCacheFilePath();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (!_transientFailureCounts.IsEmpty)
        {
            // Defer the write until the retry queue empties so the cache
            // doesn't snapshot a known-stale partial state.
            return;
        }

        MetaDataCacheDocument document;
        try
        {
            document = SnapshotForCache();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata cache: failed to snapshot in-memory state");
            return;
        }

        var saved = ResourceMetaDataCache.TrySave(path, document);
        if (saved)
        {
            _isDirty = false;
            _lastCacheSaveUtc = DateTime.UtcNow;
        }
        else
        {
            _logger.LogWarning("Metadata cache: failed to write cache file '{path}'", path);
        }
    }

    // Builds a cache document from the current in-memory state. The frontmatter
    // entries are keyed by the parent resource (matching the in-memory shape);
    // the on-disk cache stores them under the sidecar's resource key because
    // that's the file the mtime + size stamp refers to. The hydration path
    // reverses the mapping via GetSidecarParent.
    private MetaDataCacheDocument SnapshotForCache()
    {
        var files = new Dictionary<string, MetaDataCacheEntry>();

        IReadOnlyDictionary<ResourceKey, CacheStamp> stampsSnapshot;
        IReadOnlyDictionary<ResourceKey, HashSet<ResourceKey>> referencesSnapshot;
        IReadOnlyDictionary<ResourceKey, IReadOnlyDictionary<string, object>> frontmatterSnapshot;

        lock (_indexLock)
        {
            stampsSnapshot = new Dictionary<ResourceKey, CacheStamp>(_cacheStamps);
            referencesSnapshot = _referencesBySource.ToDictionary(
                kvp => kvp.Key,
                kvp => new HashSet<ResourceKey>(kvp.Value));
            frontmatterSnapshot = new Dictionary<ResourceKey, IReadOnlyDictionary<string, object>>(_frontmatterByResource);
        }

        // Reverse map: sidecar resource key -> parent's frontmatter. We need to
        // walk the parent -> frontmatter map and emit the entry against the
        // sidecar's key under which the mtime + size stamp lives.
        var parentToSidecar = new Dictionary<ResourceKey, ResourceKey>();
        foreach (var parent in frontmatterSnapshot.Keys)
        {
            if (parent.IsEmpty)
            {
                continue;
            }
            var sidecarKey = new ResourceKey(parent.Root + ":" + parent.Path + SidecarHelper.Extension);
            parentToSidecar[parent] = sidecarKey;
        }

        foreach (var (resourceKey, stamp) in stampsSnapshot)
        {
            List<string>? referencesList = null;
            if (referencesSnapshot.TryGetValue(resourceKey, out var refSet)
                && refSet.Count > 0)
            {
                referencesList = refSet.Select(r => r.ToString()).ToList();
            }

            Dictionary<string, object>? frontmatterDict = null;
            // If this is a sidecar key and its parent has frontmatter, embed
            // the frontmatter in this entry so reload hydrates both stamp and
            // index from the same record.
            if (IsSidecarKey(resourceKey))
            {
                var parent = StripSidecarSuffix(resourceKey);
                if (parent.HasValue
                    && frontmatterSnapshot.TryGetValue(parent.Value, out var fm)
                    && fm.Count > 0)
                {
                    frontmatterDict = new Dictionary<string, object>(fm, StringComparer.Ordinal);
                }
            }

            files[resourceKey.ToString()] = new MetaDataCacheEntry
            {
                MtimeUtcTicks = stamp.MtimeUtcTicks,
                Size = stamp.Size,
                IsText = stamp.IsText,
                References = referencesList,
                Frontmatter = frontmatterDict,
            };
        }

        return new MetaDataCacheDocument
        {
            Version = ResourceMetaDataCache.CurrentVersion,
            Files = files,
        };
    }

    public IReadOnlyList<ResourceKey> GetReferencers(ResourceKey target)
    {
        lock (_indexLock)
        {
            if (_referencersByTarget.TryGetValue(target, out var set))
            {
                return set.ToList();
            }
            return Array.Empty<ResourceKey>();
        }
    }

    public IReadOnlyList<ResourceKey> GetReferences(ResourceKey source)
    {
        lock (_indexLock)
        {
            if (_referencesBySource.TryGetValue(source, out var set))
            {
                return set.ToList();
            }
            return Array.Empty<ResourceKey>();
        }
    }

    public IReadOnlyList<ResourceKey> GetAllReferencedTargets()
    {
        lock (_indexLock)
        {
            return _referencersByTarget.Keys.ToList();
        }
    }

    public Result<IReadOnlyDictionary<string, object>> GetFrontmatter(ResourceKey resource)
    {
        lock (_indexLock)
        {
            if (!_frontmatterByResource.TryGetValue(resource, out var frontmatter))
            {
                return Result<IReadOnlyDictionary<string, object>>.Fail(
                    $"No frontmatter is indexed for resource '{resource}'. The resource may have no sidecar or its sidecar may be broken.");
            }
            // Return a snapshot copy so callers cannot mutate our state.
            var snapshot = new Dictionary<string, object>(frontmatter, StringComparer.Ordinal);
            return Result<IReadOnlyDictionary<string, object>>.Ok(snapshot);
        }
    }

    public async Task<Result> SetFrontmatterFieldAsync(ResourceKey resource, string field, object value)
    {
        if (string.IsNullOrEmpty(field))
        {
            return Result.Fail("The 'field' argument must be a non-empty string.");
        }

        if (!IsIndexableValue(value))
        {
            return Result.Fail($"Field '{field}' value is not indexable. Only scalar (string/number/bool) and list-of-scalar values are supported.");
        }

        return await MutateSidecarFrontmatterAsync(resource, mutate: dict => dict[field] = value);
    }

    public async Task<Result> RemoveFrontmatterFieldAsync(ResourceKey resource, string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return Result.Fail("The 'field' argument must be a non-empty string.");
        }

        return await MutateSidecarFrontmatterAsync(
            resource,
            mutate: dict => { dict.Remove(field); },
            createSidecarIfMissing: false);
    }

    public IReadOnlyList<ResourceKey> FindByMetaData(string field, object value)
    {
        if (string.IsNullOrEmpty(field) || value is null)
        {
            return Array.Empty<ResourceKey>();
        }

        lock (_indexLock)
        {
            if (!_resourcesByMetaDataField.TryGetValue(field, out var byValue))
            {
                return Array.Empty<ResourceKey>();
            }

            // Normalise the query value into the same canonical form used when
            // populating the index so an int query against a long-typed scalar
            // still finds the entry.
            var canonical = CanonicaliseScalar(value);
            if (canonical is null)
            {
                return Array.Empty<ResourceKey>();
            }

            if (!byValue.TryGetValue(canonical, out var resources))
            {
                return Array.Empty<ResourceKey>();
            }

            return resources.ToList();
        }
    }

    public IReadOnlyList<string> GetTags(ResourceKey resource)
    {
        lock (_indexLock)
        {
            if (!_frontmatterByResource.TryGetValue(resource, out var frontmatter))
            {
                return Array.Empty<string>();
            }

            if (!frontmatter.TryGetValue(TagsField, out var tagsValue))
            {
                return Array.Empty<string>();
            }

            return ExtractStringList(tagsValue);
        }
    }

    public async Task<Result> AddTagAsync(ResourceKey resource, string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return Result.Fail("Tag value must be a non-empty string.");
        }

        return await MutateSidecarFrontmatterAsync(resource, mutate: dict =>
        {
            var existing = dict.TryGetValue(TagsField, out var value)
                ? ExtractStringList(value)
                : Array.Empty<string>();

            if (existing.Contains(tag, StringComparer.Ordinal))
            {
                // Idempotent: no change needed.
                return;
            }

            var updated = new List<string>(existing.Count + 1);
            updated.AddRange(existing);
            updated.Add(tag);
            dict[TagsField] = updated;
        });
    }

    public async Task<Result> RemoveTagAsync(ResourceKey resource, string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return Result.Fail("Tag value must be a non-empty string.");
        }

        return await MutateSidecarFrontmatterAsync(
            resource,
            mutate: dict =>
            {
                if (!dict.TryGetValue(TagsField, out var value))
                {
                    return;
                }

                var existing = ExtractStringList(value);
                if (!existing.Contains(tag, StringComparer.Ordinal))
                {
                    return;
                }

                var updated = existing.Where(t => !string.Equals(t, tag, StringComparison.Ordinal)).ToList();
                if (updated.Count == 0)
                {
                    // Drop the field entirely when the list goes empty rather
                    // than leaving an empty array in the file.
                    dict.Remove(TagsField);
                }
                else
                {
                    dict[TagsField] = updated;
                }
            },
            createSidecarIfMissing: false);
    }

    public IReadOnlyList<ResourceKey> FindByTag(string tag)
    {
        // FindByTag is a thin alias for FindByMetaData against the standardised
        // tags field; the inverted index already records list-of-scalar fields
        // element-wise, so a tag query is just a value lookup.
        return FindByMetaData(TagsField, tag);
    }

    // Walks file text for "project:" candidate references. Returns the unique
    // set of valid keys found; invalid candidates are silently dropped. Parsing
    // logic is delegated to ReferenceLiteralRules so the scanner and the
    // rewrite cascade share one definition of what counts as a reference.
    public static HashSet<ResourceKey> ScanTextForReferences(string text)
    {
        var references = new HashSet<ResourceKey>();
        int searchStart = 0;

        while (true)
        {
            int markerIndex = text.IndexOf(ReferenceLiteralRules.ReferenceMarker, searchStart, StringComparison.Ordinal);
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
                searchStart = markerIndex + ReferenceLiteralRules.ReferenceMarker.Length;
            }
        }

        return references;
    }

    private enum ScanOutcome
    {
        // Scan succeeded; References reflects what the file contains right now.
        Indexed,

        // File is deliberately not indexable in its current shape (deleted,
        // oversize, binary). Prior index entries should be dropped.
        ExcludedPermanently,

        // Scan failed in a way that may resolve itself (file locked by another
        // process, transient IO error). Prior index entries should be preserved
        // and the rescan should be retried after a short delay.
        TransientFailure,
    }

    // SidecarText is populated for .cel files so the caller can parse the
    // frontmatter without re-reading the bytes; null for non-sidecar paths.
    private record FileScanResult(ScanOutcome Outcome, HashSet<ResourceKey> References, string? SidecarText = null);

    private static readonly HashSet<ResourceKey> EmptyReferenceSet = new();

    private async Task<FileScanResult> ScanTextFileAsync(ResourceKey resourceKey, string absolutePath)
    {
        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(absolutePath);
            if (!fileInfo.Exists)
            {
                return new FileScanResult(ScanOutcome.ExcludedPermanently, EmptyReferenceSet);
            }
            if (fileInfo.Length > MaxScanFileSizeBytes)
            {
                _logger.LogInformation($"metadata scan: skipping {resourceKey} (size {fileInfo.Length} bytes exceeds limit)");
                return new FileScanResult(ScanOutcome.ExcludedPermanently, EmptyReferenceSet);
            }
        }
        catch (Exception ex) when (IsTransientIoFailure(ex))
        {
            _logger.LogDebug($"metadata scan: transient stat failure for {resourceKey} ({ex.GetType().Name}): {ex.Message}");
            return new FileScanResult(ScanOutcome.TransientFailure, EmptyReferenceSet);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"metadata scan: failed to stat {resourceKey}");
            return new FileScanResult(ScanOutcome.ExcludedPermanently, EmptyReferenceSet);
        }

        var extension = Path.GetExtension(absolutePath);
        if (!string.IsNullOrEmpty(extension)
            && _textBinarySniffer.IsBinaryExtension(extension))
        {
            return new FileScanResult(ScanOutcome.ExcludedPermanently, EmptyReferenceSet);
        }

        var isTextResult = _textBinarySniffer.IsTextFile(absolutePath);
        if (isTextResult.IsFailure)
        {
            // The sniffer's failure surface doesn't distinguish locked-file from
            // genuinely-unreadable. Treat as transient: a real permanent failure
            // exhausts MaxScanRetryAttempts and gets dropped; a transient one
            // succeeds on retry. Worst case is three short retries.
            _logger.LogDebug($"metadata scan: sniffer failure for {resourceKey} - treating as transient");
            return new FileScanResult(ScanOutcome.TransientFailure, EmptyReferenceSet);
        }
        if (!isTextResult.Value)
        {
            return new FileScanResult(ScanOutcome.ExcludedPermanently, EmptyReferenceSet);
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(absolutePath);
        }
        catch (Exception ex) when (IsTransientIoFailure(ex))
        {
            _logger.LogDebug($"metadata scan: transient read failure for {resourceKey} ({ex.GetType().Name}): {ex.Message}");
            return new FileScanResult(ScanOutcome.TransientFailure, EmptyReferenceSet);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"metadata scan: failed to read {resourceKey}");
            return new FileScanResult(ScanOutcome.ExcludedPermanently, EmptyReferenceSet);
        }

        var references = ScanTextForReferences(text);

        // Capture the file content for sidecar files so the caller can parse
        // the frontmatter without a second disk read. Non-sidecar files leave
        // SidecarText null.
        var sidecarText = IsSidecarPath(absolutePath) ? text : null;
        return new FileScanResult(ScanOutcome.Indexed, references, sidecarText);
    }

    // True when the path's filename ends in ".cel" but not ".cel.cel". The
    // ".cel.cel" form is reserved as the invalid-sidecar marker per
    // file_metadata_sidecars.md and is never paired with a parent.
    private static bool IsSidecarPath(string absolutePath)
    {
        var fileName = Path.GetFileName(absolutePath);
        if (!fileName.EndsWith(SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (fileName.EndsWith(SidecarHelper.Extension + SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    // IOException covers file-locked, sharing-violation, network-share blip;
    // UnauthorizedAccessException can fire transiently on Windows while an
    // antivirus or backup product holds the file. Both are worth retrying.
    private static bool IsTransientIoFailure(Exception ex)
    {
        return ex is IOException
            || ex is UnauthorizedAccessException;
    }

    // Returns a normalised top-level frontmatter dictionary suitable for the
    // index, or null when the sidecar bytes cannot be parsed. The TOML model
    // values from Tomlyn are normalised via NormaliseTomlValue so the index
    // entries stay consistent across reload paths (cache hydration, fresh
    // parse) and the equality semantics of the dictionary keys are
    // case-sensitive ordinal.
    private IReadOnlyDictionary<string, object>? TryParseSidecarFrontmatter(string absolutePath, string? text)
    {
        if (text is null)
        {
            return null;
        }

        var parseResult = SidecarHelper.Parse(text);
        if (parseResult.IsFailure)
        {
            _logger.LogWarning($"metadata scan: sidecar at '{absolutePath}' has unparseable frontmatter and will not be indexed.");
            return null;
        }

        var raw = parseResult.Value.Frontmatter;
        if (raw.Count == 0)
        {
            return null;
        }

        var normalised = new Dictionary<string, object>(raw.Count, StringComparer.Ordinal);
        foreach (var (key, value) in raw)
        {
            var converted = NormaliseTomlValue(value);
            if (converted is null)
            {
                continue;
            }
            normalised[key] = converted;
        }

        return normalised.Count == 0 ? null : normalised;
    }

    // Converts a Tomlyn model value into the plain CLR shapes the index
    // expects: strings stay strings, TomlArray becomes List<object?>, TomlTable
    // becomes Dictionary<string, object>. Scalars come out as their underlying
    // CLR type (long, double, bool, DateTime, etc.). Returns null only for
    // truly unrepresentable input.
    private static object? NormaliseTomlValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case TomlTable table:
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var (k, v) in table)
                {
                    var converted = NormaliseTomlValue(v);
                    if (converted is null)
                    {
                        continue;
                    }
                    dict[k] = converted;
                }
                return dict;
            case TomlArray array:
                var list = new List<object>(array.Count);
                foreach (var item in array)
                {
                    var converted = NormaliseTomlValue(item);
                    if (converted is null)
                    {
                        continue;
                    }
                    list.Add(converted);
                }
                return list;
            default:
                return value;
        }
    }

    // Populates the inverted index for one resource's frontmatter. Scalar
    // fields contribute their value as a single index entry; list-of-scalar
    // fields contribute each element. Non-indexable shapes (nested tables,
    // arrays of arrays) are stored in _frontmatterByResource but not indexed.
    private static void ApplyFrontmatter(
        Dictionary<string, Dictionary<object, HashSet<ResourceKey>>> resourcesByMetaDataField,
        ResourceKey resource,
        IReadOnlyDictionary<string, object> frontmatter)
    {
        foreach (var (field, value) in frontmatter)
        {
            foreach (var indexedValue in EnumerateIndexValues(value))
            {
                if (!resourcesByMetaDataField.TryGetValue(field, out var byValue))
                {
                    byValue = new Dictionary<object, HashSet<ResourceKey>>();
                    resourcesByMetaDataField[field] = byValue;
                }
                if (!byValue.TryGetValue(indexedValue, out var set))
                {
                    set = new HashSet<ResourceKey>();
                    byValue[indexedValue] = set;
                }
                set.Add(resource);
            }
        }
    }

    // Yields the values to index for a given frontmatter field. Scalars yield
    // themselves (canonicalised); list-of-scalar yields each canonicalised
    // element. Nested objects and lists-of-non-scalars yield nothing — they
    // remain available via GetFrontmatter but not via FindByMetaData.
    private static IEnumerable<object> EnumerateIndexValues(object value)
    {
        if (value is IReadOnlyList<object> objectList
            && value is not string)
        {
            foreach (var item in objectList)
            {
                if (item is null)
                {
                    continue;
                }
                var canonical = CanonicaliseScalar(item);
                if (canonical is not null)
                {
                    yield return canonical;
                }
            }
            yield break;
        }

        if (value is System.Collections.IEnumerable enumerable
            && value is not string)
        {
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    continue;
                }
                var canonical = CanonicaliseScalar(item);
                if (canonical is not null)
                {
                    yield return canonical;
                }
            }
            yield break;
        }

        var scalar = CanonicaliseScalar(value);
        if (scalar is not null)
        {
            yield return scalar;
        }
    }

    // True when the value is a shape we can serialise back to TOML and index:
    // strings, numeric scalars, booleans, datetimes, and lists of those. The
    // service rejects nested-object frontmatter writes at the mutation surface
    // so callers get a clear error rather than a silent drop.
    private static bool IsIndexableValue(object? value)
    {
        if (value is null)
        {
            return false;
        }
        if (IsScalar(value))
        {
            return true;
        }
        if (value is IReadOnlyList<object> objectList
            && value is not string)
        {
            return objectList.All(item => item is not null && IsScalar(item));
        }
        if (value is System.Collections.IEnumerable enumerable
            && value is not string)
        {
            foreach (var item in enumerable)
            {
                if (item is null
                    || !IsScalar(item))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private static bool IsScalar(object value)
    {
        return value is string
            || value is bool
            || value is long
            || value is int
            || value is double
            || value is float
            || value is decimal
            || value is DateTime
            || value is DateTimeOffset
            || value is DateOnly
            || value is TimeOnly;
    }

    // Normalises a scalar into the form used as a dictionary key in the
    // inverted index, so a long-typed cached value and an int-typed query
    // value still compare equal. Returns null for unrepresentable input.
    private static object? CanonicaliseScalar(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string s:
                return s;
            case bool b:
                return b;
            case int i:
                return (long)i;
            case long l:
                return l;
            case short sh:
                return (long)sh;
            case byte by:
                return (long)by;
            case sbyte sb:
                return (long)sb;
            case uint ui:
                return (long)ui;
            case ulong ul:
                return (long)ul;
            case float f:
                return (double)f;
            case double d:
                return d;
            case decimal dec:
                return (double)dec;
            case DateTime dt:
                return dt.ToUniversalTime();
            case DateTimeOffset dto:
                return dto.UtcDateTime;
            default:
                return null;
        }
    }

    // Returns the value as a list of strings when possible (a TOML "tags"
    // array contributes a list of strings); empty otherwise.
    private static IReadOnlyList<string> ExtractStringList(object value)
    {
        var result = new List<string>();
        if (value is string)
        {
            return result;
        }
        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is string s)
                {
                    result.Add(s);
                }
            }
        }
        return result;
    }

    // Reads the resource's sidecar (creating it if missing), applies the
    // mutation to a working copy of the frontmatter dictionary, and writes the
    // result back through IResourceFileSystem so atomic-write + watcher event
    // semantics apply. Returns success even when the mutation is a no-op.
    private async Task<Result> MutateSidecarFrontmatterAsync(
        ResourceKey resource,
        Action<Dictionary<string, object>> mutate,
        bool createSidecarIfMissing = true)
    {
        if (resource.IsEmpty)
        {
            return Result.Fail("Cannot set frontmatter on an empty resource key.");
        }

        var sidecarKey = new ResourceKey(resource.Root + ":" + resource.Path + SidecarHelper.Extension);

        var fileSystem = _workspaceWrapper.WorkspaceService.ResourceFileSystem;
        var existsResult = await fileSystem.ExistsAsync(sidecarKey);
        if (existsResult.IsFailure)
        {
            return Result.Fail($"Failed to check sidecar existence for resource '{resource}'.")
                .WithErrors(existsResult);
        }

        Dictionary<string, object> working;
        string body = string.Empty;

        if (existsResult.Value)
        {
            var readResult = await fileSystem.ReadAllTextAsync(sidecarKey);
            if (readResult.IsFailure)
            {
                return Result.Fail($"Failed to read sidecar '{sidecarKey}'.")
                    .WithErrors(readResult);
            }

            var parseResult = SidecarHelper.Parse(readResult.Value);
            if (parseResult.IsFailure)
            {
                return Result.Fail($"Cannot mutate sidecar '{sidecarKey}': frontmatter does not parse.")
                    .WithErrors(parseResult);
            }
            working = new Dictionary<string, object>(parseResult.Value.Frontmatter, StringComparer.Ordinal);
            body = parseResult.Value.Body;
        }
        else
        {
            if (!createSidecarIfMissing)
            {
                // Removing a field from a non-existent sidecar is a no-op success.
                return Result.Ok();
            }
            working = new Dictionary<string, object>(StringComparer.Ordinal);
        }

        mutate(working);

        var composed = SidecarHelper.Compose(working, body);
        var writeResult = await fileSystem.WriteAllTextAsync(sidecarKey, composed);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write sidecar '{sidecarKey}'.")
                .WithErrors(writeResult);
        }

        // The file watcher delivers ResourceChangedMessage asynchronously
        // through the UI dispatcher, so by the time this method returns the
        // background worker has not yet seen the write. Apply the index
        // update synchronously here so the caller's next read sees the new
        // state. The watcher's eventual rescan re-applies the same parsed
        // frontmatter against the in-memory dictionaries; that pass is
        // idempotent.
        var parsedForIndex = TryParseSidecarFrontmatter("<post-write>", composed);
        UpdateFrontmatterInIndexes(resource, parsedForIndex);

        // Refresh the cache stamp for the sidecar file so a subsequent
        // workspace load can hydrate from the cache instead of rescanning.
        // Stat failures here are absorbed by UpdateCacheStamp's catch.
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolveSidecar = registry.ResolveResourcePath(sidecarKey);
        if (resolveSidecar.IsSuccess)
        {
            UpdateCacheStamp(sidecarKey, resolveSidecar.Value, isText: true);
        }

        return Result.Ok();
    }

    private static void ApplyReferences(
        Dictionary<ResourceKey, HashSet<ResourceKey>> referencersByTarget,
        Dictionary<ResourceKey, HashSet<ResourceKey>> referencesBySource,
        ResourceKey source,
        HashSet<ResourceKey> references)
    {
        if (!referencesBySource.TryGetValue(source, out var sourceSet))
        {
            sourceSet = new HashSet<ResourceKey>();
            referencesBySource[source] = sourceSet;
        }

        foreach (var target in references)
        {
            sourceSet.Add(target);

            if (!referencersByTarget.TryGetValue(target, out var targetSet))
            {
                targetSet = new HashSet<ResourceKey>();
                referencersByTarget[target] = targetSet;
            }
            targetSet.Add(source);
        }
    }

    private void RemoveSourceFromIndexes(ResourceKey source)
    {
        lock (_indexLock)
        {
            if (_referencesBySource.TryGetValue(source, out var oldTargets))
            {
                foreach (var target in oldTargets)
                {
                    if (_referencersByTarget.TryGetValue(target, out var referencers))
                    {
                        referencers.Remove(source);
                        if (referencers.Count == 0)
                        {
                            _referencersByTarget.Remove(target);
                        }
                    }
                }
                _referencesBySource.Remove(source);
            }
            _cacheStamps.Remove(source);
        }
        MarkDirty();
    }

    // Strips a parent resource's frontmatter from both the per-resource snapshot
    // and the inverted index. Called when a sidecar disappears or its content
    // becomes unparseable. The argument is the parent key (e.g. "foo.png"), not
    // the sidecar key.
    private void RemoveFrontmatterFromIndexes(ResourceKey parentResource)
    {
        bool changed = false;
        lock (_indexLock)
        {
            if (!_frontmatterByResource.TryGetValue(parentResource, out var existing))
            {
                return;
            }
            _frontmatterByResource.Remove(parentResource);
            changed = true;

            foreach (var (field, value) in existing)
            {
                if (!_resourcesByMetaDataField.TryGetValue(field, out var byValue))
                {
                    continue;
                }
                foreach (var indexedValue in EnumerateIndexValues(value))
                {
                    if (!byValue.TryGetValue(indexedValue, out var set))
                    {
                        continue;
                    }
                    set.Remove(parentResource);
                    if (set.Count == 0)
                    {
                        byValue.Remove(indexedValue);
                    }
                }
                if (byValue.Count == 0)
                {
                    _resourcesByMetaDataField.Remove(field);
                }
            }
        }
        if (changed)
        {
            MarkDirty();
        }
    }

    // Replaces the frontmatter snapshot and inverted-index entries for one
    // parent resource. Empty/null frontmatter behaves as a removal so the
    // caller doesn't have to branch when a sidecar transitions to broken.
    private void UpdateFrontmatterInIndexes(ResourceKey parentResource, IReadOnlyDictionary<string, object>? frontmatter)
    {
        RemoveFrontmatterFromIndexes(parentResource);

        if (frontmatter is null
            || frontmatter.Count == 0)
        {
            return;
        }

        lock (_indexLock)
        {
            _frontmatterByResource[parentResource] = frontmatter;
            ApplyFrontmatter(_resourcesByMetaDataField, parentResource, frontmatter);
        }
        MarkDirty();
    }

    private void UpdateSourceInIndexes(ResourceKey source, HashSet<ResourceKey> references)
    {
        lock (_indexLock)
        {
            // Strip any prior referrals from this source first so the new set
            // fully replaces the old set.
            if (_referencesBySource.TryGetValue(source, out var oldTargets))
            {
                foreach (var target in oldTargets)
                {
                    if (_referencersByTarget.TryGetValue(target, out var referencers))
                    {
                        referencers.Remove(source);
                        if (referencers.Count == 0)
                        {
                            _referencersByTarget.Remove(target);
                        }
                    }
                }
            }

            if (references.Count == 0)
            {
                _referencesBySource.Remove(source);
            }
            else
            {
                _referencesBySource[source] = new HashSet<ResourceKey>(references);
                foreach (var target in references)
                {
                    if (!_referencersByTarget.TryGetValue(target, out var targetSet))
                    {
                        targetSet = new HashSet<ResourceKey>();
                        _referencersByTarget[target] = targetSet;
                    }
                    targetSet.Add(source);
                }
            }
        }
        MarkDirty();
    }

    // Records a per-file stamp captured after a successful incremental scan.
    private void UpdateCacheStamp(ResourceKey resource, string absolutePath, bool isText)
    {
        try
        {
            var info = new FileInfo(absolutePath);
            if (!info.Exists)
            {
                lock (_indexLock)
                {
                    _cacheStamps.Remove(resource);
                }
                return;
            }
            var stamp = new CacheStamp(info.LastWriteTimeUtc.Ticks, info.Length, isText);
            lock (_indexLock)
            {
                _cacheStamps[resource] = stamp;
            }
        }
        catch
        {
            // Best-effort; the next watcher event will re-stamp.
        }
    }

    private void OnResourceCreated(object recipient, ResourceCreatedMessage message)
    {
        // A fresh lifecycle event means the file's state is changing; reset
        // the retry budget so a file that previously gave up after
        // MaxScanRetryAttempts gets re-scanned with full budget on its next
        // legitimate change.
        _transientFailureCounts.TryRemove(message.Resource, out _);
        QueueRescan(message.Resource);
    }

    private void OnResourceChanged(object recipient, ResourceChangedMessage message)
    {
        _transientFailureCounts.TryRemove(message.Resource, out _);
        QueueRescan(message.Resource);
    }

    private void OnResourceDeleted(object recipient, ResourceDeletedMessage message)
    {
        if (message.Resource.Root != ResourceKey.DefaultRoot
            || message.Resource.IsEmpty)
        {
            return;
        }

        // Atomic temp + rename writes (ResourceFileSystem.WriteAtomicAsync)
        // briefly remove the destination during File.Move(overwrite: true),
        // which fires a FileSystemWatcher delete event immediately followed
        // by a create event. By the time the dispatcher delivers the delete
        // here the file is back on disk; clearing the index would clobber
        // the synchronous entry MutateSidecarFrontmatterAsync just installed.
        // The companion create event still triggers a rescan, which would
        // re-establish the entry — but list / get / find calls landing in
        // the window between the spurious delete and the rescan would see
        // empty results. Skipping the removal when the file is still on
        // disk closes that window. A genuine deletion has the file gone by
        // the time the event arrives, so File.Exists is false and we fall
        // through to the original removal logic.
        try
        {
            var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
            var resolveResult = registry.ResolveResourcePath(message.Resource);
            if (resolveResult.IsSuccess
                && File.Exists(resolveResult.Value))
            {
                return;
            }
        }
        catch
        {
            // If resolution itself fails, fall through to the conservative
            // removal so a true deletion still clears the index.
        }

        RemoveSourceFromIndexes(message.Resource);

        // A deleted parent file drops its own frontmatter entry; a deleted
        // sidecar drops the entry under its parent key. The sidecar cascade
        // path runs both events, so handle both shapes here.
        if (IsSidecarKey(message.Resource))
        {
            var parentKey = StripSidecarSuffix(message.Resource);
            if (parentKey.HasValue)
            {
                RemoveFrontmatterFromIndexes(parentKey.Value);
            }
        }
        else
        {
            RemoveFrontmatterFromIndexes(message.Resource);
        }

        _transientFailureCounts.TryRemove(message.Resource, out _);
    }

    private void OnResourceRenamed(object recipient, ResourceRenamedMessage message)
    {
        if (message.OldResource.Root == ResourceKey.DefaultRoot)
        {
            RemoveSourceFromIndexes(message.OldResource);

            if (IsSidecarKey(message.OldResource))
            {
                var oldParent = StripSidecarSuffix(message.OldResource);
                if (oldParent.HasValue)
                {
                    RemoveFrontmatterFromIndexes(oldParent.Value);
                }
            }
            else
            {
                RemoveFrontmatterFromIndexes(message.OldResource);
            }

            _transientFailureCounts.TryRemove(message.OldResource, out _);
        }
        _transientFailureCounts.TryRemove(message.NewResource, out _);
        QueueRescan(message.NewResource);
    }

    // True when the key's path ends in ".cel" but not ".cel.cel". Mirrors the
    // file-path test used during the rebuild scan.
    private static bool IsSidecarKey(ResourceKey key)
    {
        if (key.IsEmpty)
        {
            return false;
        }
        var path = key.Path;
        if (!path.EndsWith(SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (path.EndsWith(SidecarHelper.Extension + SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    // Strips the trailing ".cel" from a sidecar key to recover its parent key.
    // Returns null when the result would be empty (e.g. a hypothetical bare
    // ".cel" key with no path component).
    private static ResourceKey? StripSidecarSuffix(ResourceKey sidecarKey)
    {
        var path = sidecarKey.Path;
        if (!path.EndsWith(SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var parentPath = path.Substring(0, path.Length - SidecarHelper.Extension.Length);
        if (string.IsNullOrEmpty(parentPath))
        {
            return null;
        }
        return new ResourceKey(sidecarKey.Root + ":" + parentPath);
    }

    // Helper used by the rescan paths to drop frontmatter entries when a
    // sidecar key disappears or stops parsing. No-op for non-sidecar keys.
    private void MaybeDropFrontmatterForSidecar(ResourceKey resource)
    {
        if (!IsSidecarKey(resource))
        {
            return;
        }
        var parentKey = StripSidecarSuffix(resource);
        if (parentKey.HasValue)
        {
            RemoveFrontmatterFromIndexes(parentKey.Value);
        }
    }

    private void QueueRescan(ResourceKey resource)
    {
        // Only project: resources contribute to the index. Watcher messages from
        // temp: and logs: roots are ignored.
        if (resource.Root != ResourceKey.DefaultRoot
            || resource.IsEmpty)
        {
            return;
        }

        _pendingRescans.Enqueue(resource);
        try
        {
            _workerSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // The signal count is unbounded in practice; ignore the rare overflow.
        }
    }

    private async Task WorkerLoopAsync()
    {
        // The worker waits on the semaphore for new work and checks _isShuttingDown
        // after every wake. Dispose sets the flag and releases the semaphore once,
        // so the worker exits cleanly without raising an OperationCanceledException.
        while (!_isShuttingDown)
        {
            try
            {
                await _workerSignal.WaitAsync();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (_isShuttingDown)
            {
                return;
            }

            while (_pendingRescans.TryDequeue(out var resource))
            {
                if (_isShuttingDown)
                {
                    return;
                }

                await ProcessRescanAsync(resource);
            }

            // Debounced cache write: persist once the queue is empty and the
            // last save was long enough ago that we won't thrash. Skipping when
            // _isDirty is false avoids re-saving an already-current snapshot.
            if (_isDirty
                && (DateTime.UtcNow - _lastCacheSaveUtc) >= MinCacheSaveInterval)
            {
                PersistCache();
            }
        }
    }

    private async Task ProcessRescanAsync(ResourceKey resource)
    {
        try
        {
            if (_isShuttingDown)
            {
                return;
            }

            var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
            var resolveResult = registry.ResolveResourcePath(resource);
            if (resolveResult.IsFailure)
            {
                RemoveSourceFromIndexes(resource);
                MaybeDropFrontmatterForSidecar(resource);
                _transientFailureCounts.TryRemove(resource, out _);
                return;
            }
            var absolutePath = resolveResult.Value;

            if (!File.Exists(absolutePath))
            {
                RemoveSourceFromIndexes(resource);
                MaybeDropFrontmatterForSidecar(resource);
                _transientFailureCounts.TryRemove(resource, out _);
                return;
            }

            var scanResult = await ScanTextFileAsync(resource, absolutePath);
            switch (scanResult.Outcome)
            {
                case ScanOutcome.Indexed:
                    UpdateSourceInIndexes(resource, scanResult.References);
                    if (IsSidecarPath(absolutePath))
                    {
                        // Derive the parent key by stripping the .cel suffix
                        // rather than querying the registry's pairing table.
                        // The pairing table is refreshed only by
                        // UpdateResourceRegistry, which lags watcher events
                        // for newly-created sidecars; relying on it here
                        // would race with the synchronous index update in
                        // MutateSidecarFrontmatterAsync. Parent existence is
                        // checked on disk so a true orphan still drops the
                        // index entry.
                        var parentKey = StripSidecarSuffix(resource);
                        if (parentKey.HasValue)
                        {
                            var parentResolve = registry.ResolveResourcePath(parentKey.Value);
                            var parentExists = parentResolve.IsSuccess
                                && File.Exists(parentResolve.Value);
                            if (parentExists)
                            {
                                var parsed = TryParseSidecarFrontmatter(absolutePath, scanResult.SidecarText);
                                UpdateFrontmatterInIndexes(parentKey.Value, parsed);
                            }
                            else
                            {
                                RemoveFrontmatterFromIndexes(parentKey.Value);
                            }
                        }
                    }
                    UpdateCacheStamp(resource, absolutePath, isText: true);
                    _transientFailureCounts.TryRemove(resource, out _);
                    break;

                case ScanOutcome.ExcludedPermanently:
                    RemoveSourceFromIndexes(resource);
                    MaybeDropFrontmatterForSidecar(resource);
                    UpdateCacheStamp(resource, absolutePath, isText: false);
                    _transientFailureCounts.TryRemove(resource, out _);
                    break;

                case ScanOutcome.TransientFailure:
                    // Preserve existing index entries; the file is briefly
                    // unreadable but the prior data is still our best guess.
                    ScheduleRetryAfterTransientFailure(resource);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"metadata scan: failed to rescan {resource}");
        }
    }

    private void ScheduleRetryAfterTransientFailure(ResourceKey resource)
    {
        var attempt = _transientFailureCounts.AddOrUpdate(resource, 1, (_, previous) => previous + 1);
        if (attempt > MaxScanRetryAttempts)
        {
            _logger.LogWarning($"metadata scan: giving up on {resource} after {MaxScanRetryAttempts} transient failures. The next watcher event for this file will reset the retry budget.");
            _transientFailureCounts.TryRemove(resource, out _);
            return;
        }

        var delay = ScanRetryDelays[attempt - 1];

        // Detached background continuation; nothing awaits this task. If the
        // service is disposed mid-delay the worker exits early via _isShuttingDown.
        _ = Task.Delay(delay).ContinueWith(_ =>
        {
            if (_isShuttingDown)
            {
                return;
            }
            QueueRescan(resource);
        }, TaskScheduler.Default);
    }

    private void MarkReady()
    {
        if (_isReady)
        {
            return;
        }
        _isReady = true;
        _readyCompletionSource.TrySetResult(true);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;

        _messengerService.UnregisterAll(this);

        // Persist the cache before signalling shutdown so the in-memory state
        // survives the next workspace load. Best-effort: a failure here is
        // logged inside PersistCache and the next load falls back to a full
        // rebuild.
        if (_isDirty)
        {
            try
            {
                PersistCache();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metadata cache: failed to persist on dispose");
            }
        }

        // Signal the worker to exit, then nudge the semaphore so it observes the
        // flag and returns. The worker checks _isShuttingDown after every wake.
        _isShuttingDown = true;
        try
        {
            _workerSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Worker is already pending wake-up; nothing more to do.
        }
        catch (ObjectDisposedException)
        {
            // Already disposed; nothing more to do.
        }

        try
        {
            _workerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Worker shutdown is best-effort; never let dispose throw.
        }

        _workerSignal.Dispose();
    }
}
