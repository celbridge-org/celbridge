using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Notifications;
using Celbridge.Reports;
using Celbridge.Tests.Localization;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests for what the notification badge states and what the list it opens does with each entry. The badge
/// is the only surface the notifications have, so its summary has to stand in for the list until it is
/// opened.
/// </summary>
[TestFixture]
public class NotificationBadgeViewModelTests
{
    private IMessengerService _messengerService = null!;
    private IDispatcher _dispatcher = null!;
    private ICommandService _commandService = null!;
    private IOpenDocumentCommand _openDocumentCommand = null!;

    private MessageHandler<object, NotificationsChangedMessage>? _changedHandler;

    private NotificationCentre _centre = null!;
    private NotificationBadgeViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();

        // Deliver what the centre sends straight to the view model, as the live messenger would.
        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, NotificationsChangedMessage>>()))
            .Do(call => _changedHandler = call.Arg<MessageHandler<object, NotificationsChangedMessage>>());

        _messengerService
            .When(service => service.Send(Arg.Any<NotificationsChangedMessage>()))
            .Do(call => _changedHandler?.Invoke(this, call.Arg<NotificationsChangedMessage>()));

        // The view model marshals onto the UI thread. Run inline so the assertions see the result.
        _dispatcher = Substitute.For<IDispatcher>();
        _dispatcher.TryEnqueue(Arg.Any<Action>()).Returns(call =>
        {
            call.Arg<Action>().Invoke();
            return true;
        });

        _openDocumentCommand = Substitute.For<IOpenDocumentCommand>();
        _commandService = Substitute.For<ICommandService>();
        _commandService
            .When(service => service.Execute(
                Arg.Any<Action<IOpenDocumentCommand>>(),
                Arg.Any<string>(),
                Arg.Any<int>()))
            .Do(call => call.Arg<Action<IOpenDocumentCommand>>().Invoke(_openDocumentCommand));

        _centre = new NotificationCentre(_messengerService);

        _viewModel = new NotificationBadgeViewModel(
            _messengerService,
            _dispatcher,
            new TestLocalizerService(),
            _centre,
            _commandService);

        _viewModel.OnLoaded();
    }

    [Test]
    public void NothingPending_StatesNothing()
    {
        _viewModel.Notifications.Should().BeEmpty();
        _viewModel.Summary.Should().BeEmpty();
        _viewModel.HasEvents.Should().BeFalse();
    }

    [Test]
    public void ALoneNotification_IsSummarisedByItsOwnLine()
    {
        RecordLoadCondition(ReportSeverity.Warning);

        _viewModel.Summary.Should().Be("The project loaded with 3 issues");
    }

    [Test]
    public void SeveralNotifications_AreCountedBySeverity()
    {
        _centre.AddEvent(new NotificationContent(ReportSeverity.Error, "Could not delete 'notes.txt'"));
        _centre.AddEvent(new NotificationContent(ReportSeverity.Warning, "first warning"));
        _centre.AddEvent(new NotificationContent(ReportSeverity.Warning, "second warning"));
        _centre.AddEvent(new NotificationContent(ReportSeverity.Info, "Conversion finished"));

        _viewModel.Summary.Should().Be("1 error. 2 warnings. 1 message.");
    }

    [Test]
    public void TheBadgeSeverity_IsTheMostSeriousPending()
    {
        _centre.AddEvent(new NotificationContent(ReportSeverity.Info, "Conversion finished"));
        _viewModel.Severity.Should().Be(ReportSeverity.Info);

        _centre.AddEvent(new NotificationContent(ReportSeverity.Error, "Could not delete 'notes.txt'"));
        _centre.AddEvent(new NotificationContent(ReportSeverity.Warning, "a later warning"));

        _viewModel.Severity.Should().Be(ReportSeverity.Error);
    }

    [Test]
    public void PerformAction_OpensTheDocument_AndLeavesTheNotificationPending()
    {
        var action = new OpenDocumentAction(new ResourceKey("project:config.json"), "Open config.json", 42, 7);
        _centre.AddEvent(new NotificationContent(ReportSeverity.Error, "config.json has a syntax error")
        {
            Action = action
        });

        _viewModel.PerformAction(action);

        _openDocumentCommand.FileResource.Should().Be(new ResourceKey("project:config.json"));
        _openDocumentCommand.Location.Should().NotBeEmpty();

        // What the notification reports is not resolved by opening it, and the report may have no other route.
        _viewModel.Notifications.Should().ContainSingle();
    }

    [Test]
    public void DismissAndClearEvents_ActOnTheCentre()
    {
        RecordLoadCondition(ReportSeverity.Warning);
        _centre.AddEvent(new NotificationContent(ReportSeverity.Error, "first event"));
        _centre.AddEvent(new NotificationContent(ReportSeverity.Error, "second event"));

        _viewModel.HasEvents.Should().BeTrue();

        var secondEvent = _viewModel.Notifications.Single(notification => notification.Content.Message == "second event");
        _viewModel.Dismiss(secondEvent);

        _viewModel.Notifications.Should().HaveCount(2);

        _viewModel.ClearEvents();

        _viewModel.Notifications.Should().ContainSingle()
            .Which.Kind.Should().Be(NotificationKind.Condition);
        _viewModel.HasEvents.Should().BeFalse();
    }

    [Test]
    public void AnArrival_IsRaisedAfterTheChange_AndADismissalIsNot()
    {
        var raised = new List<string>();
        _viewModel.NotificationsChanged += (_, _) => raised.Add("changed");
        _viewModel.NotificationArrived += (_, _) => raised.Add("arrived");

        _centre.AddEvent(new NotificationContent(ReportSeverity.Error, "Could not delete 'notes.txt'"));

        raised.Should().Equal("changed", "arrived");

        raised.Clear();
        _centre.Dismiss(_viewModel.Notifications[0].Id);

        raised.Should().Equal("changed");
    }

    private void RecordLoadCondition(ReportSeverity severity)
    {
        var content = new NotificationContent(severity, "The project loaded with 3 issues")
        {
            Action = new OpenDocumentAction(new ResourceKey("logs:reports/project-load.report"), "View Report")
        };

        _centre.RecordCondition("project-load", content);
    }
}
