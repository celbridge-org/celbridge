using Celbridge.Resources;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// The tracker follows the files of finished downloads through one key subscription. The subscription is
/// replaced only when the files change. A change or a resync that shows a file is missing makes the tracker
/// report the file.
/// </summary>
[TestFixture]
public class DownloadedFileTrackerTests
{
    private static readonly ResourceKey Report = new("downloads/report.pdf");
    private static readonly ResourceKey Notes = new("downloads/notes.txt");

    private FakeResourceMonitor _resourceMonitor = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private List<IReadOnlySet<ResourceKey>> _reports = null!;
    private DownloadedFileTracker _tracker = null!;

    [SetUp]
    public void Setup()
    {
        _resourceMonitor = new FakeResourceMonitor();

        // Nothing is on disk until a test puts a file there.
        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(
                new StorageItemInfo(StorageItemKind.NotFound, 0, DateTime.UtcNow, FileSystemAttributes.None))));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Monitor.Returns(_resourceMonitor);
        resourceService.FileSystem.Returns(_resourceFileSystem);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.HasWorkspaceService.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _reports = new List<IReadOnlySet<ResourceKey>>();
        _tracker = new DownloadedFileTracker(Substitute.For<ILogger>(), workspaceWrapper, _reports.Add);
    }

    [Test]
    public void Follow_SubscribesToTheFiles_AndAnEmptySetDisposesTheSubscription()
    {
        _tracker.Follow([Report, Notes]);

        _resourceMonitor.KeySubscriptions.Should().ContainSingle()
            .Which.Resources.Should().BeEquivalentTo([Report, Notes]);

        _tracker.Stop();

        _resourceMonitor.KeySubscriptions.Should().BeEmpty();
    }

    [Test]
    public void FollowingTheSameFiles_KeepsTheSubscription()
    {
        _tracker.Follow([Report, Notes]);
        var subscription = _resourceMonitor.KeySubscriptions.Single();

        _tracker.Follow([Notes, Report]);

        _resourceMonitor.KeySubscriptions.Should().ContainSingle()
            .Which.Should().BeSameAs(subscription);
    }

    [Test]
    public void AChange_ReportsAMissingFile_AndNotOneThatIsStillThere()
    {
        _tracker.Follow([Report, Notes]);

        _resourceMonitor.DeliverChange(Notes, ResourceItemState.File);
        _resourceMonitor.DeliverChange(Report, ResourceItemState.Absent);

        _reports.Should().ContainSingle()
            .Which.Should().BeEquivalentTo([Report]);
    }

    [Test]
    public void AResync_ReportsTheFollowedFilesMissingFromDisk()
    {
        _resourceFileSystem.GetInfoAsync(Notes)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(
                new StorageItemInfo(StorageItemKind.File, 7, DateTime.UtcNow, FileSystemAttributes.None))));
        _tracker.Follow([Report, Notes]);

        // The watcher lost events, so the tracker checks each followed file on disk.
        _resourceMonitor.DeliverResync(ResourceKey.DefaultRoot);

        _reports.Should().ContainSingle()
            .Which.Should().BeEquivalentTo([Report]);
    }
}
