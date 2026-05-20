using System.Collections.Concurrent;
using System.Diagnostics;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Workspace-scoped reference graph and frontmatter index for project resources.
/// </summary>
public sealed class ResourceMetaData : IResourceMetaData, IDisposable
{
    // Files larger than this byte budget are skipped during the scan.
    private const long MaxScanFileSizeBytes = 10 * 1024 * 1024;

    // Characters that cannot legally appear inside a resource key; the scanner
    // stops accumulating candidate-key bytes at the first one it sees.
    // Whitespace and control chars are handled separately via char.IsWhiteSpace
    // and char.IsControl so this set only enumerates the printable terminators.
    private static readonly HashSet<char> KeyTerminators = new()
    {
        '"', '\'', '`', '(', ')', '<', '>', ',', ';', ']', '}',
    };

    private const string ReferenceMarker = "project:";

    private readonly ILogger<ResourceMetaData> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ITextBinarySniffer _textBinarySniffer;

    private readonly object _indexLock = new();
    private readonly Dictionary<ResourceKey, HashSet<ResourceKey>> _referencersByTarget = new();
    private readonly Dictionary<ResourceKey, HashSet<ResourceKey>> _referencesBySource = new();

    // The pending-rescan queue. Watcher events push file keys onto this; the
    // background worker drains them. WaitForPendingUpdatesAsync awaits the
    // worker when it sees a non-empty queue.
    private readonly ConcurrentQueue<ResourceKey> _pendingRescans = new();
    private readonly SemaphoreSlim _workerSignal = new(0);
    private Task? _workerTask;

    private TaskCompletionSource<bool> _readyCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isReady;
    private volatile bool _isShuttingDown;
    private bool _isDisposed;

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

        _messengerService.Register<MonitoredResourceCreatedMessage>(this, OnResourceCreated);
        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnResourceChanged);
        _messengerService.Register<MonitoredResourceDeletedMessage>(this, OnResourceDeleted);
        _messengerService.Register<MonitoredResourceRenamedMessage>(this, OnResourceRenamed);

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

            var newReferencersByTarget = new Dictionary<ResourceKey, HashSet<ResourceKey>>();
            var newReferencesBySource = new Dictionary<ResourceKey, HashSet<ResourceKey>>();

            int filesScanned = 0;
            int filesSkipped = 0;
            int referencesFound = 0;

            foreach (var (resourceKey, absolutePath) in files)
            {
                var scanResult = await ScanTextFileAsync(resourceKey, absolutePath);
                if (scanResult.WasSkipped)
                {
                    filesSkipped++;
                    continue;
                }

                filesScanned++;

                if (scanResult.References.Count == 0)
                {
                    continue;
                }

                referencesFound += scanResult.References.Count;
                ApplyReferences(newReferencersByTarget, newReferencesBySource, resourceKey, scanResult.References);
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
            }

            stopwatch.Stop();

            MarkReady();

            // FrontmatterEntries is always zero because frontmatter scanning is
            // not yet implemented on this service.
            var report = new MetaDataScanReport(
                FilesScanned: filesScanned,
                FilesSkipped: filesSkipped,
                ReferencesFound: referencesFound,
                FrontmatterEntries: 0,
                Elapsed: stopwatch.Elapsed);

            _logger.LogDebug($"Metadata rebuild complete: {filesScanned} scanned, {filesSkipped} skipped, {referencesFound} references in {stopwatch.ElapsedMilliseconds}ms");

            return Result<MetaDataScanReport>.Ok(report);
        }
        catch (Exception ex)
        {
            return Result<MetaDataScanReport>.Fail("An exception occurred during the metadata rebuild.")
                .WithException(ex);
        }
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
        throw new NotImplementedException("The frontmatter index is not yet implemented.");
    }

    public Task<Result> SetFrontmatterFieldAsync(ResourceKey resource, string field, object value)
    {
        throw new NotImplementedException("The frontmatter index is not yet implemented.");
    }

    public Task<Result> RemoveFrontmatterFieldAsync(ResourceKey resource, string field)
    {
        throw new NotImplementedException("The frontmatter index is not yet implemented.");
    }

    public IReadOnlyList<ResourceKey> FindByMetaData(string field, object value)
    {
        throw new NotImplementedException("The frontmatter index is not yet implemented.");
    }

    public IReadOnlyList<string> GetTags(ResourceKey resource)
    {
        throw new NotImplementedException("The frontmatter index is not yet implemented.");
    }

    public Task<Result> AddTagAsync(ResourceKey resource, string tag)
    {
        throw new NotImplementedException("The frontmatter index is not yet implemented.");
    }

    public Task<Result> RemoveTagAsync(ResourceKey resource, string tag)
    {
        throw new NotImplementedException("The frontmatter index is not yet implemented.");
    }

    public IReadOnlyList<ResourceKey> FindByTag(string tag)
    {
        throw new NotImplementedException("The frontmatter index is not yet implemented.");
    }

    // Walks file text for "project:" candidate references. Returns the unique set
    // of valid keys found; invalid candidates are silently dropped.
    public static HashSet<ResourceKey> ScanTextForReferences(string text)
    {
        var references = new HashSet<ResourceKey>();
        int searchStart = 0;

        while (true)
        {
            int markerIndex = text.IndexOf(ReferenceMarker, searchStart, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                break;
            }

            int keyStart = markerIndex + ReferenceMarker.Length;
            int keyEnd = keyStart;
            while (keyEnd < text.Length)
            {
                var current = text[keyEnd];
                if (char.IsWhiteSpace(current)
                    || char.IsControl(current)
                    || KeyTerminators.Contains(current))
                {
                    break;
                }
                keyEnd++;
            }

            if (keyEnd > keyStart)
            {
                var candidate = text.Substring(markerIndex, keyEnd - markerIndex);
                if (ResourceKey.TryCreate(candidate, out var key))
                {
                    references.Add(key);
                }
            }

            searchStart = keyEnd > markerIndex ? keyEnd : markerIndex + ReferenceMarker.Length;
        }

        return references;
    }

    private record FileScanResult(bool WasSkipped, HashSet<ResourceKey> References);

    private async Task<FileScanResult> ScanTextFileAsync(ResourceKey resourceKey, string absolutePath)
    {
        try
        {
            var fileInfo = new FileInfo(absolutePath);
            if (!fileInfo.Exists)
            {
                return new FileScanResult(WasSkipped: true, References: new HashSet<ResourceKey>());
            }

            if (fileInfo.Length > MaxScanFileSizeBytes)
            {
                _logger.LogInformation($"metadata scan: skipping {resourceKey} (size {fileInfo.Length} bytes exceeds limit)");
                return new FileScanResult(WasSkipped: true, References: new HashSet<ResourceKey>());
            }

            var extension = Path.GetExtension(absolutePath);
            if (!string.IsNullOrEmpty(extension)
                && _textBinarySniffer.IsBinaryExtension(extension))
            {
                return new FileScanResult(WasSkipped: true, References: new HashSet<ResourceKey>());
            }

            var isTextResult = _textBinarySniffer.IsTextFile(absolutePath);
            if (isTextResult.IsFailure || !isTextResult.Value)
            {
                return new FileScanResult(WasSkipped: true, References: new HashSet<ResourceKey>());
            }

            string text;
            try
            {
                text = await File.ReadAllTextAsync(absolutePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"metadata scan: failed to read {resourceKey}");
                return new FileScanResult(WasSkipped: true, References: new HashSet<ResourceKey>());
            }

            var references = ScanTextForReferences(text);
            return new FileScanResult(WasSkipped: false, References: references);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"metadata scan: failed to process {resourceKey}");
            return new FileScanResult(WasSkipped: true, References: new HashSet<ResourceKey>());
        }
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
        }
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
                return;
            }

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

    private void OnResourceCreated(object recipient, MonitoredResourceCreatedMessage message)
    {
        QueueRescan(message.Resource);
    }

    private void OnResourceChanged(object recipient, MonitoredResourceChangedMessage message)
    {
        QueueRescan(message.Resource);
    }

    private void OnResourceDeleted(object recipient, MonitoredResourceDeletedMessage message)
    {
        if (message.Resource.Root != ResourceKey.DefaultRoot)
        {
            return;
        }
        RemoveSourceFromIndexes(message.Resource);
    }

    private void OnResourceRenamed(object recipient, MonitoredResourceRenamedMessage message)
    {
        if (message.OldResource.Root == ResourceKey.DefaultRoot)
        {
            RemoveSourceFromIndexes(message.OldResource);
        }
        QueueRescan(message.NewResource);
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
                return;
            }
            var absolutePath = resolveResult.Value;

            if (!File.Exists(absolutePath))
            {
                RemoveSourceFromIndexes(resource);
                return;
            }

            var scanResult = await ScanTextFileAsync(resource, absolutePath);
            if (scanResult.WasSkipped)
            {
                RemoveSourceFromIndexes(resource);
                return;
            }

            UpdateSourceInIndexes(resource, scanResult.References);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"metadata scan: failed to rescan {resource}");
        }
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
