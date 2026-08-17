using Celbridge.Messaging;
using Celbridge.Commands;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.WorkspaceUI.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// The workspace shows one notification at a time until the user dismisses it. These tests pin the
/// replacement policy that makes that safe: a newer notification takes the screen, except that nothing
/// less serious than an error displaces an error the user has not dealt with yet.
/// </summary>
[TestFixture]
public class WorkspaceToastViewModelTests
{
    private IMessengerService _messengerService = null!;
    private IDispatcher _dispatcher = null!;
    private IStringLocalizer _stringLocalizer = null!;
    private ICommandService _commandService = null!;

    private MessageHandler<object, ProjectLoadNotificationMessage>? _loadHandler;
    private MessageHandler<object, ResourceOperationFailedMessage>? _operationHandler;

    private WorkspaceToastViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();
        _dispatcher = Substitute.For<IDispatcher>();
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _commandService = Substitute.For<ICommandService>();

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

        // The view model marshals onto the UI thread; run inline so the assertions see the result.
        _dispatcher.TryEnqueue(Arg.Any<Action>()).Returns(call =>
        {
            call.Arg<Action>().Invoke();
            return true;
        });

        _stringLocalizer[Arg.Any<string>()].Returns(call =>
            new LocalizedString(call.Arg<string>(), call.Arg<string>()));

        _stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(call =>
        {
            var key = call.Arg<string>();
            var arguments = string.Join(",", call.Arg<object[]>());

            return new LocalizedString(key, $"{key}:{arguments}");
        });

        _viewModel = new WorkspaceToastViewModel(
            _messengerService,
            _dispatcher,
            _stringLocalizer,
            _commandService);
    }

    [Test]
    public void ALoadWithFindings_ShowsOneToastPointingAtItsReport()
    {
        SendLoadNotification(ReportSeverity.Warning, issueCount: 3);

        _viewModel.IsToastVisible.Should().BeTrue();
        _viewModel.ToastSeverity.Should().Be(InfoBarSeverity.Warning);
        _viewModel.IsActionVisible.Should().BeTrue();
    }

    [Test]
    public void ANewerNotification_ReplacesTheCurrentOne()
    {
        SendLoadNotification(ReportSeverity.Warning, issueCount: 3);
        SendOperationFailure(ResourceOperationType.Delete, "notes.txt");

        _viewModel.ToastSeverity.Should().Be(InfoBarSeverity.Error);
        _viewModel.ToastMessage.Should().Contain("Toast_OperationFailed_Delete_Single");
    }

    [Test]
    public void ALowerSeverity_DoesNotDisplaceAnError()
    {
        // An error is a failure the user has not acknowledged. Something completing behind it does not
        // get to take the screen away from it.
        SendOperationFailure(ResourceOperationType.Delete, "notes.txt");
        SendLoadNotification(ReportSeverity.Warning, issueCount: 3);

        _viewModel.ToastSeverity.Should().Be(InfoBarSeverity.Error);
        _viewModel.ToastMessage.Should().Contain("Toast_OperationFailed_Delete_Single");
    }

    [Test]
    public void AnError_IsReplacedByAnotherError()
    {
        SendOperationFailure(ResourceOperationType.Delete, "notes.txt");
        SendOperationFailure(ResourceOperationType.Move, "data.json");

        _viewModel.ToastMessage.Should().Contain("Toast_OperationFailed_Move_Single");
    }

    [Test]
    public void ANotification_StaysUntilItIsDismissed()
    {
        // Nothing times out: the user may not have been at the machine when it appeared, and a
        // notification they never saw is one they cannot act on.
        SendLoadNotification(ReportSeverity.Warning, issueCount: 1);

        _viewModel.IsToastVisible.Should().BeTrue();

        _viewModel.OnToastDismissed();

        _viewModel.IsToastVisible.Should().BeFalse();
    }

    [Test]
    public void ADismissedToast_LetsALowerSeverityThroughAgain()
    {
        SendOperationFailure(ResourceOperationType.Delete, "notes.txt");
        _viewModel.OnToastDismissed();

        SendLoadNotification(ReportSeverity.Warning, issueCount: 2);

        _viewModel.IsToastVisible.Should().BeTrue();
        _viewModel.ToastSeverity.Should().Be(InfoBarSeverity.Warning);
    }

    [Test]
    public void AnOperationFailure_OffersNoReportAction()
    {
        // Resource operations do not write a report yet, so there is nothing for the action to open.
        SendOperationFailure(ResourceOperationType.Delete, "notes.txt");

        _viewModel.IsActionVisible.Should().BeFalse();
    }

    [Test]
    public void ViewReport_OpensTheReportAndDismissesTheToast()
    {
        SendLoadNotification(ReportSeverity.Warning, issueCount: 3);

        _viewModel.OnViewReportClicked();

        _commandService.Received(1).Execute(
            Arg.Any<Action<IOpenDocumentCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());

        _viewModel.IsToastVisible.Should().BeFalse();
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
        var message = new ResourceOperationFailedMessage(operationType, failedItems.ToList());

        _operationHandler!.Invoke(this, message);
    }
}
