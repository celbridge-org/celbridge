using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// The save tick runs about sixty times a second against every open document and utility. These tests pin
/// what it reports and how it retries: an item waiting for its timer is reported as saving while one that
/// cannot be written is reported as failing, the failing set is sent only when it changes, and the
/// attempts back off.
/// </summary>
[TestFixture]
public class WorkspaceItemSaverTests
{
    private const double TickDelta = 0.016;

    // Longer than the one second wait a first failure schedules, so a tick of this length retries.
    private const double PastFirstRetryDelta = 1.5;

    private IMessengerService _messengerService = null!;
    private ICommandService _commandService = null!;
    private WorkspaceItemSaver _workspaceItemSaver = null!;

    private List<int> _pendingSaveCounts = null!;
    private List<IReadOnlyList<ResourceKey>> _failureReports = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();
        _commandService = Substitute.For<ICommandService>();

        _pendingSaveCounts = new List<int>();
        _failureReports = new List<IReadOnlyList<ResourceKey>>();

        // Capture the messages so a test can read what the tick reported.
        _messengerService
            .When(service => service.Send(Arg.Any<PendingSaveCountMessage>()))
            .Do(call => _pendingSaveCounts.Add(call.Arg<PendingSaveCountMessage>().Count));

        _messengerService
            .When(service => service.Send(Arg.Any<WorkspaceItemSaveFailuresChangedMessage>()))
            .Do(call => _failureReports.Add(call.Arg<WorkspaceItemSaveFailuresChangedMessage>().FailingResources));

        _workspaceItemSaver = new WorkspaceItemSaver(
            Substitute.For<ILogger<WorkspaceItemSaver>>(),
            _commandService,
            _messengerService);
    }

    [Test]
    public async Task SaveModifiedItems_ReportsAWrittenItemAsNoLongerPending()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = true };

        var result = await _workspaceItemSaver.SaveModifiedItemsAsync(new[] { item }, TickDelta);

        result.IsSuccess.Should().BeTrue();
        item.SaveCount.Should().Be(1);
        _pendingSaveCounts.Should().Equal(0);
        _failureReports.Should().BeEmpty();
    }

    [Test]
    public async Task SaveModifiedItems_CountsAnItemWaitingForItsTimerAsPending()
    {
        var item = new FakeWorkspaceItem { IsDueToSave = false };

        await _workspaceItemSaver.SaveModifiedItemsAsync(new[] { item }, TickDelta);

        item.SaveCount.Should().Be(0);
        _pendingSaveCounts.Should().Equal(1);
    }

    [Test]
    public async Task SaveModifiedItems_ReportsAFailingItemAsFailingRatherThanSaving()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };

        var result = await _workspaceItemSaver.SaveModifiedItemsAsync(new[] { item }, TickDelta);

        result.IsFailure.Should().BeTrue();
        _pendingSaveCounts.Should().Equal(
            new[] { 0 },
            "an item that cannot be written is not in the middle of being saved");

        _failureReports.Should().HaveCount(1);
        _failureReports[0].Should().ContainSingle()
            .Which.Should().Be(item.FileResource);
    }

    [Test]
    public async Task SaveModifiedItems_GoesOnReportingAHealthyItemAsSaving_WhileAnotherIsBlocked()
    {
        var healthyItem = new FakeWorkspaceItem
        {
            FileResource = new ResourceKey("healthy.md"),
            IsDueToSave = false
        };

        var blockedItem = new FakeWorkspaceItem
        {
            FileResource = new ResourceKey("blocked.md"),
            SaveSucceeds = false
        };

        var items = new[] { healthyItem, blockedItem };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        _pendingSaveCounts.Should().Equal(
            new[] { 1, 1 },
            "a document that cannot be written must not stop the workspace reporting that another one is saving");

        _failureReports.Should().HaveCount(1);
        _failureReports[0].Should().ContainSingle()
            .Which.Should().Be(blockedItem.FileResource);
    }

    [Test]
    public async Task SaveModifiedItems_ReportsTheFailingSetOnlyWhenItChanges()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        var firstResult = await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);
        var secondResult = await _workspaceItemSaver.SaveModifiedItemsAsync(items, PastFirstRetryDelta);

        firstResult.IsFailure.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue("the failure has already been reported");
        item.SaveCount.Should().Be(2, "a failed save is attempted again");
        _failureReports.Should().HaveCount(1);
    }

    [Test]
    public async Task SaveModifiedItems_WaitsBeforeRetryingAFailedSave()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        item.SaveCount.Should().Be(1, "the item is due on every tick, but the retry wait has not elapsed");
    }

    [Test]
    public async Task SaveModifiedItems_LengthensTheWaitAfterEachFailure()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, PastFirstRetryDelta);

        item.SaveCount.Should().Be(2);

        // The second failure doubles the wait to two seconds, so the same tick no longer reaches it.
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, PastFirstRetryDelta);

        item.SaveCount.Should().Be(2);

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, PastFirstRetryDelta);

        item.SaveCount.Should().Be(3);
    }

    [Test]
    public async Task SaveModifiedItems_ClearsTheFailingSet_WhenASaveSucceeds()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        item.SaveSucceeds = true;
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, PastFirstRetryDelta);

        item.HasUnsavedChanges = true;
        item.SaveSucceeds = false;
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        _failureReports.Select(report => report.Count).Should().Equal(1, 0, 1);
    }

    [Test]
    public async Task SaveModifiedItems_ClearsTheFailingSet_WhenTheItemIsClosed()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };

        await _workspaceItemSaver.SaveModifiedItemsAsync(new[] { item }, TickDelta);
        await _workspaceItemSaver.SaveModifiedItemsAsync(Array.Empty<IWorkspaceItem>(), TickDelta);
        await _workspaceItemSaver.SaveModifiedItemsAsync(new[] { item }, TickDelta);

        _failureReports.Select(report => report.Count).Should().Equal(1, 0, 1);
    }

    [Test]
    public async Task SaveModifiedItems_ClearsTheFailingSet_WhenAReloadClearsTheItem()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        // An external change reloads the buffer, which clears the unsaved changes without a save.
        item.HasUnsavedChanges = false;
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        item.HasUnsavedChanges = true;
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        item.SaveCount.Should().Be(2, "the retry wait went with the failure");
        _failureReports.Select(report => report.Count).Should().Equal(1, 0, 1);
    }

    [Test]
    public async Task SaveModifiedItems_DoesNotReportANonWritableItem()
    {
        var item = new FakeWorkspaceItem
        {
            SaveSucceeds = false,
            WritableState = WritableState.Locked
        };

        var result = await _workspaceItemSaver.SaveModifiedItemsAsync(new[] { item }, TickDelta);

        result.IsSuccess.Should().BeTrue();
        _failureReports.Should().BeEmpty("the editor already shows a read-only file as read-only");
        _pendingSaveCounts.Should().Equal(0);
    }

    private sealed class FakeWorkspaceItem : IWorkspaceItem
    {
        public ResourceKey FileResource { get; init; } = new ResourceKey("test.md");

        public bool HasUnsavedChanges { get; set; } = true;

        public WritableState WritableState { get; set; } = WritableState.Writable;

        // Whether the tick finds the item due to be written.
        public bool IsDueToSave { get; set; } = true;

        public bool SaveSucceeds { get; set; }

        public int SaveCount { get; private set; }

        public Result<bool> UpdateSaveTimer(double deltaTime)
        {
            if (!HasUnsavedChanges)
            {
                return Result<bool>.Fail("The item has no unsaved changes.");
            }

            return IsDueToSave;
        }

        public async Task<Result> SaveAsync()
        {
            await Task.CompletedTask;

            SaveCount++;

            if (!SaveSucceeds)
            {
                return Result.Fail("Simulated save failure");
            }

            HasUnsavedChanges = false;

            return Result.Ok();
        }
    }
}
