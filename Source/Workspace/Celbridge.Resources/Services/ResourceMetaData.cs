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
            var transientFailures = new List<ResourceKey>();

            int filesScanned = 0;
            int filesSkipped = 0;
            int referencesFound = 0;

            foreach (var (resourceKey, absolutePath) in files)
            {
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
                        continue;

                    case ScanOutcome.Indexed:
                        filesScanned++;
                        if (scanResult.References.Count > 0)
                        {
                            referencesFound += scanResult.References.Count;
                            ApplyReferences(newReferencersByTarget, newReferencesBySource, resourceKey, scanResult.References);
                        }
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

            // FrontmatterEntries is always zero because frontmatter scanning is
            // not yet implemented on this service.
            var report = new MetaDataScanReport(
                FilesScanned: filesScanned,
                FilesSkipped: filesSkipped,
                ReferencesFound: referencesFound,
                FrontmatterEntries: 0,
                Elapsed: stopwatch.Elapsed);

            _logger.LogDebug($"Metadata rebuild complete: {filesScanned} scanned, {filesSkipped} skipped ({transientFailures.Count} transient retries queued), {referencesFound} references in {stopwatch.ElapsedMilliseconds}ms");

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

    private record FileScanResult(ScanOutcome Outcome, HashSet<ResourceKey> References);

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
        return new FileScanResult(ScanOutcome.Indexed, references);
    }

    // IOException covers file-locked, sharing-violation, network-share blip;
    // UnauthorizedAccessException can fire transiently on Windows while an
    // antivirus or backup product holds the file. Both are worth retrying.
    private static bool IsTransientIoFailure(Exception ex)
    {
        return ex is IOException
            || ex is UnauthorizedAccessException;
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
        // A fresh watcher event means the file's state is changing; reset the
        // retry budget so a file that previously gave up after MaxScanRetryAttempts
        // gets re-scanned with full budget on its next legitimate change.
        _transientFailureCounts.TryRemove(message.Resource, out _);
        QueueRescan(message.Resource);
    }

    private void OnResourceChanged(object recipient, MonitoredResourceChangedMessage message)
    {
        _transientFailureCounts.TryRemove(message.Resource, out _);
        QueueRescan(message.Resource);
    }

    private void OnResourceDeleted(object recipient, MonitoredResourceDeletedMessage message)
    {
        if (message.Resource.Root != ResourceKey.DefaultRoot)
        {
            return;
        }
        RemoveSourceFromIndexes(message.Resource);
        _transientFailureCounts.TryRemove(message.Resource, out _);
    }

    private void OnResourceRenamed(object recipient, MonitoredResourceRenamedMessage message)
    {
        if (message.OldResource.Root == ResourceKey.DefaultRoot)
        {
            RemoveSourceFromIndexes(message.OldResource);
            _transientFailureCounts.TryRemove(message.OldResource, out _);
        }
        _transientFailureCounts.TryRemove(message.NewResource, out _);
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
                _transientFailureCounts.TryRemove(resource, out _);
                return;
            }
            var absolutePath = resolveResult.Value;

            if (!File.Exists(absolutePath))
            {
                RemoveSourceFromIndexes(resource);
                _transientFailureCounts.TryRemove(resource, out _);
                return;
            }

            var scanResult = await ScanTextFileAsync(resource, absolutePath);
            switch (scanResult.Outcome)
            {
                case ScanOutcome.Indexed:
                    UpdateSourceInIndexes(resource, scanResult.References);
                    _transientFailureCounts.TryRemove(resource, out _);
                    break;

                case ScanOutcome.ExcludedPermanently:
                    RemoveSourceFromIndexes(resource);
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
