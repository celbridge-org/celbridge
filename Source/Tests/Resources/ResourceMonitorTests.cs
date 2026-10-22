using Celbridge.Automation;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;
using Microsoft.Extensions.Time.Testing;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ResourceMonitor's watchers, and for how the monitor tells the resolver of each change to a name. Synthetic
/// IFileSystemMonitor events drive the tests, without the disk or a real watcher. ResourceSubscriptionTests covers the
/// subscriptions.
/// </summary>
[TestFixture]
public class ResourceMonitorTests
{
    private const string ProjectBackingLocation = "C:/proj";

    private IDispatcher _dispatcher = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IResourcePolicy _policy = null!;
    private IResourceRootHandler _projectHandler = null!;
    private Dictionary<string, IResourceRootHandler> _rootHandlers = null!;
    private FakeFileSystemMonitorFactory _monitorFactory = null!;
    private FakeTimeProvider _timeProvider = null!;
    private ResourceMonitor _resourceMonitor = null!;

    [SetUp]
    public void Setup()
    {
        // Run enqueued actions synchronously so deliveries happen inline.
        _dispatcher = Substitute.For<IDispatcher>();
        _dispatcher.TryEnqueue(Arg.Any<Action>())
            .Returns(callInfo =>
            {
                callInfo.Arg<Action>().Invoke();
                return true;
            });

        _projectHandler = Substitute.For<IResourceRootHandler>();
        _projectHandler.RootName.Returns(ResourceKey.DefaultRoot);
        _projectHandler.BackingLocation.Returns(ProjectBackingLocation);
        _projectHandler.Capabilities.Returns(new ResourceRootCapabilities(IsWritable: true, IsWatched: true, ListingPolicy.UntilChanged));
        // Default: any path fails to key; specific tests stub the paths they exercise.
        _projectHandler.GetResourceKey(Arg.Any<string>())
            .Returns(Result<ResourceKey>.Fail("not stubbed"));

        _rootHandlers = new Dictionary<string, IResourceRootHandler>
        {
            [ResourceKey.DefaultRoot] = _projectHandler,
        };

        var rootHandlerRegistry = Substitute.For<IRootHandlerRegistry>();
        rootHandlerRegistry.RootHandlers.Returns(_rootHandlers);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(rootHandlerRegistry);

        _policy = Substitute.For<IResourcePolicy>();
        _policy.Evaluate(Arg.Any<ResourceKey>(), ResourceAction.Read, Arg.Any<bool>())
            .Returns(Result.Ok());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(_policy);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _monitorFactory = new FakeFileSystemMonitorFactory();

        _timeProvider = new FakeTimeProvider();

        _resourceMonitor = new ResourceMonitor(
            Substitute.For<ILogger<ResourceMonitor>>(),
            _dispatcher,
            _workspaceWrapper,
            _monitorFactory,
            TestFileSystem.CreateLocal(),
            Substitute.For<IAutomationService>(),
            _timeProvider);
    }

    [Test]
    public void Initialize_StartsOneMonitorPerWatchedRoot()
    {
        var result = _resourceMonitor.Initialize();

        result.IsSuccess.Should().BeTrue();
        _monitorFactory.Created.Should().ContainSingle();
        _monitorFactory.Created[0].BackingFolderPath.Should().Be(ProjectBackingLocation);
        _monitorFactory.Created[0].Started.Should().BeTrue();
    }

    [Test]
    public void Initialize_SkipsRootsThatAreNotWatched()
    {
        var tempHandler = Substitute.For<IResourceRootHandler>();
        tempHandler.RootName.Returns("temp");
        tempHandler.BackingLocation.Returns("C:/temp");
        tempHandler.Capabilities.Returns(new ResourceRootCapabilities(IsWritable: true, IsWatched: false, ListingPolicy.UntilChanged));
        _rootHandlers["temp"] = tempHandler;

        _resourceMonitor.Initialize();

        _monitorFactory.Created.Should().ContainSingle();
        _monitorFactory.Created[0].BackingFolderPath.Should().Be(ProjectBackingLocation);
    }

    [Test]
    public void CreatedDeletedAndRenamed_AreRecordedWithTheResolver()
    {
        // A link may replace a folder that is deleted or renamed away, so the resolver checks the path again.
        // Each change may also alter a folder's listing.
        var monitor = InitializeAndGetProjectMonitor();

        monitor.RaiseCreated(PathFor("created"));
        monitor.RaiseDeleted(PathFor("deleted"));
        monitor.RaiseRenamed(PathFor("old"), PathFor("new"));

        _projectHandler.Received(1).RecordNameChanged(PathFor("created"));
        _projectHandler.Received(1).RecordNameChanged(PathFor("deleted"));
        _projectHandler.Received(1).RecordNameChanged(PathFor("old"));
        _projectHandler.Received(1).RecordNameChanged(PathFor("new"));
    }

    [Test]
    public void Changed_IsNotRecordedWithTheResolver()
    {
        // Only a change to a name alters a listing.
        var monitor = InitializeAndGetProjectMonitor();

        monitor.RaiseChanged(PathFor("changed"));

        _projectHandler.DidNotReceive().RecordNameChanged(Arg.Any<string>());
    }

    [Test]
    public void ChangesToIgnoredPaths_AreRecordedWithTheResolver()
    {
        // The listings contain every name in a folder, including the names the tree walk skips.
        var monitor = InitializeAndGetProjectMonitor();
        // No StubKey call, so every path fails to key and every event is ignored.

        monitor.RaiseCreated(PathFor("node_modules/created"));
        monitor.RaiseDeleted(PathFor("node_modules/deleted"));

        _projectHandler.Received(1).RecordNameChanged(PathFor("node_modules/created"));
        _projectHandler.Received(1).RecordNameChanged(PathFor("node_modules/deleted"));
    }

    [Test]
    public void MonitoringDesynchronized_InvalidatesThePathCache()
    {
        var monitor = InitializeAndGetProjectMonitor();

        // A folder verified before the lost events may since have been replaced by a reparse point, and
        // this is the only signal that the resolver missed it.
        monitor.RaiseDesynchronized();

        _projectHandler.Received(1).InvalidatePathCache();
    }

    [Test]
    public void Shutdown_DisposesMonitors()
    {
        var monitor = InitializeAndGetProjectMonitor();

        _resourceMonitor.Shutdown();

        monitor.Disposed.Should().BeTrue();
    }

    private FakeFileSystemMonitor InitializeAndGetProjectMonitor()
    {
        var result = _resourceMonitor.Initialize();
        result.IsSuccess.Should().BeTrue();
        _monitorFactory.Created.Should().ContainSingle();
        return _monitorFactory.Created[0];
    }

    private static string PathFor(string fileName)
    {
        return ProjectBackingLocation + "/" + fileName;
    }
}
