using Celbridge.Messaging;
using Celbridge.Notifications;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Tests.Localization;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Every source the notification centre lists reaches it through the composer, which decides whether each
/// becomes a condition or an event and writes the line the user reads. The wording is checked against the
/// application's own strings, so a renamed or missing key fails here.
/// </summary>
[TestFixture]
public class NotificationComposerTests
{
    private IMessengerService _messengerService = null!;
    private NotificationCentre _centre = null!;

    private MessageHandler<object, ProjectLoadNotificationMessage>? _loadHandler;
    private MessageHandler<object, ResourceOperationFailedMessage>? _operationHandler;
    private MessageHandler<object, WorkspaceItemSaveDiscardedMessage>? _saveDiscardedHandler;
    private MessageHandler<object, EditorNotificationMessage>? _editorHandler;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();

        // Capture the handlers so a test can deliver a message without a live messenger.
        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, ProjectLoadNotificationMessage>>()))
            .Do(call => _loadHandler = call.Arg<MessageHandler<object, ProjectLoadNotificationMessage>>());

        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, ResourceOperationFailedMessage>>()))
            .Do(call => _operationHandler = call.Arg<MessageHandler<object, ResourceOperationFailedMessage>>());

        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, WorkspaceItemSaveDiscardedMessage>>()))
            .Do(call => _saveDiscardedHandler = call.Arg<MessageHandler<object, WorkspaceItemSaveDiscardedMessage>>());

        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, EditorNotificationMessage>>()))
            .Do(call => _editorHandler = call.Arg<MessageHandler<object, EditorNotificationMessage>>());

        _centre = new NotificationCentre(_messengerService);

        var composer = new NotificationComposer(_messengerService, new TestLocalizerService(), _centre);
        composer.Start();
    }

    [Test]
    public void ALoadWithFindings_IsAConditionPointingAtItsReport()
    {
        SendLoadNotification(ReportSeverity.Warning, issueCount: 3);

        var notification = _centre.Notifications.Should().ContainSingle().Subject;
        notification.Kind.Should().Be(NotificationKind.Condition);
        notification.Content.Severity.Should().Be(ReportSeverity.Warning);
        notification.Content.Message.Should().Be("The project loaded with 3 issues");

        var action = notification.Content.Action;
        action.Should().NotBeNull();
        action!.Resource.ToString().Should().Be("logs:reports/project-load.report");
        action.Label.Should().Be("View Report");
    }

    [Test]
    public void ALoadWithOneFinding_SaysSoInTheSingular()
    {
        SendLoadNotification(ReportSeverity.Error, issueCount: 1);

        _centre.Notifications.Should().ContainSingle()
            .Which.Content.Message.Should().Be("The project loaded with 1 issue");
    }

    [Test]
    public void AOneResourceFailure_NamesItAndItsReason_WithNoAction()
    {
        // One failure is fully expressed by the line, so the operation wrote no report for an action to open.
        SendOperationFailure(ResourceOperationType.Delete, "notes.txt");

        var notification = _centre.Notifications.Should().ContainSingle().Subject;
        notification.Kind.Should().Be(NotificationKind.Event);
        notification.Content.Severity.Should().Be(ReportSeverity.Error);
        notification.Content.Message.Should().Be("Could not delete 'notes.txt': the file is locked");
        notification.Content.Action.Should().BeNull();
    }

    [Test]
    public void AOneResourceFailure_KeepsOnlyTheFirstLineOfItsReason()
    {
        // A failure reason is an outer-first chain over several lines. The rest of it is what a report is for.
        var failedResources = new List<FailedResource>
        {
            new FailedResource(new ResourceKey("project:notes.txt"), "Could not delete the file\nThe process cannot access the file")
        };

        SendOperationFailure(ResourceOperationType.Delete, failedResources, ResourceKey.Empty);

        _centre.Notifications.Should().ContainSingle()
            .Which.Content.Message.Should().Be("Could not delete 'notes.txt': Could not delete the file");
    }

    [Test]
    public void SeveralResourceFailures_CountThemAndPointAtTheReport()
    {
        var failedResources = new List<FailedResource>
        {
            new FailedResource(new ResourceKey("project:notes.txt"), "the file is locked"),
            new FailedResource(new ResourceKey("project:data.json"), "permission denied")
        };

        SendOperationFailure(
            ResourceOperationType.Delete,
            failedResources,
            new ResourceKey("logs:reports/delete-resources.report"));

        var notification = _centre.Notifications.Should().ContainSingle().Subject;
        notification.Content.Message.Should().Be("Could not delete 2 resources");
        notification.Content.Action.Should().NotBeNull();
        notification.Content.Action!.Label.Should().Be("View Report");
    }

    [Test]
    public void AMoveThatOnlyLeftReferencesStale_IsAWarningNotAFailure()
    {
        // The move ran. What it could not finish was rewriting the references into what it moved.
        var message = new ResourceOperationFailedMessage(
            ResourceOperationType.Move,
            Array.Empty<FailedResource>())
        {
            SkippedReferencers = new List<SkippedReferencer>
            {
                new SkippedReferencer(new ResourceKey("project:a.json"), ReferencerSkipReason.ReadOnly, "read-only"),
                new SkippedReferencer(new ResourceKey("project:b.json"), ReferencerSkipReason.ReadOnly, "read-only")
            },
            ReportResource = new ResourceKey("logs:reports/move-resources.report")
        };

        _operationHandler!.Invoke(this, message);

        var notification = _centre.Notifications.Should().ContainSingle().Subject;
        notification.Content.Severity.Should().Be(ReportSeverity.Warning);
        notification.Content.Message.Should().Be("2 references still point at the old location");
        notification.Content.Action.Should().NotBeNull();
    }

    [Test]
    public void DiscardedEditsOnClose_NameTheFileAsAnError()
    {
        var message = new WorkspaceItemSaveDiscardedMessage(new ResourceKey("project:notes.txt"));

        _saveDiscardedHandler!.Invoke(this, message);

        var notification = _centre.Notifications.Should().ContainSingle().Subject;
        notification.Kind.Should().Be(NotificationKind.Event);
        notification.Content.Severity.Should().Be(ReportSeverity.Error);
        notification.Content.Message.Should().Be("Unsaved changes to 'notes.txt' were discarded");
        notification.Content.Action.Should().BeNull("there is no report to open for a discarded save");
    }

    [Test]
    public void AnEditorNotification_IsRecordedAsTheEditorWroteIt()
    {
        // The editor resolved its own text, so nothing composes it from a localization key here.
        var message = new EditorNotificationMessage(ReportSeverity.Warning, "9 of 40 tilesets failed to convert");

        _editorHandler!.Invoke(this, message);

        var notification = _centre.Notifications.Should().ContainSingle().Subject;
        notification.Kind.Should().Be(NotificationKind.Event);
        notification.Content.Severity.Should().Be(ReportSeverity.Warning);
        notification.Content.Message.Should().Be("9 of 40 tilesets failed to convert");
    }

    [Test]
    public void AnEditorNotification_IsCutToOneLine()
    {
        var message = new EditorNotificationMessage(ReportSeverity.Error, "Conversion failed\nsprites/hero.png: unsupported bit depth");

        _editorHandler!.Invoke(this, message);

        _centre.Notifications.Should().ContainSingle()
            .Which.Content.Message.Should().Be("Conversion failed");
    }

    [Test]
    public void AnEditorAction_KeepsItsOwnLabelAndPosition()
    {
        // The editor's action can open anything, so it keeps the label the editor gave it.
        var message = new EditorNotificationMessage(ReportSeverity.Error, "config.json has a syntax error")
        {
            Action = new OpenDocumentAction(new ResourceKey("project:config.json"), "Open config.json", 42, 7)
        };

        _editorHandler!.Invoke(this, message);

        var action = _centre.Notifications.Should().ContainSingle().Subject.Content.Action;
        action.Should().Be(new OpenDocumentAction(new ResourceKey("project:config.json"), "Open config.json", 42, 7));
    }

    [Test]
    public void AnEditorActionWithNoLabel_TakesTheHostsWording()
    {
        var message = new EditorNotificationMessage(ReportSeverity.Warning, "9 of 40 tilesets failed to convert")
        {
            Action = new OpenDocumentAction(new ResourceKey("logs:reports/acme-tiles-convert.report"))
        };

        _editorHandler!.Invoke(this, message);

        _centre.Notifications.Should().ContainSingle()
            .Which.Content.Action!.Label.Should().Be("View Report");
    }

    [Test]
    public void NotificationsFromEverySource_AreAllKept()
    {
        // Nothing competes for a slot, so every notification is kept. The load's condition stands above the
        // events.
        SendOperationFailure(ResourceOperationType.Delete, "notes.txt");
        SendLoadNotification(ReportSeverity.Warning, issueCount: 3);
        _editorHandler!.Invoke(this, new EditorNotificationMessage(ReportSeverity.Info, "Conversion finished"));

        var messages = _centre.Notifications
            .Select(notification => notification.Content.Message)
            .ToList();

        messages.Should().Equal(
            "The project loaded with 3 issues",
            "Conversion finished",
            "Could not delete 'notes.txt': the file is locked");
    }

    private void SendLoadNotification(ReportSeverity severity, int issueCount)
    {
        var summary = new ProjectLoadReportSummary(
            new ResourceKey("logs:reports/project-load.report"),
            severity,
            issueCount);

        _loadHandler!.Invoke(this, new ProjectLoadNotificationMessage(summary));
    }

    private void SendOperationFailure(ResourceOperationType operationType, params string[] failedItems)
    {
        var failedResources = failedItems
            .Select(item => new FailedResource(new ResourceKey($"project:{item}"), "the file is locked"))
            .ToList();

        SendOperationFailure(operationType, failedResources, ResourceKey.Empty);
    }

    private void SendOperationFailure(
        ResourceOperationType operationType,
        IReadOnlyList<FailedResource> failedResources,
        ResourceKey reportResource)
    {
        var message = new ResourceOperationFailedMessage(operationType, failedResources)
        {
            ReportResource = reportResource
        };

        _operationHandler!.Invoke(this, message);
    }
}
