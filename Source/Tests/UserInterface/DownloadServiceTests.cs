using System.Collections.Concurrent;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Downloads;
using Celbridge.Explorer;
using Celbridge.Localization;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers the download service. Every download in the application goes through it. A download is staged under
/// temp: while the Download File dialog asks where the file goes. The answer reserves a free name in the chosen
/// folder, and the file moves there once the transfer has finished. Cancel, a failed transfer and a denied
/// destination each delete the staged file. A download fails when the Download File dialog cannot open. A row with a
/// missing file leaves the list. Other Location saves the file to the path chosen in the system save dialog, and
/// replaces any file there. A file saved outside the project moves through the local file system, and its row
/// records the path.
/// </summary>
[TestFixture]
public class DownloadServiceTests
{
    private record Move(ResourceKey SourceResource, ResourceKey DestResource, bool ReplaceExisting);

    private static readonly string ProjectFolderPath =
        Path.Combine(Path.GetTempPath(), "celbridge-download-tests", "project");

    private static readonly string TempFolderPath =
        Path.Combine(Path.GetTempPath(), "celbridge-download-tests", "data", "temp");

    private static readonly string OutsideFolderPath =
        Path.Combine(Path.GetTempPath(), "celbridge-download-tests", "elsewhere");

    private static readonly ResourceKey DownloadFolder = new("downloads");

    private IMessengerService _messengerService = null!;
    private ICommandService _commandService = null!;
    private IDialogService _dialogService = null!;
    private IBindableWorkspaceSettings _workspaceSettings = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private FakeResourceMonitor _resourceMonitor = null!;
    private RootHandlerRegistry _rootHandlers = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private ILocalizerService _localizerService = null!;
    private FakeFileSystem _localFileSystem = null!;
    private List<Move> _moves = null!;

    // The answer each Download File dialog gives at once. A dialog answers later when a test keeps the answer back.
    private Result<DownloadFileAnswer> _answer = null!;
    private Queue<TaskCompletionSource<Result<DownloadFileAnswer>>> _heldAnswers = null!;

    // Run part way through a move, for a test that needs to see the service mid-import.
    private Func<Task>? _duringMove;
    private ConcurrentDictionary<string, IDownloadTransfer> _transfers = null!;
    private MessageHandler<object, WorkspaceUnloadedMessage>? _workspaceUnloadedHandler;

    private DownloadService _downloadService = null!;

    [SetUp]
    public void Setup()
    {
        _moves = new List<Move>();
        _duringMove = null;
        _transfers = new ConcurrentDictionary<string, IDownloadTransfer>();
        _localFileSystem = new FakeFileSystem();
        _localFileSystem.SeedFolder(Path.Combine(ProjectFolderPath, "downloads"));

        _messengerService = Substitute.For<IMessengerService>();

        // The substitute records registrations rather than dispatching, so each handler the service
        // registers is captured for the tests to invoke.
        _workspaceUnloadedHandler = null;
        _messengerService
            .When(messenger => messenger.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, WorkspaceUnloadedMessage>>()))
            .Do(callInfo => _workspaceUnloadedHandler =
                callInfo.Arg<MessageHandler<object, WorkspaceUnloadedMessage>>());

        // The real disk lacks the in-memory items. A missing item passes the stored name check, so every key
        // resolves.
        _rootHandlers = TestRootHandlers.Create(ProjectFolderPath);
        _rootHandlers.RegisterRootHandler(new TempRootHandler(TempFolderPath));

        _resourceMonitor = new FakeResourceMonitor();

        // Each key reports the in-memory file system's item at the key's path. A folder seeded by a test then counts
        // as a folder.
        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        _resourceFileSystem.CreateFolderAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result.Ok()));
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(callInfo => GetInfoAsync(callInfo.Arg<ResourceKey>()));
        _resourceFileSystem.DeleteAsync(Arg.Any<ResourceKey>())
            .Returns(callInfo => DeleteAsync(callInfo.Arg<ResourceKey>()));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Monitor.Returns(_resourceMonitor);
        resourceService.RootHandlers.Returns(_rootHandlers);
        resourceService.FileSystem.Returns(_resourceFileSystem);

        // A project that remembers no download folder or option, until a download's answer records one.
        _workspaceSettings = Substitute.For<IBindableWorkspaceSettings>();
        _workspaceSettings.DownloadFolder = string.Empty;
        _workspaceSettings.DownloadToOtherLocation = false;

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        workspaceService.BindableWorkspaceSettings.Returns(_workspaceSettings);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.HasWorkspaceService.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _commandService = Substitute.For<ICommandService>();
        _commandService.ExecuteAsync<IMoveDownloadCommand>(
                Arg.Any<Action<IMoveDownloadCommand>>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo => MoveAsync(callInfo.Arg<Action<IMoveDownloadCommand>>()));

        // Every dialog saves to downloads/ at once, unless a test changes the answer or delays it.
        _answer = SaveTo(DownloadFolder);
        _heldAnswers = new Queue<TaskCompletionSource<Result<DownloadFileAnswer>>>();

        _dialogService = Substitute.For<IDialogService>();
        _dialogService.IsDialogOpen.Returns(false);
        _dialogService.ShowDownloadFileDialogAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ResourceKey>(),
                Arg.Any<DownloadFileChoice>())
            .Returns(callInfo => AnswerDialogAsync());

        // Every reason the service records is a localized string. These tests read the key.
        _localizerService = Substitute.For<ILocalizerService>();
        _localizerService.GetString(Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(callInfo => callInfo.Arg<string>());

        _downloadService = new DownloadService(
            Substitute.For<ILogger<DownloadService>>(),
            _messengerService,
            _localizerService,
            _commandService,
            _workspaceWrapper,
            _dialogService,
            _localFileSystem);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(ProjectFolderPath))
        {
            Directory.Delete(ProjectFolderPath, recursive: true);
        }
    }

    [Test]
    public void NothingDownloaded_ListsNothing()
    {
        _downloadService.Downloads.Should().BeEmpty();
    }

    [Test]
    public async Task ADownload_IsStagedUnderTheSitesName_BeforeTheDialogNamesItsDestination()
    {
        HoldTheNextAnswer();

        var ticket = await BeginAsync("report.pdf");

        ticket.StagingPath.Should().StartWith(Path.Combine(TempFolderPath, ProjectConstants.DownloadsFolder));

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.FileName.Should().Be("report.pdf");
        download.Resource.Should().Be(ResourceKey.Empty);
        download.Status.Should().Be(DownloadStatus.InProgress);

        // With no folder remembered, the dialog opens on the project folder, with In Project selected.
        await _dialogService.Received(1).ShowDownloadFileDialogAsync(
            "report.pdf",
            "https://example.com/report.pdf",
            ResourceKey.Empty,
            DownloadFileChoice.InProject);

        _moves.Should().BeEmpty();
        _workspaceSettings.DownloadFolder.Should().BeEmpty();
    }

    [Test]
    public async Task ADownload_IsMovedToTheFolderTheDialogNames()
    {
        var ticket = await BeginAsync("report.pdf");

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

        // The answer is remembered for the next download in this project.
        _workspaceSettings.DownloadFolder.Should().Be(DownloadFolder.ToString());

        // Selecting in the Explorer would take the keyboard from whatever the user was doing.
        _commandService.DidNotReceive().Execute<ISelectResourceCommand>(
            Arg.Any<Action<ISelectResourceCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task AFileThatArrivesBeforeTheAnswer_LandsOnceTheDialogAnswers()
    {
        var pendingAnswer = HoldTheNextAnswer();

        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "payload");

        await _downloadService.CompleteAsync(ticket.Id);

        // The file waits in temp: until the user says where it goes.
        _moves.Should().BeEmpty();
        _localFileSystem.Files.Should().ContainKey(ticket.StagingPath);
        _downloadService.Downloads.Should().ContainSingle()
            .Which.Status.Should().Be(DownloadStatus.InProgress);

        pendingAnswer.SetResult(SaveTo(DownloadFolder));

        _moves.Should().ContainSingle()
            .Which.DestResource.Should().Be(new ResourceKey("downloads/report.pdf"));

        var settled = _downloadService.Downloads.Should().ContainSingle().Subject;
        settled.Status.Should().Be(DownloadStatus.Succeeded);
        settled.Resource.Should().Be(new ResourceKey("downloads/report.pdf"));
        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
    }

    [Test]
    public async Task ANameAlreadyOnDisk_IsUniquified()
    {
        _localFileSystem.SeedFile(Path.Combine(ProjectFolderPath, "downloads", "report.pdf"), "existing");

        var ticket = await BeginAsync("report.pdf");

        // Once the dialog answers, the row shows the saved name.
        _downloadService.Downloads.Should().ContainSingle()
            .Which.FileName.Should().Be("report (1).pdf");

        _localFileSystem.SeedFile(ticket.StagingPath, "payload");
        await _downloadService.CompleteAsync(ticket.Id);

        var settled = _downloadService.Downloads.Should().ContainSingle().Subject;
        settled.FileName.Should().Be("report (1).pdf");
        settled.Resource.Should().Be(new ResourceKey("downloads/report (1).pdf"));
    }

    [Test]
    public async Task TheProjectFolder_CanBeTheDownloadFolder()
    {
        _answer = SaveTo(ResourceKey.Empty);

        var settled = await CompleteDownloadAsync("report.pdf");

        settled.Status.Should().Be(DownloadStatus.Succeeded);
        settled.Resource.Should().Be(new ResourceKey("report.pdf"));
        _localFileSystem.Files.Should().ContainKey(Path.Combine(ProjectFolderPath, "report.pdf"));
    }

    [Test]
    public async Task TheDialogOpensOnTheFolderLastChosen()
    {
        var incomingFolder = new ResourceKey("downloads/incoming");
        _localFileSystem.SeedFolder(Path.Combine(ProjectFolderPath, "downloads", "incoming"));
        _answer = SaveTo(incomingFolder);

        await CompleteDownloadAsync("report.pdf");

        _workspaceSettings.DownloadFolder.Should().Be(incomingFolder.ToString());

        await BeginAsync("notes.txt");

        await _dialogService.Received(1).ShowDownloadFileDialogAsync(
            "notes.txt",
            "https://example.com/notes.txt",
            incomingFolder,
            DownloadFileChoice.InProject);
    }

    [Test]
    public async Task TheDialogOpensOnTheProjectFolder_OnceTheFolderLastChosenHasGone()
    {
        _workspaceSettings.DownloadFolder = new ResourceKey("downloads/incoming").ToString();

        await BeginAsync("report.pdf");

        await _dialogService.Received(1).ShowDownloadFileDialogAsync(
            "report.pdf",
            "https://example.com/report.pdf",
            ResourceKey.Empty,
            DownloadFileChoice.InProject);
    }

    [Test]
    public async Task CancelInTheDialog_BeforeTheTransferFinishes_StopsTheTransfer_AndDeletesTheStagedFile()
    {
        var pendingAnswer = HoldTheNextAnswer();

        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "partial");

        pendingAnswer.SetResult(CancelTheDownload());

        _transfers["report.pdf"].Received(1).Cancel();

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Canceled);
        download.FailureReason.Should().BeEmpty();

        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
        _moves.Should().BeEmpty();
    }

    [Test]
    public async Task CancelInTheDialog_AfterTheTransferFinishes_DeletesTheStagedFile()
    {
        var pendingAnswer = HoldTheNextAnswer();

        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "payload");
        await _downloadService.CompleteAsync(ticket.Id);

        pendingAnswer.SetResult(CancelTheDownload());

        _downloadService.Downloads.Should().ContainSingle()
            .Which.Status.Should().Be(DownloadStatus.Canceled);

        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
        _moves.Should().BeEmpty();
    }

    [Test]
    public async Task ATransferThatFailsWhileTheDialogAsks_IsRecorded_AndTheAnswerSavesNothing()
    {
        var pendingAnswer = HoldTheNextAnswer();

        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "partial");

        await _downloadService.FailAsync(ticket.Id, "The download could not complete.");

        var failed = _downloadService.Downloads.Should().ContainSingle().Subject;
        failed.Status.Should().Be(DownloadStatus.Failed);
        failed.FailureReason.Should().Be("The download could not complete.");
        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);

        pendingAnswer.SetResult(SaveTo(DownloadFolder));

        _moves.Should().BeEmpty();
        var unchanged = _downloadService.Downloads.Should().ContainSingle().Subject;
        unchanged.Status.Should().Be(DownloadStatus.Failed);
        unchanged.FileName.Should().Be("report.pdf");

        // The answer reserved nothing, so the next download of report.pdf gets the plain name report.pdf.
        var nextTicket = await BeginAsync("report.pdf");

        GetRow(nextTicket.Id).FileName.Should().Be("report.pdf");
    }

    [Test]
    public async Task ADownloadThatStartsWhileAnotherDialogIsOpen_Fails_AndNothingIsStaged()
    {
        _dialogService.IsDialogOpen.Returns(true);

        var beginResult = await _downloadService.BeginAsync(
            "report.pdf",
            "https://example.com/report.pdf",
            Substitute.For<IDownloadTransfer>());

        beginResult.IsFailure.Should().BeTrue();

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.FileName.Should().Be("report.pdf");
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("Downloads_Failed");

        await _dialogService.DidNotReceive().ShowDownloadFileDialogAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ResourceKey>(),
            Arg.Any<DownloadFileChoice>());
        await _resourceFileSystem.DidNotReceive().CreateFolderAsync(Arg.Any<ResourceKey>());
    }

    [Test]
    public async Task ADownloadTheDialogServiceRefusesToAsk_Fails_AndTheStagedFileIsDeleted()
    {
        var pendingAnswer = HoldTheNextAnswer();

        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "partial");

        pendingAnswer.SetResult(Result<DownloadFileAnswer>.Fail("Another dialog is open"));

        _transfers["report.pdf"].Received(1).Cancel();

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("Downloads_Failed");
        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
    }

    [Test]
    public async Task ADeniedDestination_FailsTheDownload_AndDeletesTheStagedFile()
    {
        // WebKit tidies a file name that starts with a dot, but the service does not rely on the platform
        // doing so.
        var rule = Substitute.For<IPolicyRule>();
        rule.Source.Returns(PolicyRuleSource.SystemDeny);
        rule.Pattern.Returns(".git");
        rule.GatedActions.Returns(ResourceAction.Read | ResourceAction.Write);
        rule.Description.Returns("The Git metadata folder is reserved and cannot be addressed as a resource.");

        var deniedDestination = new ResourceKey("downloads/.git");
        var denial = new PolicyDenialError(deniedDestination, ResourceAction.Read, rule);
        _resourceFileSystem.GetInfoAsync(deniedDestination)
            .Returns(Task.FromResult<Result<StorageItemInfo>>(Result.Fail().WithException(denial)));

        var pendingAnswer = HoldTheNextAnswer();

        var ticket = await BeginAsync(".git");
        _localFileSystem.SeedFile(ticket.StagingPath, "partial");

        pendingAnswer.SetResult(SaveTo(DownloadFolder));

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("Downloads_Blocked");

        // The row names the reserved folder that matched.
        _localizerService.Received().GetString(
            "Downloads_Blocked",
            Arg.Is<object[]>(arguments => arguments.Length == 2 && Equals(arguments[1], ".git")));

        _transfers[".git"].Received(1).Cancel();
        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
        _moves.Should().BeEmpty();
    }

    [Test]
    public async Task ADestinationThatCannotBeChecked_FailsTheDownload_AsUnavailable()
    {
        _resourceFileSystem.GetInfoAsync(new ResourceKey("downloads/report.pdf"))
            .Returns(Task.FromResult(Result<StorageItemInfo>.Fail("The disk could not be read")));

        await BeginAsync("report.pdf");

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("Downloads_Failed");
    }

    [Test]
    public async Task AnAnswerNamingAFolderThatHasGone_FailsTheDownload_AsUnavailable()
    {
        _answer = SaveTo(new ResourceKey("downloads/incoming"));

        var ticket = await BeginAsync("report.pdf");

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("Downloads_Failed");

        _transfers["report.pdf"].Received(1).Cancel();

        await _downloadService.CompleteAsync(ticket.Id);

        _moves.Should().BeEmpty();
    }

    [Test]
    public async Task AnInterruptedTransfer_RecordsTheReason_AndDeletesTheStagedFile()
    {
        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "partial");

        await _downloadService.FailAsync(ticket.Id, "The download could not complete.");

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("The download could not complete.");
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
        download.FailureReason.Should().Be("Downloads_Failed");

        // A move that failed leaves the staged file behind, so it is deleted.
        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
    }

    [Test]
    public async Task RecordFailure_ListsAFailedRow_WithoutAsking()
    {
        _downloadService.RecordFailure(
            "report.pdf",
            "https://example.com/report.pdf",
            "Downloads_Failed");

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.FileName.Should().Be("report.pdf");
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("Downloads_Failed");
        download.Resource.Should().Be(ResourceKey.Empty);

        await _dialogService.DidNotReceive().ShowDownloadFileDialogAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<ResourceKey>(),
            Arg.Any<DownloadFileChoice>());
        _messengerService.Received(1).Send(Arg.Is<DownloadsChangedMessage>(message => message.HasArrival));
    }

    [Test]
    public async Task ACompletedDownloadWhoseFileIsMissing_LeavesTheList()
    {
        var succeeded = await CompleteDownloadAsync("report.pdf");

        var failedTicket = await BeginAsync("notes.txt");
        await _downloadService.FailAsync(failedTicket.Id, "The download could not complete.");

        _downloadService.Downloads.Should().HaveCount(2);

        // The file is deleted in the Explorer, and the change to the file is what tells the service its row now
        // leads nowhere.
        await _localFileSystem.DeleteFileAsync(ResolvePath(succeeded.Resource).Value);
        _resourceMonitor.DeliverChange(succeeded.Resource, ResourceItemState.Absent);

        // The failed row names no file, so there is nothing for it to lose.
        var remaining = _downloadService.Downloads.Should().ContainSingle().Subject;
        remaining.Status.Should().Be(DownloadStatus.Failed);
    }

    [Test]
    public async Task ClearAll_RemovesFinishedDownloads_AndKeepsOneStillRunning()
    {
        var succeeded = await CompleteDownloadAsync("report.pdf");

        var failedTicket = await BeginAsync("notes.txt");
        await _downloadService.FailAsync(failedTicket.Id, "The download could not complete.");

        var runningTicket = await BeginAsync("data.csv");
        _localFileSystem.SeedFile(runningTicket.StagingPath, "payload");

        _downloadService.ClearAll();

        // The running download keeps its row, so its progress stays in view and it can still be stopped.
        var running = _downloadService.Downloads.Should().ContainSingle().Subject;
        running.Id.Should().Be(runningTicket.Id);
        running.Status.Should().Be(DownloadStatus.InProgress);

        // The file a cleared record named stays in the project.
        _localFileSystem.Files.Should().ContainKey(ResolvePath(succeeded.Resource).Value);

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
    public async Task ADestinationBeingImported_IsStillReserved()
    {
        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "payload");

        // The file is not at its destination until the move lands, so a download begun during the move
        // must not be given the same path.
        DownloadTicket? concurrentTicket = null;
        _duringMove = async () => concurrentTicket = await BeginAsync("report.pdf");

        await _downloadService.CompleteAsync(ticket.Id);

        concurrentTicket.Should().NotBeNull();
        GetRow(concurrentTicket!.Id).FileName.Should().Be("report (1).pdf");
    }

    [Test]
    public async Task UnloadingTheProject_ClearsTheList_AndAbandonsATransferStillRunning()
    {
        await CompleteDownloadAsync("report.pdf");

        var ticket = await BeginAsync("notes.txt");
        _localFileSystem.SeedFile(ticket.StagingPath, "payload");

        RaiseWorkspaceUnloaded();

        _downloadService.Downloads.Should().BeEmpty();

        // Nothing is left to report the transfer's outcome, so it is stopped rather than left running.
        _transfers["notes.txt"].Received(1).Cancel();

        // Every record described the project that ended, and the staged file went with temp:.
        await _downloadService.CompleteAsync(ticket.Id);

        _moves.Should().ContainSingle()
            .Which.DestResource.Should().Be(new ResourceKey("downloads/report.pdf"));
    }

    [Test]
    public async Task Cancelling_StopsTheTransfer_AndDeletesTheStagedFile()
    {
        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "partial");
        _messengerService.ClearReceivedCalls();

        await _downloadService.CancelAsync(ticket.Id);

        _transfers["report.pdf"].Received(1).Cancel();

        // A cancellation is not a failure, and brings nothing for the badge to announce.
        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Canceled);
        download.FailureReason.Should().BeEmpty();
        _messengerService.DidNotReceive().Send(Arg.Is<DownloadsChangedMessage>(message => message.HasArrival));

        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);

        // The platform may report the stop afterwards, and by then the download has already settled.
        await _downloadService.CompleteAsync(ticket.Id);
        await _downloadService.ReportCanceledAsync(ticket.Id);

        _moves.Should().BeEmpty();
        _transfers["report.pdf"].Received(1).Cancel();
        _downloadService.Downloads.Should().ContainSingle()
            .Which.Status.Should().Be(DownloadStatus.Canceled);
    }

    [Test]
    public async Task AbandoningADownload_StopsTheTransfer_DeletesTheStagedFile_AndFailsWithTheReasonGiven()
    {
        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "partial");

        await _downloadService.AbandonAsync(ticket.Id, "the surface went away");

        _transfers["report.pdf"].Received(1).Cancel();

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Failed);
        download.FailureReason.Should().Be("the surface went away");

        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);

        // Stopping the transfer makes the platform report it, and by then the download has already settled
        // on the reason given here rather than on a cancellation the user asked for.
        await _downloadService.ReportCanceledAsync(ticket.Id);
        await _downloadService.CompleteAsync(ticket.Id);

        _moves.Should().BeEmpty();
        _downloadService.Downloads.Should().ContainSingle()
            .Which.Status.Should().Be(DownloadStatus.Failed);
    }

    [Test]
    public async Task AbandoningADownloadThatHasSettled_ChangesNothing()
    {
        var succeeded = await CompleteDownloadAsync("report.pdf");

        await _downloadService.AbandonAsync(succeeded.Id, "the surface went away");

        _transfers["report.pdf"].DidNotReceive().Cancel();
        _downloadService.Downloads.Should().ContainSingle()
            .Which.Status.Should().Be(DownloadStatus.Succeeded);
    }

    [Test]
    public async Task AStopThePlatformReports_IsACancellation_AndLeavesTheTransferAlone()
    {
        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "partial");

        await _downloadService.ReportCanceledAsync(ticket.Id);

        // The platform has already stopped the transfer, and is not called back from inside its report.
        _transfers["report.pdf"].DidNotReceive().Cancel();

        var download = _downloadService.Downloads.Should().ContainSingle().Subject;
        download.Status.Should().Be(DownloadStatus.Canceled);
        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
    }

    [Test]
    public async Task Removing_AFinishedDownload_LeavesTheOthers_AndKeepsItsFile()
    {
        var succeeded = await CompleteDownloadAsync("report.pdf");

        var failedTicket = await BeginAsync("notes.txt");
        await _downloadService.FailAsync(failedTicket.Id, "The download could not complete.");

        _downloadService.Remove(failedTicket.Id);

        _downloadService.Downloads.Should().ContainSingle()
            .Which.Id.Should().Be(succeeded.Id);

        _downloadService.Remove(succeeded.Id);

        // The record goes and the file it named stays in the project.
        _downloadService.Downloads.Should().BeEmpty();
        _localFileSystem.Files.Should().ContainKey(ResolvePath(succeeded.Resource).Value);
    }

    [Test]
    public async Task Removing_ADownloadStillRunning_ChangesNothing()
    {
        var ticket = await BeginAsync("report.pdf");
        _messengerService.ClearReceivedCalls();

        _downloadService.Remove(ticket.Id);

        _downloadService.Downloads.Should().ContainSingle()
            .Which.Status.Should().Be(DownloadStatus.InProgress);
        _messengerService.DidNotReceive().Send(Arg.Any<DownloadsChangedMessage>());
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

    [Test]
    public async Task AnOtherLocationOutsideTheProject_ReceivesTheFile_ReplacingTheFileThere()
    {
        _workspaceSettings.DownloadFolder = DownloadFolder.ToString();

        var filePath = Path.Combine(OutsideFolderPath, "quarterly.pdf");
        _localFileSystem.SeedFile(filePath, "existing");
        _answer = SaveToPath(filePath);

        var ticket = await BeginAsync("report.pdf");
        _localFileSystem.SeedFile(ticket.StagingPath, "payload");

        await _downloadService.CompleteAsync(ticket.Id);

        // The system save dialog asked before replacing the file, so the move overwrites it.
        _localFileSystem.Calls.Should().Contain($"MoveFileAsync('{ticket.StagingPath}', '{filePath}', overwrite=True)");
        (await _localFileSystem.ReadAllTextAsync(filePath)).Value.Should().Be("payload");
        _localFileSystem.Files.Should().NotContainKey(ticket.StagingPath);
        _moves.Should().BeEmpty();

        // The row shows the chosen name, and finds the file by its path.
        var settled = GetRow(ticket.Id);
        settled.Status.Should().Be(DownloadStatus.Succeeded);
        settled.FileName.Should().Be("quarterly.pdf");
        settled.FilePath.Should().Be(filePath);
        settled.Resource.Should().Be(ResourceKey.Empty);

        // The option is remembered, and the folder last chosen stays for the next save in the project.
        _workspaceSettings.DownloadToOtherLocation.Should().BeTrue();
        _workspaceSettings.DownloadFolder.Should().Be(DownloadFolder.ToString());
    }

    [Test]
    public async Task AnOtherLocationInsideTheProject_MovesTheFileIntoTheProject_ReplacingTheFileThere()
    {
        var destination = new ResourceKey("downloads/report.pdf");
        var filePath = ResolvePath(destination).Value;
        _localFileSystem.SeedFile(filePath, "existing");
        _answer = SaveToPath(filePath);

        var settled = await CompleteDownloadAsync("report.pdf");

        var move = _moves.Should().ContainSingle().Subject;
        move.DestResource.Should().Be(destination);
        move.ReplaceExisting.Should().BeTrue();

        settled.Status.Should().Be(DownloadStatus.Succeeded);
        settled.Resource.Should().Be(destination);
        settled.FilePath.Should().BeEmpty();
        (await _localFileSystem.ReadAllTextAsync(filePath)).Value.Should().Be("payload");
    }

    [Test]
    public async Task AProjectFolderAnswer_MovesWithoutReplacing_AndForgetsOtherLocation()
    {
        _workspaceSettings.DownloadToOtherLocation = true;

        var settled = await CompleteDownloadAsync("report.pdf");

        // An In Project save moves the file to a reserved free name.
        _moves.Should().ContainSingle()
            .Which.ReplaceExisting.Should().BeFalse();
        settled.Resource.Should().Be(new ResourceKey("downloads/report.pdf"));

        _workspaceSettings.DownloadToOtherLocation.Should().BeFalse();
        _workspaceSettings.DownloadFolder.Should().Be(DownloadFolder.ToString());
    }

    [Test]
    public async Task TheDialogSelectsOtherLocation_OnceOtherLocationWasLastUsed()
    {
        _answer = SaveToPath(Path.Combine(OutsideFolderPath, "report.pdf"));

        await CompleteDownloadAsync("report.pdf");

        await BeginAsync("notes.txt");

        await _dialogService.Received(1).ShowDownloadFileDialogAsync(
            "notes.txt",
            "https://example.com/notes.txt",
            ResourceKey.Empty,
            DownloadFileChoice.OtherLocation);
    }

    [Test]
    public async Task CheckFilesOutsideProject_RemovesTheRowOfADeletedFile_AndKeepsTheRowOfAFileThatExists()
    {
        var deletedPath = Path.Combine(OutsideFolderPath, "report.pdf");
        _answer = SaveToPath(deletedPath);
        await CompleteDownloadAsync("report.pdf");

        var keptPath = Path.Combine(OutsideFolderPath, "notes.txt");
        _answer = SaveToPath(keptPath);
        var kept = await CompleteDownloadAsync("notes.txt");

        await _localFileSystem.DeleteFileAsync(deletedPath);
        _messengerService.ClearReceivedCalls();

        await _downloadService.CheckFilesOutsideProjectAsync();

        _downloadService.Downloads.Should().ContainSingle()
            .Which.Id.Should().Be(kept.Id);
        _messengerService.Received(1).Send(Arg.Any<DownloadsChangedMessage>());
    }

    [Test]
    public async Task CancelingAnOtherLocationDownload_DeletesTheEmptyFileTheSaveDialogMade()
    {
        // The system save dialog on Windows makes an empty file at the path it returns.
        var filePath = Path.Combine(OutsideFolderPath, "report.pdf");
        _localFileSystem.SeedFile(filePath, string.Empty);
        _answer = SaveToPath(filePath);

        var ticket = await BeginAsync("report.pdf");
        await _downloadService.CancelAsync(ticket.Id);

        GetRow(ticket.Id).Status.Should().Be(DownloadStatus.Canceled);
        _localFileSystem.Files.Should().NotContainKey(filePath);
    }

    [Test]
    public async Task AFailedOtherLocationDownload_KeepsAFileWithContentAtThePath()
    {
        var filePath = Path.Combine(OutsideFolderPath, "report.pdf");
        _localFileSystem.SeedFile(filePath, "existing");
        _answer = SaveToPath(filePath);

        var ticket = await BeginAsync("report.pdf");
        await _downloadService.FailAsync(ticket.Id, "The download could not complete.");

        GetRow(ticket.Id).Status.Should().Be(DownloadStatus.Failed);
        (await _localFileSystem.ReadAllTextAsync(filePath)).Value.Should().Be("existing");
    }

    [Test]
    public async Task AFailedOtherLocationDownloadInsideTheProject_DeletesTheEmptyFileThroughTheProject()
    {
        var destination = new ResourceKey("downloads/report.pdf");
        var filePath = ResolvePath(destination).Value;
        _localFileSystem.SeedFile(filePath, string.Empty);
        _answer = SaveToPath(filePath);

        var ticket = await BeginAsync("report.pdf");
        await _downloadService.FailAsync(ticket.Id, "The download could not complete.");

        await _resourceFileSystem.Received(1).DeleteAsync(destination);
        _localFileSystem.Files.Should().NotContainKey(filePath);
    }

    [Test]
    public async Task AnOtherLocationAnswerAfterTheTransferFailed_DeletesTheEmptyFileTheSaveDialogMade()
    {
        var pendingAnswer = HoldTheNextAnswer();
        var ticket = await BeginAsync("report.pdf");
        await _downloadService.FailAsync(ticket.Id, "The download could not complete.");

        var filePath = Path.Combine(OutsideFolderPath, "report.pdf");
        _localFileSystem.SeedFile(filePath, string.Empty);
        pendingAnswer.SetResult(SaveToPath(filePath));

        _localFileSystem.Files.Should().NotContainKey(filePath);
    }

    private static Result<DownloadFileAnswer> SaveTo(ResourceKey folder)
    {
        var answer = new DownloadFileAnswer(DownloadFileChoice.InProject, folder);

        return Result<DownloadFileAnswer>.Ok(answer);
    }

    private static Result<DownloadFileAnswer> SaveToPath(string filePath)
    {
        var answer = new DownloadFileAnswer(DownloadFileChoice.OtherLocation, ResourceKey.Empty, filePath);

        return Result<DownloadFileAnswer>.Ok(answer);
    }

    private static Result<DownloadFileAnswer> CancelTheDownload()
    {
        var answer = new DownloadFileAnswer(DownloadFileChoice.Cancel, ResourceKey.Empty);

        return Result<DownloadFileAnswer>.Ok(answer);
    }

    // The next dialog waits until the test sets the returned answer. The rest of the service's answer handling then
    // runs inside SetResult, so the test reads the outcome straight after.
    private TaskCompletionSource<Result<DownloadFileAnswer>> HoldTheNextAnswer()
    {
        var pendingAnswer = new TaskCompletionSource<Result<DownloadFileAnswer>>();
        _heldAnswers.Enqueue(pendingAnswer);

        return pendingAnswer;
    }

    private Task<Result<DownloadFileAnswer>> AnswerDialogAsync()
    {
        if (_heldAnswers.TryDequeue(out var pendingAnswer))
        {
            return pendingAnswer.Task;
        }

        return Task.FromResult(_answer);
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

    // Runs a download to the end with the dialog's current answer, and returns the download's settled row.
    private async Task<DownloadEntry> CompleteDownloadAsync(string fileName)
    {
        var ticket = await BeginAsync(fileName);
        _localFileSystem.SeedFile(ticket.StagingPath, "payload");

        await _downloadService.CompleteAsync(ticket.Id);

        return GetRow(ticket.Id);
    }

    private DownloadEntry GetRow(long downloadId)
    {
        return _downloadService.Downloads.Should().ContainSingle(download => download.Id == downloadId).Subject;
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

        _moves.Add(new Move(command.SourceResource, command.DestResource, command.ReplaceExisting));

        if (_duringMove is not null)
        {
            await _duringMove();
        }

        var moveResult = await _localFileSystem.MoveFileAsync(
            ResolvePath(command.SourceResource).Value,
            ResolvePath(command.DestResource).Value,
            overwrite: command.ReplaceExisting);
        moveResult.IsSuccess.Should().BeTrue(moveResult.DiagnosticReport);

        return Result.Ok();
    }

    private async Task<Result<StorageItemInfo>> GetInfoAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<StorageItemInfo>.Fail($"Failed to resolve '{resource}'")
                .WithErrors(resolveResult);
        }

        return await _localFileSystem.GetInfoAsync(resolveResult.Value);
    }

    private async Task<Result<DeleteResult>> DeleteAsync(ResourceKey resource)
    {
        await _localFileSystem.DeleteFileAsync(ResolvePath(resource).Value);

        return Result<DeleteResult>.Ok(new DeleteResult(SidecarOutcome.NotPresent));
    }

    private Result<string> ResolvePath(ResourceKey resource)
    {
        return _rootHandlers.ResolveResourcePath(resource);
    }
}
