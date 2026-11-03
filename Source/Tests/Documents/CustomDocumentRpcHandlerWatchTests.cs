using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for the editor bridge's watches on files. Each watch is a matcher subscription on the resource monitor. The
/// host sends each batch of a watch to the page under the watch's id.
/// </summary>
[TestFixture]
public class CustomDocumentRpcHandlerWatchTests
{
    private sealed record SentChanges(int WatchId, IReadOnlyList<WatchedFileChange> Changes);

    private IServiceProvider? _previousServiceProvider;
    private CustomDocumentViewModel _viewModel = null!;
    private FakeResourceMonitor _monitor = null!;
    private CustomDocumentRpcHandler _handler = null!;
    private List<SentChanges> _sent = null!;

    [SetUp]
    public void Setup()
    {
        // Loading content reads the file's info first. A file that is absent loads as an empty document.
        var resourceFileSystem = Substitute.For<IResourceFileSystem>();
        resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Result<StorageItemInfo>.Fail("not found"));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Monitor.Returns(new FakeResourceMonitor());
        resourceService.FileSystem.Returns(resourceFileSystem);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // The view model acquires the messenger through the global ServiceLocator, so the locator is restored
        // afterwards exactly as this fixture found it.
        var services = new ServiceCollection();
        services.AddSingleton<IMessengerService>(new MessengerService());
        services.AddSingleton(workspaceWrapper);
        _previousServiceProvider = ServiceLocator.ServiceProvider;
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _viewModel = new CustomDocumentViewModel(workspaceWrapper, TestFileSystem.CreateLocal(), []);

        _monitor = new FakeResourceMonitor();
        _sent = new List<SentChanges>();

        var metadata = new DocumentMetadata("C:/Projects/Acme/notes.md", "notes.md", "notes.md", "en");

        _handler = new CustomDocumentRpcHandler(
            _viewModel,
            Substitute.For<ILogger<CustomDocumentRpcHandlerWatchTests>>(),
            Substitute.For<IProjectService>(),
            Substitute.For<IReportWriter>(),
            _monitor,
            () => metadata,
            () => throw new NotSupportedException(),
            pip => { },
            (watchId, changes) =>
            {
                _sent.Add(new SentChanges(watchId, changes));
                return Task.CompletedTask;
            });
    }

    [TearDown]
    public void TearDown()
    {
        _viewModel.Cleanup();

        if (_previousServiceProvider is not null)
        {
            ServiceLocator.Initialize(_previousServiceProvider);
        }
        else
        {
            ServiceLocator.Reset();
        }
    }

    [Test]
    public async Task AWatch_SubscribesOnItsRootWithTheCompiledFilter()
    {
        await _handler.WatchFilesAsync(1, "temp:", "*.py", "history/");

        var subscription = _monitor.MatcherSubscriptions.Single();
        subscription.RootName.Should().Be("temp");
        subscription.Matcher.IsMatch("src/main.py", isFolder: false).Should().BeTrue();
        subscription.Matcher.IsMatch("notes.md", isFolder: false).Should().BeFalse();
        subscription.Matcher.IsMatch("history/main.py", isFolder: false).Should().BeFalse();
    }

    [Test]
    public async Task AWatch_WithNoRoot_FollowsTheProjectRoot()
    {
        await _handler.WatchFilesAsync(1);

        _monitor.MatcherSubscriptions.Single().RootName.Should().Be(ResourceKey.DefaultRoot);
    }

    [Test]
    public async Task AWatch_WithARefusedPattern_IsRejectedWithTheFiltersMessage()
    {
        var watchFiles = () => _handler.WatchFilesAsync(1, "project:", string.Empty, "!history/");

        await watchFiles.Should().ThrowAsync<ArgumentException>().WithMessage("*'!history/'*");
        _monitor.MatcherSubscriptions.Should().BeEmpty();
    }

    [TestCase("project")]
    [TestCase("project:src")]
    [TestCase("")]
    public async Task AWatch_OnSomethingOtherThanARootKey_IsRejected(string root)
    {
        var watchFiles = () => _handler.WatchFilesAsync(1, root);

        await watchFiles.Should().ThrowAsync<ArgumentException>().WithMessage("*Invalid root*");
        _monitor.MatcherSubscriptions.Should().BeEmpty();
    }

    [Test]
    public async Task AWatch_OnARootTheMonitorRefuses_IsRejectedWithTheMonitorsMessage()
    {
        _monitor.RefusedMatcherRoots.Add("logs");

        var watchFiles = () => _handler.WatchFilesAsync(1, "logs:");

        await watchFiles.Should().ThrowAsync<ArgumentException>().WithMessage("*not watched*");
        _monitor.MatcherSubscriptions.Should().BeEmpty();
    }

    [Test]
    public async Task AWatch_UnderAWatchIdInUse_IsRejected()
    {
        await _handler.WatchFilesAsync(1);

        var watchFiles = () => _handler.WatchFilesAsync(1, "temp:");

        await watchFiles.Should().ThrowAsync<ArgumentException>().WithMessage("*already in use*");
        _monitor.MatcherSubscriptions.Single().RootName.Should().Be(ResourceKey.DefaultRoot);
    }

    [Test]
    public async Task TwoWatches_EachReceiveOnlyTheirOwnChanges()
    {
        await _handler.WatchFilesAsync(1, "project:", "*.py");
        await _handler.WatchFilesAsync(2, "project:", "*.md");

        _monitor.DeliverChange(ResourceKey.Create("main.py"), ResourceItemState.File);
        _monitor.DeliverChange(ResourceKey.Create("notes.md"), ResourceItemState.File);

        _sent.Select(sent => sent.WatchId).Should().Equal(1, 2);
        _sent[0].Changes.Should().Equal(new WatchedFileChange("project:main.py", "file"));
        _sent[1].Changes.Should().Equal(new WatchedFileChange("project:notes.md", "file"));
    }

    [TestCase(ResourceItemState.File, "file")]
    [TestCase(ResourceItemState.Folder, "folder")]
    [TestCase(ResourceItemState.Absent, "absent")]
    public async Task AChange_IsSentWithTheCanonicalKeyAndTheState(ResourceItemState state, string expectedState)
    {
        await _handler.WatchFilesAsync(3, "temp:");

        _monitor.DeliverChange(ResourceKey.Create("temp:src/main.py"), state);

        _sent.Should().ContainSingle();
        _sent[0].WatchId.Should().Be(3);
        _sent[0].Changes.Should().Equal(new WatchedFileChange("temp:src/main.py", expectedState));
    }

    [Test]
    public async Task AResync_IsSentAsAChangeNamingTheRootAsAFolder()
    {
        await _handler.WatchFilesAsync(1, "temp:", "*.py");

        _monitor.DeliverResync("temp");

        _sent.Should().ContainSingle();
        _sent[0].Changes.Should().Equal(new WatchedFileChange("temp:", "folder"));
    }

    [Test]
    public async Task EndingAWatch_DisposesOnlyThatWatchsSubscription()
    {
        await _handler.WatchFilesAsync(1, "project:", "*.py");
        await _handler.WatchFilesAsync(2, "project:", "*.py");

        await _handler.UnwatchFilesAsync(1);
        _monitor.DeliverChange(ResourceKey.Create("main.py"), ResourceItemState.File);

        _monitor.MatcherSubscriptions.Should().ContainSingle();
        _sent.Select(sent => sent.WatchId).Should().Equal(2);
    }

    [Test]
    public async Task EndingAWatchIdWithNoWatch_Succeeds()
    {
        await _handler.WatchFilesAsync(1);

        await _handler.UnwatchFilesAsync(7);
        await _handler.UnwatchFilesAsync(1);
        await _handler.UnwatchFilesAsync(1);

        _monitor.MatcherSubscriptions.Should().BeEmpty();
    }

    [Test]
    public async Task Initialize_EndsEveryWatch()
    {
        await _handler.WatchFilesAsync(1);
        await _handler.WatchFilesAsync(2, "temp:");

        await _handler.InitializeAsync(DocumentRpcMethods.CurrentProtocolVersion);

        _monitor.MatcherSubscriptions.Should().BeEmpty();
    }

    [Test]
    public async Task EndAllWatches_EndsEveryWatch()
    {
        await _handler.WatchFilesAsync(1);
        await _handler.WatchFilesAsync(2, "temp:");

        _handler.EndAllWatches();

        _monitor.MatcherSubscriptions.Should().BeEmpty();
    }
}
