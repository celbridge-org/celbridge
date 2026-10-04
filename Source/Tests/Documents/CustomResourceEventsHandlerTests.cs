using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for the resource change relay a custom editor subscribes to. The debounce is driven by a delay hook,
/// so each test decides when the quiet period ends.
/// </summary>
[TestFixture]
public class CustomResourceEventsHandlerTests
{
    private IMessengerService _messengerService = null!;
    private CustomResourceEventsHandler _handler = null!;
    private List<ResourceChangeNotification> _notifications = null!;
    private List<TaskCompletionSource> _pendingDelays = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();
        _notifications = new List<ResourceChangeNotification>();
        _pendingDelays = new List<TaskCompletionSource>();

        _handler = new CustomResourceEventsHandler(
            _messengerService,
            Substitute.For<ILogger>(),
            notification =>
            {
                _notifications.Add(notification);
                return Task.CompletedTask;
            },
            _ =>
            {
                var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingDelays.Add(delay);
                return delay.Task;
            });
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
    }

    [Test]
    public async Task NothingIsReported_BeforeThePageSubscribes()
    {
        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("main.py")));

        await EndQuietPeriodsAsync();

        _notifications.Should().BeEmpty();
        _pendingDelays.Should().BeEmpty();
    }

    [Test]
    public async Task OneSave_IsReportedOnce_AsTheFirstEventOfItsBurst()
    {
        _handler.Subscribe();

        // An atomic save of a new file: the watcher reports it as created, then changed.
        _messengerService.Send(new ResourceCreatedMessage(new ResourceKey("src/main.py")));
        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("src/main.py")));

        await EndQuietPeriodsAsync();

        _notifications.Should().Equal(new ResourceChangeNotification(ResourceChangeKinds.Created, "project:src/main.py"));
    }

    [Test]
    public async Task OnlyFilesMatchingThePatterns_AreReported()
    {
        _handler.Subscribe(new[] { "*.py", "web/**/*.js" });

        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("deep/folder/main.py")));
        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("web/app/site.js")));
        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("other/site.js")));
        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("notes.md")));

        await EndQuietPeriodsAsync();

        _notifications.Select(n => n.Resource).Should().BeEquivalentTo(
            "project:deep/folder/main.py",
            "project:web/app/site.js");
    }

    [Test]
    public async Task ResourcesOutsideTheProjectTree_AreNeverReported()
    {
        _handler.Subscribe();

        _messengerService.Send(new ResourceChangedMessage(ResourceKey.Create("utils:logger.state._json")));
        _messengerService.Send(new ResourceChangedMessage(ResourceKey.Create("temp:scratch.py")));

        await EndQuietPeriodsAsync();

        _notifications.Should().BeEmpty();
    }

    [Test]
    public async Task AFileCreatedAndDeletedInOneQuietPeriod_IsNotReported()
    {
        _handler.Subscribe();

        _messengerService.Send(new ResourceCreatedMessage(new ResourceKey("main.py~")));
        _messengerService.Send(new ResourceDeletedMessage(new ResourceKey("main.py~")));

        await EndQuietPeriodsAsync();

        _notifications.Should().BeEmpty();
    }

    [Test]
    public async Task AFileDeletedAndRecreated_IsReportedAsChanged()
    {
        _handler.Subscribe();

        _messengerService.Send(new ResourceDeletedMessage(new ResourceKey("main.py")));
        _messengerService.Send(new ResourceCreatedMessage(new ResourceKey("main.py")));

        await EndQuietPeriodsAsync();

        _notifications.Should().Equal(new ResourceChangeNotification(ResourceChangeKinds.Changed, "project:main.py"));
    }

    [Test]
    public void ARenameBetweenWatchedFiles_IsReportedAtOnce_WithBothKeys()
    {
        _handler.Subscribe(new[] { "*.py" });

        _messengerService.Send(new ResourceRenamedMessage(new ResourceKey("a.py"), new ResourceKey("b.py")));

        _notifications.Should().Equal(new ResourceChangeNotification(ResourceChangeKinds.Renamed, "project:b.py", "project:a.py"));
    }

    [Test]
    public void ARenameIntoThePatterns_IsReportedAsCreated()
    {
        _handler.Subscribe(new[] { "*.py" });

        // An editor that saves by renaming a temporary file over the real one.
        _messengerService.Send(new ResourceRenamedMessage(new ResourceKey("main.py.tmp"), new ResourceKey("main.py")));

        _notifications.Should().Equal(new ResourceChangeNotification(ResourceChangeKinds.Created, "project:main.py"));
    }

    [Test]
    public async Task ARename_ReportsThePendingChangeFirst_AndSupersedesItsWait()
    {
        _handler.Subscribe();

        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("a.py")));
        _messengerService.Send(new ResourceRenamedMessage(new ResourceKey("a.py"), new ResourceKey("b.py")));

        await EndQuietPeriodsAsync();

        _notifications.Should().Equal(
            new ResourceChangeNotification(ResourceChangeKinds.Changed, "project:a.py"),
            new ResourceChangeNotification(ResourceChangeKinds.Renamed, "project:b.py", "project:a.py"));
    }

    [Test]
    public async Task Unsubscribing_DropsPendingChanges_AndStopsReporting()
    {
        _handler.Subscribe();
        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("main.py")));

        _handler.Unsubscribe();
        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("other.py")));

        await EndQuietPeriodsAsync();

        _notifications.Should().BeEmpty();
    }

    [Test]
    public async Task SubscribingAgain_ReplacesThePatterns()
    {
        _handler.Subscribe(new[] { "*.py" });
        _handler.Subscribe(new[] { "*.js" });

        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("main.py")));
        _messengerService.Send(new ResourceChangedMessage(new ResourceKey("main.js")));

        await EndQuietPeriodsAsync();

        _notifications.Should().Equal(new ResourceChangeNotification(ResourceChangeKinds.Changed, "project:main.js"));
    }

    [TestCase(null, ResourceChangeKinds.Changed, ResourceChangeKinds.Changed)]
    [TestCase(ResourceChangeKinds.Created, ResourceChangeKinds.Changed, ResourceChangeKinds.Created)]
    [TestCase(ResourceChangeKinds.Changed, ResourceChangeKinds.Created, ResourceChangeKinds.Changed)]
    [TestCase(ResourceChangeKinds.Changed, ResourceChangeKinds.Deleted, ResourceChangeKinds.Deleted)]
    [TestCase(ResourceChangeKinds.Deleted, ResourceChangeKinds.Created, ResourceChangeKinds.Changed)]
    [TestCase(ResourceChangeKinds.Created, ResourceChangeKinds.Deleted, null)]
    public void MergeKinds_FoldsABurstIntoWhatSettled(string? pending, string next, string? expected)
    {
        CustomResourceEventsHandler.MergeKinds(pending, next).Should().Be(expected);
    }

    // Ends every quiet period started so far, and any the reports start in turn, then lets the reports run.
    private async Task EndQuietPeriodsAsync()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            var delays = _pendingDelays.ToList();
            _pendingDelays.Clear();
            foreach (var delay in delays)
            {
                delay.TrySetResult();
            }

            await Task.Delay(20);
        }
    }
}
