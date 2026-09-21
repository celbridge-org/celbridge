using System.Collections.Concurrent;
using Celbridge.Commands;
using Celbridge.Downloads;
using Celbridge.Explorer;
using Celbridge.Localization;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Tests.FileSystem;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// The download badge lists what this service holds, and every download in the application goes through
/// it. These tests pin the rules that keep a download inside the project and its record honest: a taken
/// name is uniquified even against a transfer still running, a denied destination is reported rather than
/// written, a staged file is always cleaned up, and a record whose file has gone leaves the list.
/// </summary>
[TestFixture]
public class DownloadServiceTests
{
    private record Move(ResourceKey SourceResource, ResourceKey DestResource);

    private static readonly string ProjectFolderPath =
        Path.Combine(Path.GetTempPath(), "celbridge-download-tests", "project");

    private static readonly string TempFolderPath =
        Path.Combine(ProjectFolderPath, ".celbridge", "temp");

    private IMessengerService _messengerService = null!;
    private ICommandService _commandService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private FakeFileSystem _localFileSystem = null!;
    private List<Move> _moves = null!;
    private ConcurrentDictionary<string, IDownloadTransfer> _transfers = null!;
    private MessageHandler<object, ResourceRegistryUpdatedMessage>? _registryUpdatedHandler;
    private MessageHandler<object, WorkspaceUnloadedMessage>? _workspaceUnloadedHandler;

    private DownloadService _downloadService = null!;

    [SetUp]
    public void Setup()
    {
        _moves = new List<Move>();
        _transfers = new ConcurrentDictionary<string, IDownloadTransfer>();
        _localFileSystem = new FakeFileSystem();
        _localFileSystem.SeedFolder(Path.Combine(ProjectFolderPath, "downloads"));

        _messengerService = Substitute.For<IMessengerService>();

        // The substitute records registrations rather than dispatching, so each handler the service
        // registers is captured for the tests to invoke.
        _registryUpdatedHandler = null;
        _messengerService
            .When(messenger => messenger.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, ResourceRegistryUpdatedMessage>>()))
            .Do(callInfo => _registryUpdatedHandler =
                callInfo.Arg<MessageHandler<object, ResourceRegistryUpdatedMessage>>());

        _workspaceUnloadedHandler = null;
        _messengerService
            .When(messenger => messenger.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, WorkspaceUnloadedMessage>>()))
            .Do(callInfo => _workspaceUnloadedHandler =
                callInfo.Arg<MessageHandler<object, WorkspaceUnloadedMessage>>());

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>())
            .Returns(callInfo => ResolvePath(callInfo.Arg<ResourceKey>()));
        _resourceRegistry.GetResourceKey(Arg.Any<string>())
            .Returns(callInfo => ResolveResource(callInfo.Arg<string>()));
        _resourceRegistry.GetResource(Arg.Any<ResourceKey>())
            .Returns(callInfo => GetResource(callInfo.Arg<ResourceKey>()));

        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        _resourceFileSystem.CreateFolderAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result.Ok()));
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(
                new StorageItemInfo(StorageItemKind.NotFound, 0, DateTime.UtcNow, FileSystemAttributes.None))));
        _resourceFileSystem.DeleteAsync(Arg.Any<ResourceKey>())
            .Returns(callInfo => DeleteAsync(callInfo.Arg<ResourceKey>()));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);
        resourceService.FileSystem.Returns(_resourceFileSystem);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.HasWorkspaceService.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _commandService = Substitute.For<ICommandService>();
        _commandService.ExecuteAsync<IMoveDownloadCommand>(
                Arg.Any<Action<IMoveDownloadCommand>>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo => MoveAsync(callInfo.Arg<Action<IMoveDownloadCommand>>()));

        var projectService = Substitute.For<IProjectService>();

        // Every reason the service records is a localized string, and what it reads is the key.
        var localizerService = Substitute.For<ILocalizerService>();
        localizerService.GetString(Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(callInfo => callInfo.Arg<string>());

        _downloadService = new DownloadService(
            Substitute.For<ILogger<DownloadService>>(),
            _messengerService,
            localizerService,
            _commandService,
            _workspaceWrapper,
            projectService,
            _localFileSystem);
    }

    [Test]
    public void NothingDownloaded_ListsNothing()
    {
        _downloadService.Downloads.Should().BeEmpty();
    }

    [Test]
    public async Task ADownload_IsStagedUnderTemp_AndMovedToTheDownloadsFolder()
    {
        var ticket = await BeginAsync("report.pdf");

        ticket.Destination.Should().Be(new ResourceKey("downloads/report.pdf"));
        ticket.StagingPath.Should().StartWith(TempFolderPath);

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.FileName.Should().Be("report.pdf");
        download.Status.Should().Be(DownloadStatus.InProgress);

        // The transfer writes its bytes to the staging path the ticket named.
        _localFileSystem.SeedFile(ticket.StagingPath, "payload");

        await _downloadService.CompleteAsync(ticket.Id);

        var move = _moves.Should().ContainSingle().Subject;
        move.SourceResource.Root.Should().Be(ProjectConstants.TempFolder);
        move.DestResource.Should().Be(new ResourceKey("downloads/report.pdf"));

        var settled = _downloadService.Downloads.Should().ContainSingle().Subject;
        settled.Status.Should().Be(DownloadStatus.Succeeded);
        settled.Resource.Should().Be(new ResourceKey("downloads/report.pdf"));

        // The staged file became the download, so nothing is left under temp:.
        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
        _localFileSystem.Files.Should().ContainKey(ResolvePath(settled.Resource).Value);

        // Selecting in the Explorer would take the keyboard from whatever the user was doing.
        _commandService.DidNotReceive().Execute<ISelectResourceCommand>(
            Arg.Any<Action<ISelectResourceCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task ANameAlreadyOnDisk_IsUniquified()
    {
        _localFileSystem.SeedFile(Path.Combine(ProjectFolderPath, "downloads", "report.pdf"), "existing");

        var ticket = await BeginAsync("report.pdf");

        ticket.Destination.Should().Be(new ResourceKey("downloads/report (1).pdf"));

        // The row names the file the download is saved as, from the moment it starts.
        _downloadService.Downloads.Should().ContainSingle()
            .Which.FileName.Should().Be("report (1).pdf");

        _localFileSystem.SeedFile(ticket.StagingPath, "payload");
        await _downloadService.CompleteAsync(ticket.Id);

        var settled = _downloadService.Downloads.Should().ContainSingle().Subject;
        settled.FileName.Should().Be("report (1).pdf");
        settled.Resource.Should().Be(new ResourceKey("downloads/report (1).pdf"));
    }

    [Test]
    public async Task TwoDownloadsOfTheSameNameStartedTogether_ReserveDistinctDestinations()
    {
        // Neither has written a byte yet, so only the reservation the first one holds tells them apart.
        var firstTicket = await BeginAsync("report.pdf");
        var secondTicket = await BeginAsync("report.pdf");

        firstTicket.Destination.Should().Be(new ResourceKey("downloads/report.pdf"));
        secondTicket.Destination.Should().Be(new ResourceKey("downloads/report (1).pdf"));
        secondTicket.StagingPath.Should().NotBe(firstTicket.StagingPath);

        // Newest first, each under the name it will be saved as.
        _downloadService.Downloads.Select(download => download.FileName)
            .Should().Equal("report (1).pdf", "report.pdf");
    }

    [Test]
    public async Task ADeniedDestination_IsRecordedAsAFailure_AndNothingIsStaged()
    {
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<StorageItemInfo>.Fail("Denied by the resource policy")));

        var beginResult = await _downloadService.BeginAsync(
            "report.pdf",
            "https://example.com/report.pdf",
            Substitute.For<IDownloadTransfer>());

        beginResult.IsFailure.Should().BeTrue();

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("Downloads_Blocked");

        await _resourceFileSystem.DidNotReceive().CreateFolderAsync(Arg.Any<ResourceKey>());
    }

    [Test]
    public async Task AnInterruptedTransfer_RecordsTheReason_AndDeletesTheStagedFile()
    {
        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "partial");

        await _downloadService.FailAsync(ticket.Id, "The transfer did not complete.");

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("The transfer did not complete.");
        download.Resource.Should().Be(ResourceKey.Empty);

        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
        _moves.Should().BeEmpty();
    }

    [Test]
    public async Task AFailedMove_SettlesAsAFailure_AndDeletesTheStagedFile()
    {
        _commandService.ExecuteAsync<IMoveDownloadCommand>(
                Arg.Any<Action<IMoveDownloadCommand>>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Task.FromResult<Result>(Result.Fail("Destination already exists")));

        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "payload");

        await _downloadService.CompleteAsync(ticket.Id);

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("Downloads_ImportFailed");

        // A move that failed leaves the staged file behind, so it is deleted.
        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
    }

    [Test]
    public async Task ACompletedDownloadWhoseFileHasGone_LeavesTheList()
    {
        var succeededTicket = await CompleteDownloadAsync("report.pdf");

        var failedTicket = await BeginAsync("notes.txt");
        await _downloadService.FailAsync(failedTicket.Id, "The transfer did not complete.");

        _downloadService.Downloads.Should().HaveCount(2);

        // The file is deleted in the Explorer, and the registry rebuild that follows is what tells the
        // service its row now leads nowhere.
        await _localFileSystem.DeleteFileAsync(ResolvePath(succeededTicket.Destination).Value);
        RaiseRegistryUpdated();

        // The failed row names no file, so there is nothing for it to lose.
        var remaining = _downloadService.Downloads.Should().ContainSingle().Subject;
        remaining.Status.Should().Be(DownloadStatus.Failed);
    }

    [Test]
    public async Task ClearAll_RemovesFinishedDownloads_AndKeepsOneStillRunning()
    {
        var succeededTicket = await CompleteDownloadAsync("report.pdf");

        var failedTicket = await BeginAsync("notes.txt");
        await _downloadService.FailAsync(failedTicket.Id, "The transfer did not complete.");

        var runningTicket = await BeginAsync("data.csv");
        _localFileSystem.SeedFile(runningTicket.StagingPath, "payload");

        _downloadService.ClearAll();

        // The running download keeps its row, so its progress stays in view and it can still be stopped.
        var running = _downloadService.Downloads.Should().ContainSingle().Subject;
        running.Id.Should().Be(runningTicket.Id);
        running.Status.Should().Be(DownloadStatus.InProgress);

        // The file a cleared record named stays in the project.
        _localFileSystem.Files.Should().ContainKey(ResolvePath(succeededTicket.Destination).Value);

        await _downloadService.CompleteAsync(runningTicket.Id);

        _downloadService.Downloads.Should().ContainSingle()
            .Which.Status.Should().Be(DownloadStatus.Succeeded);
    }

    [Test]
    public async Task ClearAll_WithOnlyRunningDownloads_ChangesNothing()
    {
        await BeginAsync("report.pdf");
        _messengerService.ClearReceivedCalls();

        _downloadService.ClearAll();

        _downloadService.Downloads.Should().ContainSingle()
            .Which.Status.Should().Be(DownloadStatus.InProgress);
        _messengerService.DidNotReceive().Send(Arg.Any<DownloadsChangedMessage>());
    }

    [Test]
    public async Task TheOldestRecord_IsDroppedOnceTheListIsFull()
    {
        for (var index = 0; index < DownloadService.DownloadLimit + 5; index++)
        {
            await BeginAsync($"report{index}.pdf");
        }

        _downloadService.Downloads.Should().HaveCount(DownloadService.DownloadLimit);
        _downloadService.Downloads[0].FileName.Should().Be($"report{DownloadService.DownloadLimit + 4}.pdf");
    }

    [Test]
    public async Task UnloadingTheProject_ClearsTheList_AndAbandonsATransferStillRunning()
    {
        await CompleteDownloadAsync("report.pdf");

        var ticket = await BeginAsync("notes.txt");
        _localFileSystem.SeedFile(ticket.StagingPath, "payload");

        RaiseWorkspaceUnloaded();

        _downloadService.Downloads.Should().BeEmpty();

        // Every record described the project that ended, and the staged file goes with temp:.
        await _downloadService.CompleteAsync(ticket.Id);

        _moves.Should().ContainSingle()
            .Which.DestResource.Should().Be(new ResourceKey("downloads/report.pdf"));
    }

    [Test]
    public async Task Cancelling_StopsTheTransfer_AndDeletesTheStagedFile()
    {
        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "partial");

        await _downloadService.CancelAsync(ticket.Id);

        _transfers["report.pdf"].Received(1).Cancel();

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("Downloads_TransferCancelled");

        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);

        // The platform may report the stop afterwards, and by then the download has already settled.
        await _downloadService.CompleteAsync(ticket.Id);

        _moves.Should().BeEmpty();
        _downloadService.Downloads.Should().ContainSingle()
            .Which.Status.Should().Be(DownloadStatus.Failed);
    }

    [Test]
    public async Task Progress_IsRecordedAgainstTheDownload()
    {
        var ticket = await BeginAsync("report.pdf");

        _downloadService.ReportProgress(ticket.Id, bytesReceived: 512, totalBytes: 2048);

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.BytesReceived.Should().Be(512);
        download.TotalBytes.Should().Be(2048);
    }

    private async Task<DownloadTicket> BeginAsync(string fileName)
    {
        var beginResult = await _downloadService.BeginAsync(
            fileName,
            $"https://example.com/{fileName}",
            _transfers.GetOrAdd(fileName, _ => Substitute.For<IDownloadTransfer>()));

        beginResult.IsSuccess.Should().BeTrue(beginResult.DiagnosticReport);

        return beginResult.Value;
    }

    private async Task<DownloadTicket> CompleteDownloadAsync(string fileName)
    {
        var ticket = await BeginAsync(fileName);
        _localFileSystem.SeedFile(ticket.StagingPath, "payload");

        await _downloadService.CompleteAsync(ticket.Id);

        return ticket;
    }

    private void RaiseRegistryUpdated()
    {
        _registryUpdatedHandler.Should().NotBeNull();
        _registryUpdatedHandler!.Invoke(_downloadService, new ResourceRegistryUpdatedMessage());
    }

    private void RaiseWorkspaceUnloaded()
    {
        _workspaceUnloadedHandler.Should().NotBeNull();
        _workspaceUnloadedHandler!.Invoke(_downloadService, new WorkspaceUnloadedMessage());
    }

    // Mirrors what the move command does: the staged file is renamed to its destination.
    private async Task<Result> MoveAsync(Action<IMoveDownloadCommand> configure)
    {
        var command = Substitute.For<IMoveDownloadCommand>();
        configure(command);

        _moves.Add(new Move(command.SourceResource, command.DestResource));

        var moveResult = await _localFileSystem.MoveFileAsync(
            ResolvePath(command.SourceResource).Value,
            ResolvePath(command.DestResource).Value);
        moveResult.IsSuccess.Should().BeTrue(moveResult.DiagnosticReport);

        return Result.Ok();
    }

    private async Task<Result<DeleteResult>> DeleteAsync(ResourceKey resource)
    {
        await _localFileSystem.DeleteFileAsync(ResolvePath(resource).Value);

        return Result<DeleteResult>.Ok(new DeleteResult(SidecarOutcome.NotPresent));
    }

    private static Result<string> ResolvePath(ResourceKey resource)
    {
        var rootPath = resource.Root == ProjectConstants.TempFolder
            ? TempFolderPath
            : ProjectFolderPath;

        var relativePath = resource.Path.Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(rootPath, relativePath));
    }

    // The temp root lives under the project folder, so it is matched first.
    private static Result<ResourceKey> ResolveResource(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (fullPath.StartsWith(TempFolderPath, StringComparison.Ordinal))
        {
            var relativePath = Path.GetRelativePath(TempFolderPath, fullPath);

            return new ResourceKey($"{ProjectConstants.TempFolder}:{ToKeyPath(relativePath)}");
        }

        if (fullPath.StartsWith(ProjectFolderPath, StringComparison.Ordinal))
        {
            var relativePath = Path.GetRelativePath(ProjectFolderPath, fullPath);

            return new ResourceKey(ToKeyPath(relativePath));
        }

        return Result<ResourceKey>.Fail($"'{path}' is outside the project");
    }

    private Result<IResource> GetResource(ResourceKey resource)
    {
        var path = ResolvePath(resource).Value;
        if (!_localFileSystem.Files.ContainsKey(path))
        {
            return Result<IResource>.Fail($"'{resource}' does not exist");
        }

        return Result<IResource>.Ok(Substitute.For<IFileResource>());
    }

    private static string ToKeyPath(string relativePath)
    {
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }
}
