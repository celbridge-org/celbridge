using Celbridge.Messaging;
using Celbridge.Notifications;
using Celbridge.Reports;
using Celbridge.UserInterface.Services;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// The notification badge lists what this service holds, so what it holds is what the user can still reach.
/// These tests pin the rules that keep that honest: nothing is displaced by a later arrival, conditions stand
/// above events and cannot be dismissed, and the list stays bounded however much a source repeats itself.
/// </summary>
[TestFixture]
public class NotificationCentreTests
{
    private IMessengerService _messengerService = null!;
    private NotificationCentre _centre = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();
        _centre = new NotificationCentre(_messengerService);
    }

    [Test]
    public void NothingRecorded_ListsNothing()
    {
        _centre.Notifications.Should().BeEmpty();
    }

    [Test]
    public void AnEvent_IsListedAndAnnouncedAsAnArrival()
    {
        _centre.AddEvent(CreateContent("Could not delete 'notes.txt'"));

        var notification = _centre.Notifications.Should().ContainSingle().Subject;
        notification.Kind.Should().Be(NotificationKind.Event);
        notification.OccurrenceCount.Should().Be(1);

        _messengerService.Received(1).Send(
            Arg.Is<NotificationsChangedMessage>(message => message.HasArrival));
    }

    [Test]
    public void ALaterEvent_DisplacesNothing()
    {
        // Every notification is kept, whatever its severity.
        _centre.AddEvent(CreateContent("Could not delete 'notes.txt'", ReportSeverity.Error));
        _centre.AddEvent(CreateContent("Conversion finished", ReportSeverity.Info));

        _centre.Notifications.Should().HaveCount(2);
    }

    [Test]
    public void Conditions_AreListedAboveEvents_AndEachNewestFirst()
    {
        _centre.AddEvent(CreateContent("first event"));
        _centre.RecordCondition("project-load", CreateContent("The project loaded with 3 issues"));
        _centre.AddEvent(CreateContent("second event"));

        var messages = _centre.Notifications
            .Select(notification => notification.Content.Message)
            .ToList();

        messages.Should().Equal("The project loaded with 3 issues", "second event", "first event");
    }

    [Test]
    public void AnIdenticalConsecutiveEvent_IsCountedOnTheEntryBeforeIt()
    {
        // A looping editor repeating one line takes one row.
        _centre.AddEvent(CreateContent("Conversion failed"));
        var firstId = _centre.Notifications[0].Id;

        _messengerService.ClearReceivedCalls();

        _centre.AddEvent(CreateContent("Conversion failed"));
        _centre.AddEvent(CreateContent("Conversion failed"));

        var notification = _centre.Notifications.Should().ContainSingle().Subject;
        notification.OccurrenceCount.Should().Be(3);
        notification.Id.Should().Be(firstId, "a row that is open in the list must still dismiss what it shows");

        // Each repeat still happened, so each is still an arrival.
        _messengerService.Received(2).Send(
            Arg.Is<NotificationsChangedMessage>(message => message.HasArrival));
    }

    [Test]
    public void AnIdenticalEventWithAnotherInBetween_IsListedAgain()
    {
        _centre.AddEvent(CreateContent("Conversion failed"));
        _centre.AddEvent(CreateContent("Could not delete 'notes.txt'"));
        _centre.AddEvent(CreateContent("Conversion failed"));

        _centre.Notifications.Should().HaveCount(3);
    }

    [Test]
    public void AnEventWithTheSameLineButAnotherAction_IsNotCounted()
    {
        // Two failed deletes read alike but each wrote its own report. Counting the second on the first would
        // lose the first report's action, and with it the only route back to that report.
        var firstReport = CreateContent("Could not delete 3 resources") with
        {
            Action = new OpenDocumentAction(new ResourceKey("logs:reports/delete-resources.report"), "View Report")
        };
        var secondReport = CreateContent("Could not delete 3 resources") with
        {
            Action = new OpenDocumentAction(new ResourceKey("logs:reports/delete-resources-2.report"), "View Report")
        };

        _centre.AddEvent(firstReport);
        _centre.AddEvent(secondReport);

        _centre.Notifications.Should().HaveCount(2);
    }

    [Test]
    public void AFullList_DropsTheOldestEvent()
    {
        _centre.RecordCondition("project-load", CreateContent("The project loaded with 3 issues"));

        for (var index = 0; index < NotificationCentre.NotificationLimit; index++)
        {
            _centre.AddEvent(CreateContent($"event {index}"));
        }

        var notifications = _centre.Notifications;
        notifications.Should().HaveCount(NotificationCentre.NotificationLimit);

        // The condition stands for something still true of the project, so it is never what makes room.
        notifications[0].Kind.Should().Be(NotificationKind.Condition);
        notifications[1].Content.Message.Should().Be($"event {NotificationCentre.NotificationLimit - 1}");
        notifications.Should().NotContain(notification => notification.Content.Message == "event 0");
    }

    [Test]
    public void RecordingAConditionAgain_ReplacesTheSourcesEarlierCondition()
    {
        _centre.RecordCondition("project-load", CreateContent("The project loaded with 3 issues", ReportSeverity.Warning));
        _centre.RecordCondition("project-load", CreateContent("The project loaded with 1 issue", ReportSeverity.Error));

        var notification = _centre.Notifications.Should().ContainSingle().Subject;
        notification.Content.Message.Should().Be("The project loaded with 1 issue");
        notification.Content.Severity.Should().Be(ReportSeverity.Error);
    }

    [Test]
    public void RecordingTheSameConditionAgain_IsNotAnArrival()
    {
        _centre.RecordCondition("project-load", CreateContent("The project loaded with 3 issues"));
        _messengerService.ClearReceivedCalls();

        _centre.RecordCondition("project-load", CreateContent("The project loaded with 3 issues"));

        _centre.Notifications.Should().ContainSingle();
        _messengerService.DidNotReceive().Send(Arg.Any<NotificationsChangedMessage>());
    }

    [Test]
    public void Dismiss_RemovesTheEvent()
    {
        _centre.AddEvent(CreateContent("first event"));
        _centre.AddEvent(CreateContent("second event"));
        var firstEvent = _centre.Notifications.Single(notification => notification.Content.Message == "first event");

        _messengerService.ClearReceivedCalls();

        _centre.Dismiss(firstEvent.Id);

        _centre.Notifications.Should().ContainSingle()
            .Which.Content.Message.Should().Be("second event");
        _messengerService.Received(1).Send(
            Arg.Is<NotificationsChangedMessage>(message => !message.HasArrival));
    }

    [Test]
    public void Dismiss_LeavesACondition()
    {
        // Dismissing a condition would claim something about the project that is not true.
        _centre.RecordCondition("project-load", CreateContent("The project loaded with 3 issues"));
        var condition = _centre.Notifications[0];

        _messengerService.ClearReceivedCalls();

        _centre.Dismiss(condition.Id);

        _centre.Notifications.Should().ContainSingle();
        _messengerService.DidNotReceive().Send(Arg.Any<NotificationsChangedMessage>());
    }

    [Test]
    public void ClearEvents_LeavesTheConditions()
    {
        _centre.RecordCondition("project-load", CreateContent("The project loaded with 3 issues"));
        _centre.AddEvent(CreateContent("first event"));
        _centre.AddEvent(CreateContent("second event"));

        _centre.ClearEvents();

        _centre.Notifications.Should().ContainSingle()
            .Which.Kind.Should().Be(NotificationKind.Condition);
    }

    [Test]
    public void Clear_RemovesEverything()
    {
        _centre.RecordCondition("project-load", CreateContent("The project loaded with 3 issues"));
        _centre.AddEvent(CreateContent("first event"));

        _messengerService.ClearReceivedCalls();

        _centre.Clear();

        _centre.Notifications.Should().BeEmpty();
        _messengerService.Received(1).Send(
            Arg.Is<NotificationsChangedMessage>(message => !message.HasArrival));
    }

    [Test]
    public void Clear_WithNothingPending_AnnouncesNothing()
    {
        _centre.Clear();

        _messengerService.DidNotReceive().Send(Arg.Any<NotificationsChangedMessage>());
    }

    [Test]
    public void AListAlreadyHandedOut_IsNotChangedByALaterArrival()
    {
        // The badge reads the list on the UI thread while a source records on another, so the list a reader
        // holds must stay as it was handed out.
        _centre.AddEvent(CreateContent("first event"));
        var notifications = _centre.Notifications;

        _centre.AddEvent(CreateContent("second event"));

        notifications.Should().ContainSingle();
    }

    private static NotificationContent CreateContent(string message, ReportSeverity severity = ReportSeverity.Warning)
    {
        return new NotificationContent(severity, message);
    }
}
