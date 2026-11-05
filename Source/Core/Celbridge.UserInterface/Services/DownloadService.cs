using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Downloads;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

public sealed class DownloadService : IDownloadService
{
    // How often a running download's progress is published. The platform reports every few kilobytes,
    // which is far more often than a list of rows can usefully redraw.
    private static readonly TimeSpan ProgressPublishInterval = TimeSpan.FromMilliseconds(250);

    // Where a download's file goes. Resource is the file's key in the project, and FilePath is the path of a file
    // outside the project, which has no key. A free name in a project folder stays reserved until the file lands.
    // A path the system save dialog chose replaces any file there, since that dialog asked before replacing it.
    private sealed record Destination(ResourceKey Resource, string FilePath, bool IsReserved)
    {
        public bool IsInProject => !Resource.IsEmpty;
    }

    // Contains the state of a download still running. StagingResource is where the platform writes the file, at
    // StagingPath. Transfer is the platform's handle, which can cancel the download. Destination is set once the user
    // has chosen one. The service keeps this state apart from the rows, so removing a row keeps the transfer held.
    // Holding the transfer keeps the platform's handle alive. The service changes this state only under its lock.
    private sealed class InFlightDownload
    {
        public InFlightDownload(ResourceKey stagingResource, string stagingPath, IDownloadTransfer transfer)
        {
            StagingResource = stagingResource;
            StagingPath = stagingPath;
            Transfer = transfer;
        }

        public ResourceKey StagingResource { get; }
        public string StagingPath { get; }
        public IDownloadTransfer Transfer { get; }

        // Null until the user has chosen where the file goes.
        public Destination? Destination { get; set; }

        // True once the transfer has finished. The file then waits in temp: until the user has answered.
        public bool IsTransferFinished { get; set; }
    }

    private readonly ILogger<DownloadService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ILocalizerService _localizerService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IDialogService _dialogService;
    private readonly ILocalFileSystem _localFileSystem;

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
        IDialogService dialogService,
        ILocalFileSystem localFileSystem)
    {
        _logger = logger;
        _messengerService = messengerService;
        _localizerService = localizerService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _dialogService = dialogService;
        _localFileSystem = localFileSystem;

        _destinations = new DownloadDestinations(workspaceWrapper, localFileSystem, localizerService);
        _fileTracker = new DownloadedFileTracker(logger, workspaceWrapper, OnFilesMissing);

        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    public IReadOnlyList<DownloadEntry> Downloads => _rows.Downloads;

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

        // Only one dialog shows at a time. A download fails when it starts under an open dialog. That dialog may be
        // an earlier download's Download File dialog.
        if (_dialogService.IsDialogOpen)
        {
            RecordFailure(fileName, sourceUrl, GetString("Downloads_Failed"));

            return Result<DownloadTicket>.Fail($"Cannot start the download of '{fileName}' because a dialog is open");
        }

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var rootHandlerRegistry = resourceService.RootHandlers;
        var resourceFileSystem = resourceService.FileSystem;

        // temp:/ is wiped on workspace load, so the staging folder may not be there yet.
        var stagingFolder = new ResourceKey($"{ProjectConstants.TempFolder}:{ProjectConstants.DownloadsFolder}");
        var createFolderResult = await resourceFileSystem.CreateFolderAsync(stagingFolder);
        if (createFolderResult.IsFailure)
        {
            RecordFailure(fileName, sourceUrl, GetString("Downloads_Failed"));

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
            RecordFailure(fileName, sourceUrl, GetString("Downloads_Failed"));

            return Result<DownloadTicket>.Fail($"Failed to resolve the download staging path '{stagingResource}'")
                .WithErrors(resolveStagingResult);
        }
        var stagingPath = resolveStagingResult.Value;

        // Listed under the site's name until the user has chosen a folder.
        var downloadId = RecordStart(fileName, sourceUrl, stagingResource, stagingPath, transfer);

        _ = AskForDestinationAsync(downloadId, fileName, sourceUrl);

        return new DownloadTicket(downloadId, stagingPath);
    }

    // A download that failed to start still gets a row, so the user sees the failure.
    public void RecordFailure(string suggestedFileName, string sourceUrl, string reason)
    {
        var fileName = Path.GetFileName(suggestedFileName);

        _rows.AddFailed(fileName, sourceUrl, reason);

        SendDownloadsChanged(hasArrival: true);
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
        InFlightDownload? inFlightDownload;
        lock (_lock)
        {
            if (!_inFlightDownloads.TryGetValue(downloadId, out inFlightDownload))
            {
                return;
            }

            if (inFlightDownload.Destination is null)
            {
                // The dialog is still asking where the file goes, and the answer moves the file.
                inFlightDownload.IsTransferFinished = true;
                return;
            }

            _inFlightDownloads.Remove(downloadId);
        }

        await MoveIntoPlaceAsync(downloadId, inFlightDownload);
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

        await ReleaseDestinationAsync(inFlightDownload);

        SettleAsFailed(downloadId, reason);
    }

    public async Task AbandonAsync(long downloadId, string reason)
    {
        await StopAsFailedAsync(downloadId, reason);
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
        // transfer. Only the rows of finished downloads go, and their files stay where they were saved.
        if (!_rows.ClearFinished())
        {
            return;
        }

        SendDownloadsChanged(hasArrival: false);
    }

    public async Task CheckFilesOutsideProjectAsync()
    {
        var filePaths = _rows.GetSucceededFilePaths();
        if (filePaths.Count == 0)
        {
            return;
        }

        var missingPaths = new HashSet<string>();
        foreach (var filePath in filePaths)
        {
            var infoResult = await _localFileSystem.GetInfoAsync(filePath);
            if (infoResult.IsSuccess
                && infoResult.Value.Kind != StorageItemKind.File)
            {
                missingPaths.Add(filePath);
            }
        }

        if (!_rows.RemoveSucceededFilePaths(missingPaths))
        {
            return;
        }

        SendDownloadsChanged(hasArrival: false);
    }

    // Asks where the file goes while the transfer runs. The answer either reserves a destination or stops the
    // download. The service ignores the answer for a download that has already failed or been canceled.
    private async Task AskForDestinationAsync(long downloadId, string fileName, string sourceUrl)
    {
        try
        {
            var downloadFolder = await GetRememberedFolderAsync();
            var rememberedChoice = GetRememberedChoice();

            var showResult = await _dialogService.ShowDownloadFileDialogAsync(
                fileName,
                sourceUrl,
                downloadFolder,
                rememberedChoice);
            if (showResult.IsFailure)
            {
                _logger.LogWarning($"Could not ask where to save '{fileName}'. {showResult.FirstErrorMessage}");

                await StopAsFailedAsync(downloadId, GetString("Downloads_Failed"));
                return;
            }
            var answer = showResult.Value;

            if (answer.Choice == DownloadFileChoice.Cancel)
            {
                await CancelAsync(downloadId);
                return;
            }

            RememberChoice(answer);

            if (answer.Choice == DownloadFileChoice.OtherLocation)
            {
                await SaveToPathAsync(downloadId, fileName, answer.FilePath);
                return;
            }

            await SaveToFolderAsync(downloadId, fileName, answer.Folder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to ask where to save '{fileName}'");

            await StopAsFailedAsync(downloadId, GetString("Downloads_Failed"));
        }
    }

    // Returns the folder the dialog opens on. That is the folder last chosen in this project, or the project
    // folder when nothing was chosen or the chosen folder has gone. A folder renamed to another letter case
    // counts as gone, since the remembered key differs from the stored name.
    private async Task<ResourceKey> GetRememberedFolderAsync()
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return ResourceKey.Empty;
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var rememberedPath = workspaceService.BindableWorkspaceSettings.DownloadFolder;
        if (string.IsNullOrEmpty(rememberedPath)
            || !ResourceKey.TryCreate(rememberedPath, out var rememberedFolder))
        {
            return ResourceKey.Empty;
        }

        var infoResult = await workspaceService.ResourceService.FileSystem.GetInfoAsync(rememberedFolder);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.Folder)
        {
            return ResourceKey.Empty;
        }

        return rememberedFolder;
    }

    // Returns the option the user last saved a file with in this project. That is the In Project option until
    // Other Location has been used.
    private DownloadFileChoice GetRememberedChoice()
    {
        if (_workspaceWrapper.HasWorkspaceService
            && _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings.DownloadToOtherLocation)
        {
            return DownloadFileChoice.OtherLocation;
        }

        return DownloadFileChoice.InProject;
    }

    // Remembers the option the user saved with. Only a save with the In Project option changes the download folder,
    // so the folder chosen last stays for the next save in the project.
    private void RememberChoice(DownloadFileAnswer answer)
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return;
        }
        var workspaceSettings = _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;

        if (answer.Choice == DownloadFileChoice.OtherLocation)
        {
            workspaceSettings.DownloadToOtherLocation = true;
            return;
        }

        var rememberedPath = string.Empty;
        if (!answer.Folder.IsEmpty)
        {
            rememberedPath = answer.Folder.ToString();
        }

        workspaceSettings.DownloadFolder = rememberedPath;
        workspaceSettings.DownloadToOtherLocation = false;
    }

    // Reserves the saved name in the folder the user chose, and moves the file there once the transfer has
    // finished. A failure here stops the download, and the log records why.
    private async Task SaveToFolderAsync(long downloadId, string fileName, ResourceKey folder)
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return;
        }
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        // The answer can name any key, so the folder is checked before a name is reserved in it.
        if (!folder.IsEmpty)
        {
            var folderInfoResult = await resourceFileSystem.GetInfoAsync(folder);
            if (folderInfoResult.IsFailure
                || folderInfoResult.Value.Kind != StorageItemKind.Folder)
            {
                _logger.LogError($"Download folder '{folder}' is not a folder in the project");

                await StopAsFailedAsync(downloadId, GetString("Downloads_Failed"));
                return;
            }
        }

        var reserveResult = await _destinations.ReserveAsync(folder, fileName);
        if (reserveResult.IsFailure)
        {
            _logger.LogError($"Failed to reserve a download destination for '{fileName}'. {reserveResult.DiagnosticReport}");

            await StopAsFailedAsync(downloadId, GetString("Downloads_Failed"));
            return;
        }
        var destination = reserveResult.Value;

        // Probed before the move, so a policy denial stops the transfer straight away.
        var probeResult = await resourceFileSystem.GetInfoAsync(destination);
        if (probeResult.IsFailure)
        {
            _logger.LogError($"Download blocked: {probeResult.FirstErrorMessage}");

            _destinations.Release(destination);
            await StopAsFailedAsync(downloadId, _destinations.DescribeBlocked(probeResult, fileName));
            return;
        }

        // A taken name gets " (N)" before its extension, and the row shows the saved name.
        await SetDestinationAsync(downloadId, new Destination(destination, string.Empty, IsReserved: true));
    }

    // Moves the file to the path the system save dialog chose, once the transfer has finished. A path inside the
    // project has a resource key, so the file moves into the project the way a save to a project folder does.
    // A path outside the project moves through the local file system.
    private async Task SaveToPathAsync(long downloadId, string fileName, string filePath)
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return;
        }
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

        var keyResult = resourceService.RootHandlers.GetResourceKey(filePath);
        if (keyResult.IsFailure
            || keyResult.Value.Root != ResourceKey.DefaultRoot)
        {
            await SetDestinationAsync(downloadId, new Destination(ResourceKey.Empty, filePath, IsReserved: false));
            return;
        }
        var resource = keyResult.Value;

        // Probed before the move, so a path in a reserved folder like .git stops the transfer straight away.
        var probeResult = await resourceService.FileSystem.GetInfoAsync(resource);
        if (probeResult.IsFailure)
        {
            _logger.LogError($"Download blocked: {probeResult.FirstErrorMessage}");

            await StopAsFailedAsync(downloadId, _destinations.DescribeBlocked(probeResult, fileName));
            return;
        }

        await SetDestinationAsync(downloadId, new Destination(resource, string.Empty, IsReserved: false));
    }

    // Gives the download the destination the user chose, and shows the saved name in its row. The file moves there
    // at once if the transfer has already finished.
    private async Task SetDestinationAsync(long downloadId, Destination destination)
    {
        InFlightDownload? finishedDownload = null;
        var isInFlight = false;
        lock (_lock)
        {
            if (_inFlightDownloads.TryGetValue(downloadId, out var inFlightDownload))
            {
                isInFlight = true;
                inFlightDownload.Destination = destination;

                if (inFlightDownload.IsTransferFinished)
                {
                    _inFlightDownloads.Remove(downloadId);
                    finishedDownload = inFlightDownload;
                }
            }
        }

        if (!isInFlight)
        {
            // The transfer failed or was stopped while the dialog was asking.
            ReleaseReservation(destination);
            await DeleteEmptyFileAsync(destination);
            return;
        }

        var savedName = destination.IsInProject
            ? destination.Resource.ResourceName
            : Path.GetFileName(destination.FilePath);

        var isUpdated = _rows.Update(downloadId, entry => entry with
        {
            FileName = savedName
        });
        if (isUpdated)
        {
            SendDownloadsChanged(hasArrival: false);
        }

        if (finishedDownload is not null)
        {
            await MoveIntoPlaceAsync(downloadId, finishedDownload);
        }
    }

    // Moves a finished transfer's staged file to the destination the user chose.
    private async Task MoveIntoPlaceAsync(long downloadId, InFlightDownload inFlightDownload)
    {
        var destination = inFlightDownload.Destination!;

        try
        {
            if (!_workspaceWrapper.HasWorkspaceService)
            {
                // The download's project has closed. Loading the next workspace wipes temp:, and the staged file
                // with it.
                _logger.LogWarning(
                    $"A download failed: its project closed before '{inFlightDownload.StagingPath}' moved");

                SettleAsFailed(downloadId, GetString("Downloads_Failed"));
                return;
            }

            // temp: lies in the application data folder. The move is a rename when the destination shares that
            // folder's volume, and a copy otherwise.
            //
            // A file in the project moves through IMoveDownloadCommand. The command checks the destination against
            // the reserved folders, updates the Explorer, and runs in order with other changes to the project. Those
            // steps belong to the project, so a file outside the project moves directly by its path.
            Result moveResult;
            if (destination.IsInProject)
            {
                moveResult = await _commandService.ExecuteAsync<IMoveDownloadCommand>(command =>
                {
                    command.SourceResource = inFlightDownload.StagingResource;
                    command.DestResource = destination.Resource;
                    command.ReplaceExisting = !destination.IsReserved;
                });
            }
            else
            {
                moveResult = await _localFileSystem.MoveFileAsync(
                    inFlightDownload.StagingPath,
                    destination.FilePath,
                    overwrite: true);
            }

            if (moveResult.IsFailure)
            {
                var destinationName = destination.IsInProject
                    ? destination.Resource.ToString()
                    : destination.FilePath;
                _logger.LogError(
                    $"Failed to move the downloaded file to '{destinationName}'. {moveResult.DiagnosticReport}");

                SettleAsFailed(downloadId, GetString("Downloads_Failed"));

                // A move that failed leaves the staged file behind.
                if (_workspaceWrapper.HasWorkspaceService)
                {
                    var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
                    await resourceFileSystem.DeleteAsync(inFlightDownload.StagingResource);
                }
                await DeleteEmptyFileAsync(destination);
                return;
            }

            // The badge announces the arrival, and the download's row in the list reveals the file. The Explorer
            // selection stays put, so the keyboard stays where the user was working.
            SettleAsSucceeded(downloadId, destination);
        }
        finally
        {
            // Released once the file has landed or the move has failed. Until then a second download gets
            // another path.
            ReleaseReservation(destination);
        }
    }

    // Stops a running download, records the download as failed with the reason given, and deletes the staged
    // file.
    private async Task StopAsFailedAsync(long downloadId, string reason)
    {
        var inFlightDownload = TakeInFlight(downloadId);
        if (inFlightDownload is null)
        {
            return;
        }

        // Taken out of flight before the transfer is stopped. A cancellation the platform reports afterwards then
        // finds nothing to settle, and the row keeps the reason given here.
        try
        {
            inFlightDownload.Transfer.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop a download");
        }

        if (_workspaceWrapper.HasWorkspaceService)
        {
            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            await resourceFileSystem.DeleteAsync(inFlightDownload.StagingResource);
        }

        await ReleaseDestinationAsync(inFlightDownload);

        SettleAsFailed(downloadId, reason);
    }

    // Frees the destination of a download that saves nothing there.
    private async Task ReleaseDestinationAsync(InFlightDownload inFlightDownload)
    {
        if (inFlightDownload.Destination is not Destination destination)
        {
            return;
        }

        ReleaseReservation(destination);
        await DeleteEmptyFileAsync(destination);
    }

    // The system save dialog on Windows creates an empty file at the path it returns. A download that saves nothing
    // deletes that file. A file with content stays, because the save dialog only asked to replace it. A reserved
    // destination never has a file, since the In Project option picks a free name.
    private async Task DeleteEmptyFileAsync(Destination destination)
    {
        if (destination.IsReserved)
        {
            return;
        }

        if (destination.IsInProject)
        {
            if (!_workspaceWrapper.HasWorkspaceService)
            {
                return;
            }
            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

            var resourceInfoResult = await resourceFileSystem.GetInfoAsync(destination.Resource);
            if (resourceInfoResult.IsFailure
                || !IsEmptyFile(resourceInfoResult.Value))
            {
                return;
            }

            var deleteResult = await resourceFileSystem.DeleteAsync(destination.Resource);
            if (deleteResult.IsFailure)
            {
                _logger.LogWarning(deleteResult, $"Failed to delete the empty file at '{destination.Resource}'");
            }
            return;
        }

        var infoResult = await _localFileSystem.GetInfoAsync(destination.FilePath);
        if (infoResult.IsFailure
            || !IsEmptyFile(infoResult.Value))
        {
            return;
        }

        var deleteFileResult = await _localFileSystem.DeleteFileAsync(destination.FilePath);
        if (deleteFileResult.IsFailure)
        {
            _logger.LogWarning(deleteFileResult, $"Failed to delete the empty file at '{destination.FilePath}'");
        }
    }

    private static bool IsEmptyFile(StorageItemInfo info)
    {
        return info.Kind == StorageItemKind.File
            && info.Size == 0;
    }

    private void ReleaseReservation(Destination destination)
    {
        if (destination.IsReserved)
        {
            _destinations.Release(destination.Resource);
        }
    }

    private long RecordStart(
        string fileName,
        string sourceUrl,
        ResourceKey stagingResource,
        string stagingPath,
        IDownloadTransfer transfer)
    {
        var downloadId = _rows.AddRunning(fileName, sourceUrl);

        lock (_lock)
        {
            _inFlightDownloads.Add(downloadId, new InFlightDownload(stagingResource, stagingPath, transfer));
        }

        SendDownloadsChanged(hasArrival: false);

        return downloadId;
    }

    private void SettleAsSucceeded(long downloadId, Destination destination)
    {
        var isUpdated = _rows.Update(downloadId, entry => entry with
        {
            Resource = destination.Resource,
            FilePath = destination.FilePath,
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

        await ReleaseDestinationAsync(inFlightDownload);

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
        if (!_rows.RemoveSucceededResources(missingFiles))
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
        _fileTracker.Follow(_rows.GetSucceededResources());

        var message = new DownloadsChangedMessage(hasArrival);
        _messengerService.Send(message);
    }

    private string GetString(string name, params object[] arguments)
    {
        return _localizerService.GetString(name, arguments);
    }
}
