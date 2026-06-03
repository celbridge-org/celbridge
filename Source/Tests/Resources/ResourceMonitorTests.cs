using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;
using Uno.Extensions;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ResourceMonitor's domain half — path-to-ResourceKey mapping, policy
/// filtering, and messenger dispatch — driven through synthetic IFileSystemMonitor
/// events, with no disk and no real watcher.
/// </summary>
[TestFixture]
public class ResourceMonitorTests
{
    private const string ProjectBackingLocation = "C:/proj";

    private IMessengerService _messengerService = null!;
    private IDispatcher _dispatcher = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IResourcePolicy _policy = null!;
    private IResourceRootHandler _projectHandler = null!;
    private Dictionary<string, IResourceRootHandler> _rootHandlers = null!;
    private FakeFileSystemMonitorFactory _monitorFactory = null!;
    private ResourceMonitor _resourceMonitor = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();

        // Run enqueued actions synchronously so message sends happen inline.
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
        _projectHandler.Capabilities.Returns(new ResourceRootCapabilities(IsWritable: true, IsWatched: true));
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
        _policy.Evaluate(Arg.Any<ResourceKey>(), ResourceAction.List, Arg.Any<bool>())
            .Returns(Result.Ok());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(_policy);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
        // Keep the project-tree registry debounce dormant; tests assert on
        // messages, which are dispatched before the debounce is scheduled.
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        _monitorFactory = new FakeFileSystemMonitorFactory();

        _resourceMonitor = new ResourceMonitor(
            Substitute.For<ILogger<ResourceMonitor>>(),
            _dispatcher,
            _messengerService,
            _workspaceWrapper,
            _monitorFactory);
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
        tempHandler.Capabilities.Returns(new ResourceRootCapabilities(IsWritable: true, IsWatched: false));
        _rootHandlers["temp"] = tempHandler;

        _resourceMonitor.Initialize();

        _monitorFactory.Created.Should().ContainSingle();
        _monitorFactory.Created[0].BackingFolderPath.Should().Be(ProjectBackingLocation);
    }

    [Test]
    public void Created_SendsCreatedAndChangedMessages()
    {
        var monitor = InitializeAndGetProjectMonitor();
        var key = StubKey("foo.txt");

        monitor.RaiseCreated(PathFor("foo.txt"));

        _messengerService.Received(1).Send(Arg.Is<ResourceCreatedMessage>(m => m.Resource == key));
        _messengerService.Received(1).Send(Arg.Is<ResourceChangedMessage>(m => m.Resource == key));
    }

    [Test]
    public void Changed_SendsChangedMessage()
    {
        var monitor = InitializeAndGetProjectMonitor();
        var key = StubKey("foo.txt");

        monitor.RaiseChanged(PathFor("foo.txt"));

        _messengerService.Received(1).Send(Arg.Is<ResourceChangedMessage>(m => m.Resource == key));
    }

    [Test]
    public void Deleted_SendsDeletedMessage()
    {
        var monitor = InitializeAndGetProjectMonitor();
        var key = StubKey("foo.txt");

        monitor.RaiseDeleted(PathFor("foo.txt"));

        _messengerService.Received(1).Send(Arg.Is<ResourceDeletedMessage>(m => m.Resource == key));
    }

    [Test]
    public void Renamed_SendsRenamedAndChangedMessages()
    {
        var monitor = InitializeAndGetProjectMonitor();
        var oldKey = StubKey("old.txt");
        var newKey = StubKey("new.txt");

        monitor.RaiseRenamed(PathFor("old.txt"), PathFor("new.txt"));

        _messengerService.Received(1).Send(Arg.Is<ResourceRenamedMessage>(
            m => m.OldResource == oldKey && m.NewResource == newKey));
        _messengerService.Received(1).Send(Arg.Is<ResourceChangedMessage>(m => m.Resource == newKey));
    }

    [Test]
    public void Changed_PolicyDeniesList_DropsEvent()
    {
        var monitor = InitializeAndGetProjectMonitor();
        var key = StubKey("secret.txt");
        _policy.Evaluate(key, ResourceAction.List, Arg.Any<bool>())
            .Returns(Result.Fail("denied"));

        monitor.RaiseChanged(PathFor("secret.txt"));

        _messengerService.DidNotReceive().Send(Arg.Any<ResourceChangedMessage>());
    }

    [Test]
    public void Changed_PathCannotBeKeyed_DropsEvent()
    {
        var monitor = InitializeAndGetProjectMonitor();
        // No StubKey call, so GetResourceKey returns the default failure.

        monitor.RaiseChanged(PathFor("unmapped.txt"));

        _messengerService.DidNotReceive().Send(Arg.Any<ResourceChangedMessage>());
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

    private ResourceKey StubKey(string fileName)
    {
        var key = new ResourceKey(fileName);
        _projectHandler.GetResourceKey(PathFor(fileName)).Returns(Result<ResourceKey>.Ok(key));
        return key;
    }
}
