using Celbridge.Commands;
using Celbridge.Downloads;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

public sealed class DownloadService : IDownloadService
{
    /// <summary>
    /// The most downloads the list holds at once. Past it the oldest is dropped to make room.
    /// </summary>
    public const int DownloadLimit = 50;

    // How often a running download's progress is published. The platform reports every few kilobytes,
    // which is far more often than a list of rows can usefully redraw.
    private static readonly TimeSpan ProgressPublishInterval = TimeSpan.FromMilliseconds(250);

    // What a download still running needs while it runs and when it settles: where the platform is
    // writing it, where it is going, and the transfer to stop it with. Held apart from the entries, so a
    // row that leaves the list cannot orphan the transfer behind it, and holding the transfer is also what
    // keeps the platform's handle for the download alive.
    private record InFlightDownload(ResourceKey StagingResource, ResourceKey Destination, IDownloadTransfer Transfer);

    private readonly ILogger<DownloadService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ILocalizerService _localizerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly ILocalFileSystem _localFileSystem;

    private readonly object _lock = new();

    // Newest first, which is the order they are listed in.
    private readonly List<DownloadEntry> _entries = new();
    private readonly Dictionary<long, InFlightDownload> _inFlightDownloads = new();

    // The destinations promised to a download, held until its file is at that path or putting it there has
    // failed. Held apart from the transfers, which are let go of as soon as one stops running, leaving a
    // window before the move lands where a second download of the same name would find the path free.
    private readonly HashSet<ResourceKey> _reservedDestinations = new();

    // Reserving a destination and recording it has to be indivisible, or two downloads of the same name
    // started together both find the same path free.
    private readonly SemaphoreSlim _beginGate = new(1, 1);

    private IReadOnlyList<DownloadEntry> _downloads = Array.Empty<DownloadEntry>();

    private long _nextDownloadId = 1;
    private long _nextProgressPublishTick;

    public DownloadService(
        ILogger<DownloadService> logger,
        IMessengerService messengerService,
        ILocalizerService localizerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        ILocalFileSystem localFileSystem)
    {
        _logger = logger;
        _messengerService = messengerService;
        _localizerService = localizerService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _localFileSystem = localFileSystem;

        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdated);
        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    public IReadOnlyList<DownloadEntry> Downloads
    {
        get
        {
            lock (_lock)
            {
                return _downloads;
            }
        }
    }

    public Result<string> GetDestinationFolderPath()
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return Result<string>.Fail("No workspace is loaded");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var downloadsFolder = ResolveDownloadsFolder(resourceRegistry);

        var resolveResult = resourceRegistry.ResolveResourcePath(downloadsFolder);
        if (resolveResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to resolve the downloads folder '{downloadsFolder}'")
                .WithErrors(resolveResult);
        }

        try
        {
            return Path.GetFullPath(resolveResult.Value);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to resolve the downloads folder '{downloadsFolder}'")
                .WithException(ex);
        }
    }

    public async Task<Result<DownloadTicket>> BeginAsync(
        string suggestedFileName,
        string sourceUrl,
        IDownloadTransfer transfer)
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return Result<DownloadTicket>.Fail("Cannot start a download because no workspace is loaded");
        }

        var fileName = Path.GetFileName(suggestedFileName);
        if (string.IsNullOrEmpty(fileName))
        {
            return Result<DownloadTicket>.Fail($"Cannot start a download because '{suggestedFileName}' names no file");
        }

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var resourceRegistry = resourceService.Registry;
        var resourceFileSystem = resourceService.FileSystem;

        await _beginGate.WaitAsync();
        try
        {
            var reserveResult = await ReserveDestinationAsync(resourceRegistry, fileName);
            if (reserveResult.IsFailure)
            {
                RecordFailure(fileName, sourceUrl, GetString("Downloads_DestinationUnavailable"));

                return Result<DownloadTicket>.Fail($"Failed to reserve a download destination for '{fileName}'")
                    .WithErrors(reserveResult);
            }
            var destination = reserveResult.Value;

            // Probing the destination before anything is staged is what surfaces a policy denial up front
            // instead of after the transfer has run.
            var probeResult = await resourceFileSystem.GetInfoAsync(destination);
            if (probeResult.IsFailure)
            {
                _logger.LogError($"Download blocked: {probeResult.FirstErrorMessage}");

                RecordFailure(fileName, sourceUrl, DescribeBlockedDestination(probeResult, fileName));

                return Result<DownloadTicket>.Fail($"The download destination '{destination}' is not permitted")
                    .WithErrors(probeResult);
            }

            // temp:/ is wiped on workspace load, so the staging folder may not be there yet.
            var stagingFolder = new ResourceKey($"{ProjectConstants.TempFolder}:{ProjectConstants.DownloadsFolder}");
            var createFolderResult = await resourceFileSystem.CreateFolderAsync(stagingFolder);
            if (createFolderResult.IsFailure)
            {
                RecordFailure(fileName, sourceUrl, GetString("Downloads_DestinationUnavailable"));

                return Result<DownloadTicket>.Fail($"Failed to create the download staging folder '{stagingFolder}'")
                    .WithErrors(createFolderResult);
            }

            // The transfer is staged under temp: so the wipe-on-load policy bounds what an abandoned
            // download leaves behind. The name is random, because two downloads may share a file name.
            var stagingName = $"{Path.GetFileNameWithoutExtension(Path.GetRandomFileName())}{Path.GetExtension(fileName)}";
            var stagingResource = new ResourceKey($"{ProjectConstants.TempFolder}:{ProjectConstants.DownloadsFolder}/{stagingName}");

            var resolveStagingResult = resourceRegistry.ResolveResourcePath(stagingResource);
            if (resolveStagingResult.IsFailure)
            {
                RecordFailure(fileName, sourceUrl, GetString("Downloads_DestinationUnavailable"));

                return Result<DownloadTicket>.Fail($"Failed to resolve the download staging path '{stagingResource}'")
                    .WithErrors(resolveStagingResult);
            }
            var stagingPath = resolveStagingResult.Value;

            // Listed under the name it will be saved as, which differs from the suggested one when that
            // name was already taken.
            var downloadId = RecordStart(destination.ResourceName, sourceUrl, stagingResource, destination, transfer);

            return new DownloadTicket(downloadId, stagingPath, destination);
        }
        finally
        {
            _beginGate.Release();
        }
    }

    public void ReportProgress(long downloadId, long bytesReceived, long? totalBytes)
    {
        var isPublished = false;

        lock (_lock)
        {
            var isUpdated = TryUpdateEntry(downloadId, entry => entry with
            {
                BytesReceived = bytesReceived,
                TotalBytes = totalBytes
            });

            if (!isUpdated)
            {
                return;
            }

            // The list is republished on every report so a reader always sees the latest byte count, but
            // announcing each one would redraw the rows far faster than anyone can read them.
            var currentTick = Environment.TickCount64;
            if (currentTick >= _nextProgressPublishTick)
            {
                _nextProgressPublishTick = currentTick + (long)ProgressPublishInterval.TotalMilliseconds;
                isPublished = true;
            }
        }

        if (isPublished)
        {
            SendDownloadsChanged(hasArrival: false);
        }
    }

    public async Task CompleteAsync(long downloadId)
    {
        var inFlightDownload = TakeInFlight(downloadId);
        if (inFlightDownload is null)
        {
            return;
        }

        try
        {
            if (!_workspaceWrapper.HasWorkspaceService)
            {
                // The project the download belonged to has gone, and its staging file goes with temp:.
                SettleAsFailed(downloadId, GetString("Downloads_ImportFailed"));
                return;
            }

            // Moved rather than copied. temp: lives inside the project folder, so the move is a rename: it
            // writes no second copy for antivirus to scan, keeps the mark of the web the platform gave the
            // file, and puts no limit on its size.
            var moveResult = await _commandService.ExecuteAsync<IMoveDownloadCommand>(command =>
            {
                command.SourceResource = inFlightDownload.StagingResource;
                command.DestResource = inFlightDownload.Destination;
            });

            if (moveResult.IsFailure)
            {
                _logger.LogError(
                    $"Failed to move the downloaded file to '{inFlightDownload.Destination}'. {moveResult.DiagnosticReport}");

                SettleAsFailed(downloadId, GetString("Downloads_ImportFailed"));

                // A move that failed leaves the staged file behind.
                if (_workspaceWrapper.HasWorkspaceService)
                {
                    var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
                    await resourceFileSystem.DeleteAsync(inFlightDownload.StagingResource);
                }
                return;
            }

            // Nothing in the Explorer is selected or expanded, since selecting there takes the keyboard from
            // whatever the user was doing when the download finished. The badge announces the arrival, and its
            // row finds the file.
            SettleAsSucceeded(downloadId, inFlightDownload.Destination);
        }
        finally
        {
            // Given up once the file is at the destination, or once putting it there has failed, so no
            // second download can be handed the same path while the move is in flight.
            ReleaseDestination(inFlightDownload.Destination);
        }
    }

    public async Task FailAsync(long downloadId, string reason)
    {
        var inFlightDownload = TakeInFlight(downloadId);
        if (inFlightDownload is null)
        {
            return;
        }

        if (_workspaceWrapper.HasWorkspaceService)
        {
            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            await resourceFileSystem.DeleteAsync(inFlightDownload.StagingResource);
        }

        ReleaseDestination(inFlightDownload.Destination);

        SettleAsFailed(downloadId, reason);
    }

    public async Task AbandonAsync(long downloadId, string reason)
    {
        var inFlightDownload = TakeInFlight(downloadId);
        if (inFlightDownload is null)
        {
            return;
        }

        // Taken out of flight before the transfer is stopped, so the stop the platform may report finds
        // nothing to settle and the row keeps the reason given here rather than reading as a cancellation.
        try
        {
            inFlightDownload.Transfer.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop an abandoned download");
        }

        if (_workspaceWrapper.HasWorkspaceService)
        {
            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            await resourceFileSystem.DeleteAsync(inFlightDownload.StagingResource);
        }

        ReleaseDestination(inFlightDownload.Destination);

        SettleAsFailed(downloadId, reason);
    }

    public async Task CancelAsync(long downloadId)
    {
        var inFlightDownload = TakeInFlight(downloadId);
        if (inFlightDownload is null)
        {
            return;
        }

        // The platform may report the stop afterwards or not at all, and by then the download is no
        // longer in flight, so the outcome is recorded here rather than waited for.
        try
        {
            inFlightDownload.Transfer.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel a download");
        }

        await SettleCanceledAsync(downloadId, inFlightDownload);
    }

    public async Task ReportCanceledAsync(long downloadId)
    {
        var inFlightDownload = TakeInFlight(downloadId);
        if (inFlightDownload is null)
        {
            return;
        }

        await SettleCanceledAsync(downloadId, inFlightDownload);
    }

    public void Remove(long downloadId)
    {
        lock (_lock)
        {
            // A download still running keeps its row, as it does through Clear All.
            var removedCount = _entries.RemoveAll(entry =>
                entry.Id == downloadId &&
                entry.Status != DownloadStatus.InProgress);

            if (removedCount == 0)
            {
                return;
            }

            UpdateDownloads();
        }

        SendDownloadsChanged(hasArrival: false);
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            // A download still running keeps its row, so its progress stays in view and it can still be
            // stopped. Only the records of finished downloads go, and the files they landed on stay.
            var removedCount = _entries.RemoveAll(entry => entry.Status != DownloadStatus.InProgress);
            if (removedCount == 0)
            {
                return;
            }

            UpdateDownloads();
        }

        SendDownloadsChanged(hasArrival: false);
    }

    // Downloads land in the folder the project names, which is downloads/ unless it names another, so the
    // project root stays uncluttered when a session downloads several files.
    private ResourceKey ResolveDownloadsFolder(IResourceRegistry resourceRegistry)
    {
        var configuredFolder = _projectService.CurrentProject?.Config.Resources.DownloadsFolder ?? string.Empty;

        return DownloadsFolderPath.Resolve(resourceRegistry, configuredFolder);
    }

    // Appends " (N)" before the extension until the destination is free: absent from disk, and not
    // already promised to a download that is still running.
    private async Task<Result<ResourceKey>> ReserveDestinationAsync(IResourceRegistry resourceRegistry, string fileName)
    {
        var downloadsFolder = ResolveDownloadsFolder(resourceRegistry);

        if (!ResourceKey.TryCreate($"{downloadsFolder.Path}/{fileName}", out var requestedResource))
        {
            return Result<ResourceKey>.Fail($"'{fileName}' is not a valid download file name");
        }

        var resolveResult = resourceRegistry.ResolveResourcePath(requestedResource);
        if (resolveResult.IsFailure)
        {
            return Result<ResourceKey>.Fail($"Failed to resolve the download destination '{requestedResource}'")
                .WithErrors(resolveResult);
        }

        try
        {
            var requestedPath = Path.GetFullPath(resolveResult.Value);
            var folderPath = Path.GetDirectoryName(requestedPath)!;
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(requestedPath);
            var extension = Path.GetExtension(requestedPath);
            var candidateName = Path.GetFileName(requestedPath);
            var count = 1;

            while (true)
            {
                var candidatePath = Path.Combine(folderPath, candidateName);

                var getKeyResult = resourceRegistry.GetResourceKey(candidatePath);
                if (getKeyResult.IsFailure)
                {
                    return Result<ResourceKey>.Fail($"Failed to resolve the download destination '{candidatePath}'")
                        .WithErrors(getKeyResult);
                }
                var candidateResource = getKeyResult.Value;

                var infoResult = await _localFileSystem.GetInfoAsync(candidatePath);
                var existsOnDisk = infoResult.IsSuccess &&
                    infoResult.Value.Kind != StorageItemKind.NotFound;

                if (!existsOnDisk &&
                    !IsDestinationReserved(candidateResource))
                {
                    return candidateResource;
                }

                candidateName = string.IsNullOrEmpty(extension)
                    ? $"{nameWithoutExtension} ({count})"
                    : $"{nameWithoutExtension} ({count}){extension}";
                count++;
            }
        }
        catch (Exception ex)
        {
            return Result<ResourceKey>.Fail($"Failed to find a free download destination for '{fileName}'")
                .WithException(ex);
        }
    }

    // Only Celbridge's own reservations can deny a destination, since nothing a project configures denies a
    // read or a write, and a downloads folder inside one is refused before it is ever used. The row names the
    // reserved folder the policy matched, should a destination still reach one.
    private string DescribeBlockedDestination(Result probeResult, string fileName)
    {
        if (probeResult.FirstException is not PolicyDenialError denial)
        {
            return GetString("Downloads_DestinationUnavailable");
        }

        var reservedName = denial.MatchedRule.Pattern.Split('/')[0];

        return GetString("Downloads_Blocked", fileName, reservedName);
    }

    private bool IsDestinationReserved(ResourceKey resource)
    {
        lock (_lock)
        {
            return _reservedDestinations.Contains(resource);
        }
    }

    private void ReleaseDestination(ResourceKey destination)
    {
        lock (_lock)
        {
            _reservedDestinations.Remove(destination);
        }
    }

    private long RecordStart(
        string fileName,
        string sourceUrl,
        ResourceKey stagingResource,
        ResourceKey destination,
        IDownloadTransfer transfer)
    {
        long downloadId;

        lock (_lock)
        {
            downloadId = TakeDownloadId();

            _inFlightDownloads.Add(downloadId, new InFlightDownload(stagingResource, destination, transfer));
            _reservedDestinations.Add(destination);

            var entry = new DownloadEntry(
                downloadId,
                fileName,
                ResourceKey.Empty,
                sourceUrl,
                DownloadStatus.InProgress,
                FailureReason: string.Empty,
                DateTimeOffset.UtcNow);

            AddEntry(entry);
        }

        SendDownloadsChanged(hasArrival: false);

        return downloadId;
    }

    // A download that never started still gets a row, because a denial the user cannot see reads as the
    // download having silently done nothing.
    private void RecordFailure(string fileName, string sourceUrl, string reason)
    {
        lock (_lock)
        {
            var entry = new DownloadEntry(
                TakeDownloadId(),
                fileName,
                ResourceKey.Empty,
                sourceUrl,
                DownloadStatus.Failed,
                reason,
                DateTimeOffset.UtcNow);

            AddEntry(entry);
        }

        SendDownloadsChanged(hasArrival: true);
    }

    private void SettleAsSucceeded(long downloadId, ResourceKey destination)
    {
        lock (_lock)
        {
            var isUpdated = TryUpdateEntry(downloadId, entry => entry with
            {
                Resource = destination,
                Status = DownloadStatus.Succeeded
            });

            if (!isUpdated)
            {
                return;
            }
        }

        SendDownloadsChanged(hasArrival: true);
    }

    private void SettleAsFailed(long downloadId, string reason)
    {
        lock (_lock)
        {
            var isUpdated = TryUpdateEntry(downloadId, entry => entry with
            {
                Status = DownloadStatus.Failed,
                FailureReason = reason
            });

            if (!isUpdated)
            {
                return;
            }
        }

        SendDownloadsChanged(hasArrival: true);
    }

    private async Task SettleCanceledAsync(long downloadId, InFlightDownload inFlightDownload)
    {
        if (_workspaceWrapper.HasWorkspaceService)
        {
            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            await resourceFileSystem.DeleteAsync(inFlightDownload.StagingResource);
        }

        ReleaseDestination(inFlightDownload.Destination);

        SettleAsCanceled(downloadId);
    }

    // Announced as no arrival, since a download that was stopped has brought nothing to draw attention to.
    private void SettleAsCanceled(long downloadId)
    {
        lock (_lock)
        {
            var isUpdated = TryUpdateEntry(downloadId, entry => entry with
            {
                Status = DownloadStatus.Canceled
            });

            if (!isUpdated)
            {
                return;
            }
        }

        SendDownloadsChanged(hasArrival: false);
    }

    private InFlightDownload? TakeInFlight(long downloadId)
    {
        lock (_lock)
        {
            if (!_inFlightDownloads.Remove(downloadId, out var inFlightDownload))
            {
                return null;
            }

            return inFlightDownload;
        }
    }

    // A succeeded row's whole value is that clicking it reveals the file, so a row whose file has gone
    // leaves the list rather than sitting there as a dead end. A failed or canceled row names no file and
    // stays.
    private void OnResourceRegistryUpdated(object recipient, ResourceRegistryUpdatedMessage message)
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        lock (_lock)
        {
            var removedCount = _entries.RemoveAll(entry =>
                entry.Status == DownloadStatus.Succeeded &&
                resourceRegistry.GetResource(entry.Resource).IsFailure);

            if (removedCount == 0)
            {
                return;
            }

            UpdateDownloads();
        }

        SendDownloadsChanged(hasArrival: false);
    }

    // Every record describes the project that is ending. A transfer still running is stopped, since nothing
    // is left to report its outcome, and its staging file goes with temp:.
    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        List<InFlightDownload> abandonedDownloads;
        bool hasClearedEntries;

        lock (_lock)
        {
            abandonedDownloads = _inFlightDownloads.Values.ToList();

            _inFlightDownloads.Clear();
            _reservedDestinations.Clear();

            hasClearedEntries = _entries.Count > 0;
            if (hasClearedEntries)
            {
                _entries.Clear();

                UpdateDownloads();
            }
        }

        // Stopped outside the lock, since a transfer can report back into the service as it stops.
        foreach (var abandonedDownload in abandonedDownloads)
        {
            try
            {
                abandonedDownload.Transfer.Cancel();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop a download whose project closed");
            }
        }

        if (!hasClearedEntries)
        {
            return;
        }

        SendDownloadsChanged(hasArrival: false);
    }

    private long TakeDownloadId()
    {
        var downloadId = _nextDownloadId;
        _nextDownloadId++;

        return downloadId;
    }

    // A download still running keeps its row, so its progress stays in view and it can still be stopped.
    // With nothing settled left to drop, the list grows past the limit rather than losing one.
    private void AddEntry(DownloadEntry entry)
    {
        while (_entries.Count >= DownloadLimit)
        {
            var oldestSettledIndex = _entries.FindLastIndex(
                candidate => candidate.Status != DownloadStatus.InProgress);
            if (oldestSettledIndex < 0)
            {
                break;
            }

            _entries.RemoveAt(oldestSettledIndex);
        }

        _entries.Insert(0, entry);

        UpdateDownloads();
    }

    private bool TryUpdateEntry(long downloadId, Func<DownloadEntry, DownloadEntry> update)
    {
        var entryIndex = _entries.FindIndex(entry => entry.Id == downloadId);
        if (entryIndex < 0)
        {
            return false;
        }

        _entries[entryIndex] = update(_entries[entryIndex]);

        UpdateDownloads();

        return true;
    }

    // Each change publishes a new list, since a reader on another thread may still hold the previous one.
    private void UpdateDownloads()
    {
        var downloads = new List<DownloadEntry>(_entries);

        _downloads = downloads.AsReadOnly();
    }

    // Sent outside the lock, so a recipient reading the list back cannot deadlock with a change on
    // another thread.
    private void SendDownloadsChanged(bool hasArrival)
    {
        var message = new DownloadsChangedMessage(hasArrival);
        _messengerService.Send(message);
    }

    private string GetString(string name, params object[] arguments)
    {
        return _localizerService.GetString(name, arguments);
    }
}
