using Celbridge.Commands;
using Celbridge.Downloads;
using Celbridge.Messaging;
using Celbridge.Tests.Localization;
using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests for what the download list offers. Clear All removes only the downloads that have finished, so
/// it has nothing to do while every download is still running.
/// </summary>
[TestFixture]
public class DownloadBadgeViewModelTests
{
    private IDownloadService _downloadService = null!;
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

        _viewModel = new DownloadBadgeViewModel(
            Substitute.For<IMessengerService>(),
            dispatcher,
            new TestLocalizerService(),
            _downloadService,
            Substitute.For<ICommandService>());
    }

    [Test]
    public void DownloadsThatAreAllStillRunning_LeaveNothingToClear()
    {
        ShowDownloads(CreateEntry(1, DownloadStatus.InProgress), CreateEntry(2, DownloadStatus.InProgress));

        _viewModel.CanClearAll.Should().BeFalse();
    }

    [TestCase(DownloadStatus.Succeeded)]
    [TestCase(DownloadStatus.Failed)]
    public void AFinishedDownload_CanBeCleared(DownloadStatus finishedStatus)
    {
        ShowDownloads(CreateEntry(1, DownloadStatus.InProgress), CreateEntry(2, finishedStatus));

        _viewModel.CanClearAll.Should().BeTrue();
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
