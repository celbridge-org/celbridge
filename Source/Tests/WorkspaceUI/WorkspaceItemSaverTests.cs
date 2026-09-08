using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Tests for the save pass that runs against every open document and utility.
/// </summary>
[TestFixture]
public class WorkspaceItemSaverTests
{
    private const double TickDelta = 0.016;

    // Longer than the one second wait a first failure schedules, so a tick of this length retries.
    private const double PastFirstRetryDelta = 1.5;

    // Short enough that a stalled write does not slow the suite.
    private const double FlushTimeout = 0.1;

    private IMessengerService _messengerService = null!;
    private ICommandService _commandService = null!;
    private WorkspaceItemSaver _workspaceItemSaver = null!;

    private List<int> _pendingSaveCounts = null!;
    private List<IReadOnlyList<ResourceKey>> _retryReports = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();
        _commandService = Substitute.For<ICommandService>();

        _pendingSaveCounts = new List<int>();
        _retryReports = new List<IReadOnlyList<ResourceKey>>();

        // Capture the messages so a test can read what the tick reported.
        _messengerService
            .When(service => service.Send(Arg.Any<PendingSaveCountMessage>()))
            .Do(call => _pendingSaveCounts.Add(call.Arg<PendingSaveCountMessage>().Count));

        _messengerService
            .When(service => service.Send(Arg.Any<WorkspaceItemSaveRetriesChangedMessage>()))
            .Do(call => _retryReports.Add(call.Arg<WorkspaceItemSaveRetriesChangedMessage>().RetryingResources));

        _workspaceItemSaver = new WorkspaceItemSaver(
            Substitute.For<ILogger<WorkspaceItemSaver>>(),
            _commandService,
            _messengerService,
            new SaveRetryTracker());
    }

    [Test]
    public async Task SaveModifiedItems_ReportsAWrittenItemAsNoLongerPending()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = true };

        var result = await _workspaceItemSaver.SaveModifiedItemsAsync(new[] { item }, TickDelta);

        result.IsSuccess.Should().BeTrue();
        item.SaveCount.Should().Be(1);
        _pendingSaveCounts.Should().Equal(0);
        _retryReports.Should().BeEmpty();
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
    public async Task SaveModifiedItems_ReportsARetryingItemAsRetryingRatherThanSaving()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };

        var result = await _workspaceItemSaver.SaveModifiedItemsAsync(new[] { item }, TickDelta);

        result.IsFailure.Should().BeTrue();
        _pendingSaveCounts.Should().Equal(
            new[] { 0 },
            "an item that cannot be written is not in the middle of being saved");

        _retryReports.Should().HaveCount(1);
        _retryReports[0].Should().ContainSingle()
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

        _retryReports.Should().HaveCount(1);
        _retryReports[0].Should().ContainSingle()
            .Which.Should().Be(blockedItem.FileResource);
    }

    [Test]
    public async Task SaveModifiedItems_ReportsTheRetryingSetOnlyWhenItChanges()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        var firstResult = await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);
        var secondResult = await _workspaceItemSaver.SaveModifiedItemsAsync(items, PastFirstRetryDelta);

        firstResult.IsFailure.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue("the failure has already been reported");
        item.SaveCount.Should().Be(2, "a failed save is attempted again");
        _retryReports.Should().HaveCount(1);
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
    public async Task SaveModifiedItems_ClearsTheRetryingSet_WhenASaveSucceeds()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        item.SaveSucceeds = true;
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, PastFirstRetryDelta);

        item.HasUnsavedChanges = true;
        item.SaveSucceeds = false;
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        _retryReports.Select(report => report.Count).Should().Equal(1, 0, 1);
    }

    [Test]
    public async Task SaveModifiedItems_ClearsTheRetryingSet_WhenTheItemIsClosed()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };

        await _workspaceItemSaver.SaveModifiedItemsAsync(new[] { item }, TickDelta);
        await _workspaceItemSaver.SaveModifiedItemsAsync(Array.Empty<IWorkspaceItem>(), TickDelta);
        await _workspaceItemSaver.SaveModifiedItemsAsync(new[] { item }, TickDelta);

        _retryReports.Select(report => report.Count).Should().Equal(1, 0, 1);
    }

    [Test]
    public async Task SaveModifiedItems_ClearsTheRetryingSet_WhenAReloadClearsTheItem()
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
        _retryReports.Select(report => report.Count).Should().Equal(1, 0, 1);
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
        _retryReports.Should().BeEmpty("the editor already shows a read-only file as read-only");
        _pendingSaveCounts.Should().Equal(0);
    }

    [Test]
    public async Task SaveModifiedItems_DoesNotCountABlockedItemAsPending_WhileItWaitsToRetry()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        // The failure scheduled a wait, and the item is no longer due, so it is neither saving nor retrying.
        item.IsDueToSave = false;
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        _pendingSaveCounts.Should().Equal(new[] { 0, 0 }, "a blocked item is reported as failing, not as saving");
    }

    [Test]
    public async Task SaveModifiedItems_BacksOffANonWritableItem()
    {
        var item = new FakeWorkspaceItem
        {
            SaveSucceeds = false,
            WritableState = WritableState.Locked
        };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        item.SaveCount.Should().Be(1, "a locked file waits rather than being written on every pass");

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, PastFirstRetryDelta);

        item.SaveCount.Should().Be(2);
    }

    [Test]
    public async Task SaveModifiedItems_WithdrawsTheFailure_WhenTheItemTurnsNonWritable()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        // The resource update the first failure asked for reports the file as read-only.
        item.WritableState = WritableState.Locked;
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, PastFirstRetryDelta);

        _retryReports.Select(report => report.Count).Should().Equal(new[] { 1, 0 },
            "a file that turns read-only ends up reported the same way as one that was read-only all along");
    }

    [Test]
    public async Task SaveModifiedItems_WithdrawsTheFailure_WhenTheItemTurnsNonWritableWhileWaitingToRetry()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        // The wait from the first failure has not elapsed, so no second attempt is made.
        item.WritableState = WritableState.Locked;
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        item.SaveCount.Should().Be(1);
        _retryReports.Select(report => report.Count).Should().Equal(
            new[] { 1, 0 },
            "the report follows the item's current writable state, not the state at its last attempt");
    }

    [Test]
    public async Task SaveModifiedItems_GoesOnRequestingAResourceUpdate_WhileASaveKeepsFailing()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, PastFirstRetryDelta);

        _commandService.ReceivedWithAnyArgs(2).Execute<IUpdateResourcesCommand>();
    }

    [Test]
    public async Task SaveModifiedItems_ReportsAFailure_WhenTheSaveThrows()
    {
        var item = new FakeWorkspaceItem { SaveThrows = true };
        var items = new[] { item };

        var result = await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);

        result.IsFailure.Should().BeTrue("an editor that throws is a failed save, not a stopped pass");
        _retryReports.Select(report => report.Count).Should().Equal(1);
    }

    [Test]
    public async Task SaveModifiedItems_RetriesOnTheItemsOwnCadence_WhenItIsNotDueOnEveryPass()
    {
        var item = new FakeWorkspaceItem { SaveSucceeds = false, SaveDelay = 1.0 };
        var items = new[] { item };

        await _workspaceItemSaver.SaveModifiedItemsAsync(items, TickDelta);
        item.SaveCount.Should().Be(1);

        // Half a second on, neither the item's timer nor the wait has expired.
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, 0.5);
        item.SaveCount.Should().Be(1);

        // A second later both have, so the item is written again.
        await _workspaceItemSaver.SaveModifiedItemsAsync(items, 1.0);
        item.SaveCount.Should().Be(2);
    }

    [Test]
    public async Task FlushModifiedItems_WritesEveryItemHoldingUnsavedChanges()
    {
        var document = new FakeWorkspaceItem { FileResource = new ResourceKey("project:notes.txt"), SaveSucceeds = true };
        var utility = new FakeWorkspaceItem { FileResource = new ResourceKey("project:panel.toml"), SaveSucceeds = true };

        await _workspaceItemSaver.FlushModifiedItemsAsync(new[] { document, utility }, FlushTimeout);

        document.SaveCount.Should().Be(1);
        utility.SaveCount.Should().Be(1, "a utility is written by the same flush as a document");
    }

    [Test]
    public async Task FlushModifiedItems_SkipsAnItemWithNothingUnwritten()
    {
        var item = new FakeWorkspaceItem { HasUnsavedChanges = false, SaveSucceeds = true };

        await _workspaceItemSaver.FlushModifiedItemsAsync(new[] { item }, FlushTimeout);

        item.SaveCount.Should().Be(0);
    }

    [Test]
    public async Task FlushModifiedItems_AbandonsAnItemThatDoesNotWriteInTime()
    {
        var stalledItem = new FakeWorkspaceItem { FileResource = new ResourceKey("project:stalled.txt"), SaveHangs = true };
        var nextItem = new FakeWorkspaceItem { FileResource = new ResourceKey("project:notes.txt"), SaveSucceeds = true };

        await _workspaceItemSaver.FlushModifiedItemsAsync(new[] { stalledItem, nextItem }, FlushTimeout);

        nextItem.SaveCount.Should().Be(1, "one editor that never answers must not hold the workspace open");
    }

    [Test]
    public async Task FlushModifiedItems_ContinuesAfterAnItemThrows()
    {
        var throwingItem = new FakeWorkspaceItem { FileResource = new ResourceKey("project:throws.txt"), SaveThrows = true };
        var nextItem = new FakeWorkspaceItem { FileResource = new ResourceKey("project:notes.txt"), SaveSucceeds = true };

        await _workspaceItemSaver.FlushModifiedItemsAsync(new[] { throwingItem, nextItem }, FlushTimeout);

        nextItem.SaveCount.Should().Be(1);
    }

    [Test]
    public async Task FlushModifiedItems_ContinuesAfterAFailedWrite()
    {
        var failingItem = new FakeWorkspaceItem { FileResource = new ResourceKey("project:locked.txt"), SaveSucceeds = false };
        var nextItem = new FakeWorkspaceItem { FileResource = new ResourceKey("project:notes.txt"), SaveSucceeds = true };

        await _workspaceItemSaver.FlushModifiedItemsAsync(new[] { failingItem, nextItem }, FlushTimeout);

        nextItem.SaveCount.Should().Be(1);
    }

    private sealed class FakeWorkspaceItem : IWorkspaceItem
    {
        private double _saveTimer;

        public ResourceKey FileResource { get; init; } = new ResourceKey("test.md");

        public bool HasUnsavedChanges { get; set; } = true;

        public WritableState WritableState { get; set; } = WritableState.Writable;

        // Whether the pass finds the item due to be written. Ignored once SaveDelay is set.
        public bool IsDueToSave { get; set; } = true;

        // The cadence the item comes due on, re-arming after each due pass as a document does. Left at
        // zero, IsDueToSave decides instead.
        public double SaveDelay { get; init; }

        public bool SaveSucceeds { get; set; }

        public bool SaveThrows { get; set; }

        // Whether the write never completes, as an editor that has stopped answering does.
        public bool SaveHangs { get; init; }

        public int SaveCount { get; private set; }

        public Result<bool> UpdateSaveTimer(double deltaTime)
        {
            if (!HasUnsavedChanges)
            {
                return Result<bool>.Fail("The item has no unsaved changes.");
            }

            if (SaveDelay <= 0)
            {
                return IsDueToSave;
            }

            _saveTimer -= deltaTime;
            if (_saveTimer > 0)
            {
                return false;
            }

            _saveTimer = SaveDelay;

            return true;
        }

        public async Task<Result> SaveAsync()
        {
            await Task.CompletedTask;

            SaveCount++;

            if (SaveHangs)
            {
                await Task.Delay(Timeout.Infinite);
            }

            if (SaveThrows)
            {
                throw new InvalidOperationException("Simulated save exception");
            }

            if (!SaveSucceeds)
            {
                return Result.Fail("Simulated save failure");
            }

            HasUnsavedChanges = false;

            return Result.Ok();
        }
    }
}
