using Celbridge.Commands;
using Celbridge.Downloads;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.Platform;
using Celbridge.Tests.Localization;
using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests for what the download list offers. Clear All removes only the downloads that have finished, so
/// it has nothing to do while every download is still running. A canceled download stays in the list but
/// is neither a failure nor counted by the badge. The summary counts downloads by outcome, even a single one.
/// Revealing a file saved outside the project opens the system file manager on the file.
/// </summary>
[TestFixture]
public class DownloadBadgeViewModelTests
{
    private IDownloadService _downloadService = null!;
    private ICommandService _commandService = null!;
    private IFileManagerLauncher _fileManagerLauncher = null!;
    private DownloadBadgeViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _downloadService = Substitute.For<IDownloadService>();
        _downloadService.Downloads.Returns(Array.Empty<DownloadEntry>());

        // The view model marshals onto the UI thread. Run inline so the assertions see the result.
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.TryEnqueue(Arg.Any<Action>()).Returns(call =>
        {
            call.Arg<Action>().Invoke();
            return true;
        });

        _commandService = Substitute.For<ICommandService>();

        _fileManagerLauncher = Substitute.For<IFileManagerLauncher>();
        _fileManagerLauncher.OpenFileManagerAsync(Arg.Any<string>())
            .Returns(Task.FromResult(Result.Ok()));

        _viewModel = new DownloadBadgeViewModel(
            Substitute.For<IMessengerService>(),
            dispatcher,
            new TestLocalizerService(),
            _downloadService,
            _commandService,
            _fileManagerLauncher,
            Substitute.For<ILogger<DownloadBadgeViewModel>>());
    }

    [Test]
    public void DownloadsThatAreAllStillRunning_LeaveNothingToClear()
    {
        ShowDownloads(CreateEntry(1, DownloadStatus.InProgress), CreateEntry(2, DownloadStatus.InProgress));

        _viewModel.CanClearAll.Should().BeFalse();
    }

    [TestCase(DownloadStatus.Succeeded)]
    [TestCase(DownloadStatus.Failed)]
    [TestCase(DownloadStatus.Canceled)]
    public void AFinishedDownload_CanBeCleared(DownloadStatus finishedStatus)
    {
        ShowDownloads(CreateEntry(1, DownloadStatus.InProgress), CreateEntry(2, finishedStatus));

        _viewModel.CanClearAll.Should().BeTrue();
    }

    [Test]
    public void ACanceledDownload_IsNotAFailure_AndIsLeftOutOfTheCount()
    {
        ShowDownloads(CreateEntry(1, DownloadStatus.Succeeded), CreateEntry(2, DownloadStatus.Canceled));

        _viewModel.HasFailure.Should().BeFalse();
        _viewModel.BadgeCount.Should().Be(1);
        _viewModel.Downloads.Should().HaveCount(2);
    }

    [Test]
    public void AFailedDownload_IsAFailure_AndIsCounted()
    {
        ShowDownloads(CreateEntry(1, DownloadStatus.Succeeded), CreateEntry(2, DownloadStatus.Failed));

        _viewModel.HasFailure.Should().BeTrue();
        _viewModel.BadgeCount.Should().Be(2);
    }

    [TestCase(DownloadStatus.InProgress, "1 download in progress.")]
    [TestCase(DownloadStatus.Succeeded, "1 download completed.")]
    [TestCase(DownloadStatus.Failed, "1 download failed.")]
    public void ALoneDownload_IsSummarisedByItsOutcome(DownloadStatus status, string expected)
    {
        ShowDownloads(CreateEntry(1, status));

        _viewModel.Summary.Should().Be(expected);
    }

    [Test]
    public void SeveralDownloads_AreSummarisedByACountOfEachOutcome()
    {
        ShowDownloads(
            CreateEntry(1, DownloadStatus.Succeeded),
            CreateEntry(2, DownloadStatus.Succeeded),
            CreateEntry(3, DownloadStatus.Failed));

        _viewModel.Summary.Should().Be("1 download failed. 2 downloads completed.");
    }

    [Test]
    public async Task RevealingAFileOutsideTheProject_OpensTheFileManagerOnItsPath()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "elsewhere", "file1.bin");
        var download = CreateEntry(1, DownloadStatus.Succeeded) with
        {
            FilePath = filePath
        };

        _viewModel.Reveal(download);

        await _fileManagerLauncher.Received(1).OpenFileManagerAsync(filePath);
        _commandService.DidNotReceive().Execute<ISelectResourceCommand>(
            Arg.Any<Action<ISelectResourceCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    private void ShowDownloads(params DownloadEntry[] downloads)
    {
        _downloadService.Downloads.Returns(downloads);

        _viewModel.OnLoaded();
    }

    private static DownloadEntry CreateEntry(long id, DownloadStatus status)
    {
        return new DownloadEntry(
            id,
            $"file{id}.bin",
            ResourceKey.Empty,
            "https://example.com/file.bin",
            status,
            FailureReason: string.Empty,
            DateTimeOffset.UtcNow);
    }
}
