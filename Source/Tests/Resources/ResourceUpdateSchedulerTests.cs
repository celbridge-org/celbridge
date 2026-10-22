using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.Time.Testing;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ResourceUpdateScheduler: which changes schedule a registry update, the quiet interval, and dropping an
/// update that a registry update already covered. The monitor is a substitute that captures the subscription.
/// </summary>
[TestFixture]
public class ResourceUpdateSchedulerTests
{
    private static readonly TimeSpan UpdateDebounceInterval = TimeSpan.FromMilliseconds(250);

    private IMessengerService _messengerService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IResourceService _resourceService = null!;
    private IResourceMonitor _monitor = null!;
    private IMatcherSubscription _subscription = null!;
    private IPathMatcher _searchScope = null!;
    private FakeTimeProvider _timeProvider = null!;
    private ResourceUpdateScheduler _scheduler = null!;
    private ResourceChangeHandlers? _handlers;
    private MessageHandler<object, ResourceRegistryUpdateStartingMessage>? _updateStartingHandler;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();

        // The substitute records registrations and dispatches nothing, so capture the scheduler's update-starting
        // handler for RaiseRegistryUpdateStarting to invoke.
        _updateStartingHandler = null;
        _messengerService
            .When(messenger => messenger.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, ResourceRegistryUpdateStartingMessage>>()))
            .Do(callInfo => _updateStartingHandler =
                callInfo.Arg<MessageHandler<object, ResourceRegistryUpdateStartingMessage>>());

        // Run enqueued actions inline, so an update runs while the time is advanced.
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.TryEnqueue(Arg.Any<Action>())
            .Returns(callInfo =>
            {
                callInfo.Arg<Action>().Invoke();
                return true;
            });

        _resourceService = Substitute.For<IResourceService>();
        _resourceService.UpdateResourcesAsync().Returns(Result.Ok());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(_resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);

        _searchScope = Substitute.For<IPathMatcher>();
        _subscription = Substitute.For<IMatcherSubscription>();

        _handlers = null;
        _monitor = Substitute.For<IResourceMonitor>();
        _monitor.SubscribeToMatcher(Arg.Any<string>(), Arg.Any<IPathMatcher>(), Arg.Any<ResourceChangeHandlers>())
            .Returns(callInfo =>
            {
                _handlers = callInfo.Arg<ResourceChangeHandlers>();
                return Result<IMatcherSubscription>.Ok(_subscription);
            });

        _timeProvider = new FakeTimeProvider();

        _scheduler = new ResourceUpdateScheduler(
            Substitute.For<ILogger<ResourceUpdateScheduler>>(),
            dispatcher,
            _messengerService,
            _workspaceWrapper,
            _timeProvider);
    }

    [TearDown]
    public void TearDown()
    {
        _scheduler.Dispose();
    }

    [Test]
    public void Start_SubscribesToTheProjectWithTheSearchScope()
    {
        _scheduler.Start(_monitor, _searchScope).IsSuccess.Should().BeTrue();

        _monitor.Received(1).SubscribeToMatcher(ResourceKey.DefaultRoot, _searchScope, Arg.Any<ResourceChangeHandlers>());
    }

    [Test]
    public async Task AChangeSchedulesAnUpdateAfterTheQuietInterval()
    {
        Start();

        DeliverChange("notes.txt");
        _timeProvider.Advance(UpdateDebounceInterval - TimeSpan.FromMilliseconds(1));
        await _resourceService.DidNotReceive().UpdateResourcesAsync();

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await _resourceService.Received(1).UpdateResourcesAsync();
    }

    [Test]
    public async Task ChangesWithinTheQuietIntervalShareOneUpdate()
    {
        Start();

        DeliverChange("first.txt");
        _timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        DeliverChange("second.txt");
        _timeProvider.Advance(UpdateDebounceInterval);

        await _resourceService.Received(1).UpdateResourcesAsync();
    }

    [Test]
    public async Task AResyncSchedulesAnUpdate()
    {
        // The lost events may have changed anything in the project.
        Start();

        _handlers!.OnResync(new ResourceResync(ResourceKey.DefaultRoot));
        _timeProvider.Advance(UpdateDebounceInterval);

        await _resourceService.Received(1).UpdateResourcesAsync();
    }

    [Test]
    public async Task ScheduledUpdate_IsDroppedWhenAnUpdateAlreadyReadTheFileSystem()
    {
        Start();

        // The shape of a mutating command: the change is reported, then the command's own registry update reads the
        // file system before the quiet interval elapses.
        _scheduler.ScheduleUpdate();
        RaiseRegistryUpdateStarting();
        _timeProvider.Advance(UpdateDebounceInterval);

        await _resourceService.DidNotReceive().UpdateResourcesAsync();
    }

    [Test]
    public async Task ScheduledUpdate_RunsWhenTheRequestFollowsTheLastRead()
    {
        Start();

        // A change that lands after the read began may sit in a folder the walk has already passed, so it has to be
        // rescanned.
        RaiseRegistryUpdateStarting();
        _scheduler.ScheduleUpdate();
        _timeProvider.Advance(UpdateDebounceInterval);

        await _resourceService.Received(1).UpdateResourcesAsync();
    }

    [Test]
    public async Task NothingIsScheduledWhileTheWorkspaceIsNotLoaded()
    {
        Start();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        DeliverChange("notes.txt");
        _timeProvider.Advance(UpdateDebounceInterval);

        await _resourceService.DidNotReceive().UpdateResourcesAsync();
    }

    [Test]
    public void Dispose_DisposesTheSubscription()
    {
        Start();

        _scheduler.Dispose();

        _subscription.Received(1).Dispose();
    }

    private void Start()
    {
        _scheduler.Start(_monitor, _searchScope).IsSuccess.Should().BeTrue();
        _handlers.Should().NotBeNull();
    }

    private void DeliverChange(string path)
    {
        var change = new ResourceChange(ResourceKey.Create(path), ResourceItemState.File);
        _handlers!.OnChanges(new ResourceChangeBatch(ResourceKey.DefaultRoot, new[] { change }));
    }

    private void RaiseRegistryUpdateStarting()
    {
        _updateStartingHandler.Should().NotBeNull();
        _updateStartingHandler!.Invoke(_scheduler, new ResourceRegistryUpdateStartingMessage());
    }
}
