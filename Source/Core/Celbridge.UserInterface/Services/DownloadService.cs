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
    // How often a running download's progress is published. The platform reports every few kilobytes,
    // which is far more often than a list of rows can usefully redraw.
    private static readonly TimeSpan ProgressPublishInterval = TimeSpan.FromMilliseconds(250);

    // What a download still running needs: where the platform is writing it, where it is going, and the
    // transfer to stop it with. Held apart from the rows, so a row that leaves the list cannot orphan
    // its transfer, and holding the transfer is what keeps the platform's handle alive.
    private record InFlightDownload(ResourceKey StagingResource, ResourceKey Destination, IDownloadTransfer Transfer);

    private readonly ILogger<DownloadService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ILocalizerService _localizerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly DownloadList _rows = new();
    private readonly DownloadDestinations _destinations;
    private readonly DownloadedFileTracker _fileTracker;

    // Guards the transfers in flight and the progress clock.
    private readonly Lock _lock = new();
    private readonly Dictionary<long, InFlightDownload> _inFlightDownloads = new();
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

        _destinations = new DownloadDestinations(workspaceWrapper, projectService, localFileSystem, localizerService);
        _fileTracker = new DownloadedFileTracker(logger, workspaceWrapper, OnFilesMissing);

        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    public IReadOnlyList<DownloadEntry> Downloads => _rows.Downloads;

    public Result<string> GetDestinationFolderPath()
    {
        return _destinations.GetFolderPath();
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
        var rootHandlerRegistry = resourceService.RootHandlers;
        var resourceFileSystem = resourceService.FileSystem;

        var reserveResult = await _destinations.ReserveAsync(fileName);
        if (reserveResult.IsFailure)
        {
            RecordFailure(fileName, sourceUrl, _destinations.DescribeUnavailable(reserveResult));

            return Result<DownloadTicket>.Fail($"Failed to reserve a download destination for '{fileName}'")
                .WithErrors(reserveResult);
        }
        var destination = reserveResult.Value;

        // The destination stays reserved only for a download that started.
        var isStarted = false;
        try
        {
            // Probed before anything is staged, so a policy denial surfaces now rather than after the
            // whole transfer has run.
            var probeResult = await resourceFileSystem.GetInfoAsync(destination);
            if (probeResult.IsFailure)
            {
                _logger.LogError($"Download blocked: {probeResult.FirstErrorMessage}");

                RecordFailure(fileName, sourceUrl, _destinations.DescribeBlocked(probeResult, fileName));

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

            // Staged under temp:, which is wiped on workspace load, so an abandoned download leaves
            // nothing behind for long. The name is random, because two downloads may share a file name.
            var stagingName = $"{Path.GetFileNameWithoutExtension(Path.GetRandomFileName())}{Path.GetExtension(fileName)}";
            var stagingResource = new ResourceKey($"{ProjectConstants.TempFolder}:{ProjectConstants.DownloadsFolder}/{stagingName}");

            var resolveStagingResult = rootHandlerRegistry.ResolveResourcePath(stagingResource);
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
            isStarted = true;

            return new DownloadTicket(downloadId, stagingPath, destination);
        }
        finally
        {
            if (!isStarted)
            {
                _destinations.Release(destination);
            }
        }
    }

    public void ReportProgress(long downloadId, long bytesReceived, long? totalBytes)
    {
        var isUpdated = _rows.Update(downloadId, entry => entry with
        {
            BytesReceived = bytesReceived,
            TotalBytes = totalBytes
        });

        if (!isUpdated)
        {
            return;
        }

        // Every report updates the row, so a reader always sees the latest byte count. Only one report in each
        // interval is announced, since announcing each would redraw the rows faster than anyone can read them.
        var isPublished = false;
        lock (_lock)
        {
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
                // The project the download belonged to has closed, and its staging file goes with temp:.
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
            // Released once the file has landed, or once the move has failed, so no second download is
            // given the same path while this one is still moving.
            _destinations.Release(inFlightDownload.Destination);
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

        _destinations.Release(inFlightDownload.Destination);

        SettleAsFailed(downloadId, reason);
    }

    public async Task AbandonAsync(long downloadId, string reason)
    {
        var inFlightDownload = TakeInFlight(downloadId);
        if (inFlightDownload is null)
        {
            return;
        }

        // Taken out of flight before the transfer is stopped, so the cancellation the platform may report
        // finds nothing to settle and the row keeps the reason given here.
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

        _destinations.Release(inFlightDownload.Destination);

        SettleAsFailed(downloadId, reason);
    }

    public async Task CancelAsync(long downloadId)
    {
        var inFlightDownload = TakeInFlight(downloadId);
        if (inFlightDownload is null)
        {
            return;
        }

        // The platform may report the stop afterwards or not at all, and by then the download is out of
        // flight, so record the outcome here rather than wait for it.
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
        // The row of a running download stays, here and in Clear All.
        if (!_rows.RemoveFinished(downloadId))
        {
            return;
        }

        SendDownloadsChanged(hasArrival: false);
    }

    public void ClearAll()
    {
        // The row of a running download stays, so the user can follow the transfer's progress and stop the
        // transfer. Only the rows of finished downloads go, and their files stay in the project.
        if (!_rows.ClearFinished())
        {
            return;
        }

        SendDownloadsChanged(hasArrival: false);
    }

    private long RecordStart(
        string fileName,
        string sourceUrl,
        ResourceKey stagingResource,
        ResourceKey destination,
        IDownloadTransfer transfer)
    {
        var downloadId = _rows.AddRunning(fileName, sourceUrl);

        lock (_lock)
        {
            _inFlightDownloads.Add(downloadId, new InFlightDownload(stagingResource, destination, transfer));
        }

        SendDownloadsChanged(hasArrival: false);

        return downloadId;
    }

    // A download that never started still gets a row: a denial the user cannot see looks like the download
    // silently doing nothing.
    private void RecordFailure(string fileName, string sourceUrl, string reason)
    {
        _rows.AddFailed(fileName, sourceUrl, reason);

        SendDownloadsChanged(hasArrival: true);
    }

    private void SettleAsSucceeded(long downloadId, ResourceKey destination)
    {
        var isUpdated = _rows.Update(downloadId, entry => entry with
        {
            Resource = destination,
            Status = DownloadStatus.Succeeded
        });

        if (!isUpdated)
        {
            return;
        }

        SendDownloadsChanged(hasArrival: true);
    }

    private void SettleAsFailed(long downloadId, string reason)
    {
        var isUpdated = _rows.Update(downloadId, entry => entry with
        {
            Status = DownloadStatus.Failed,
            FailureReason = reason
        });

        if (!isUpdated)
        {
            return;
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

        _destinations.Release(inFlightDownload.Destination);

        SettleAsCanceled(downloadId);
    }

    // Announced as no arrival: a stopped download brought nothing to draw attention to.
    private void SettleAsCanceled(long downloadId)
    {
        var isUpdated = _rows.Update(downloadId, entry => entry with
        {
            Status = DownloadStatus.Canceled
        });

        if (!isUpdated)
        {
            return;
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

    // Clicking a succeeded row reveals its file. A row with a missing file would lead nowhere, so the row
    // leaves the list. Failed and canceled rows name no file, and they stay.
    private void OnFilesMissing(IReadOnlySet<ResourceKey> missingFiles)
    {
        if (!_rows.RemoveSucceeded(missingFiles))
        {
            return;
        }

        SendDownloadsChanged(hasArrival: false);
    }

    // Every record belongs to the project that is closing. A transfer still running is stopped, because
    // nothing is left to report its outcome, and temp: takes its staging file with it.
    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        List<InFlightDownload> abandonedDownloads;
        lock (_lock)
        {
            abandonedDownloads = _inFlightDownloads.Values.ToList();
            _inFlightDownloads.Clear();
        }

        _destinations.Clear();
        _fileTracker.Stop();
        var hasClearedRows = _rows.Clear();

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

        if (!hasClearedRows)
        {
            return;
        }

        SendDownloadsChanged(hasArrival: false);
    }

    // Sent outside the locks, so a recipient reading the list back cannot deadlock with a change on
    // another thread. Every change to the rows ends here, so the tracker follows the succeeded rows' files first.
    private void SendDownloadsChanged(bool hasArrival)
    {
        _fileTracker.Follow(_rows.GetSucceededFiles());

        var message = new DownloadsChangedMessage(hasArrival);
        _messengerService.Send(message);
    }

    private string GetString(string name, params object[] arguments)
    {
        return _localizerService.GetString(name, arguments);
    }
}
