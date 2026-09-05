using Celbridge.Messaging;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Workspace;
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
    private MessageHandler<object, EditorNotificationMessage>? _editorHandler;
    private MessageHandler<object, WorkspaceItemSaveFailedMessage>? _saveFailureHandler;

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

        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, EditorNotificationMessage>>()))
            .Do(call => _editorHandler = call.Arg<MessageHandler<object, EditorNotificationMessage>>());

        _messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, WorkspaceItemSaveFailedMessage>>()))
            .Do(call => _saveFailureHandler = call.Arg<MessageHandler<object, WorkspaceItemSaveFailedMessage>>());

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
    public void AOneResourceFailure_NamesItAndItsReasonWithNoReportAction()
    {
        // One failure is fully expressed by the line, so the operation wrote no report for the action
        // to open.
        SendOperationFailure(ResourceOperationType.Delete, "notes.txt");

        _viewModel.ToastMessage.Should().Contain("notes.txt");
        _viewModel.ToastMessage.Should().Contain("the file is locked");
        _viewModel.IsActionVisible.Should().BeFalse();
    }

    [Test]
    public void AOneResourceFailure_ShowsOnlyTheFirstLineOfItsReason()
    {
        // A failure reason is an outer-first chain over several lines. The rest of it is what the
        // report the reader can open is for.
        var failedResources = new List<FailedResource>
        {
            new FailedResource(new ResourceKey("project:notes.txt"), "Could not delete the file\nThe process cannot access the file")
        };

        SendOperationFailure(ResourceOperationType.Delete, failedResources, ResourceKey.Empty);

        _viewModel.ToastMessage.Should().Contain("Could not delete the file");
        _viewModel.ToastMessage.Should().NotContain("The process cannot access the file");
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

        _viewModel.ToastMessage.Should().Contain("Toast_OperationFailed_Delete_Multiple");
        _viewModel.ToastMessage.Should().Contain("2");
        _viewModel.IsActionVisible.Should().BeTrue();
    }

    [Test]
    public void AMoveThatOnlyLeftReferencesStale_IsAWarningNotAFailure()
    {
        // The move ran; what it could not finish was rewriting the references into what it moved.
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

        _viewModel.ToastSeverity.Should().Be(InfoBarSeverity.Warning);
        _viewModel.ToastMessage.Should().Contain("Toast_ReferencesNotUpdated_Many");
        _viewModel.IsActionVisible.Should().BeTrue();
    }

    [Test]
    public void TheAction_OpensItsDocumentAndDismissesTheToast()
    {
        SendLoadNotification(ReportSeverity.Warning, issueCount: 3);

        _viewModel.OnActionClicked();

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

    [Test]
    public void AnEditorNotification_ShowsTheTextTheEditorWrote()
    {
        // The editor resolved its own text, so nothing composes it from a localization key here.
        var message = new EditorNotificationMessage(ReportSeverity.Warning, "9 of 40 tilesets failed to convert")
        {
            Action = new OpenDocumentAction(new ResourceKey("logs:reports/acme-tiles-convert.report"))
        };

        _editorHandler!.Invoke(this, message);

        _viewModel.ToastSeverity.Should().Be(InfoBarSeverity.Warning);
        _viewModel.ToastMessage.Should().Be("9 of 40 tilesets failed to convert");
        _viewModel.IsActionVisible.Should().BeTrue();
    }

    [Test]
    public void AnEditorNotification_IsCutToOneLine()
    {
        var message = new EditorNotificationMessage(ReportSeverity.Error, "Conversion failed\nsprites/hero.png: unsupported bit depth");

        _editorHandler!.Invoke(this, message);

        _viewModel.ToastMessage.Should().Be("Conversion failed");
    }

    [Test]
    public void AnEditorWarning_DoesNotDisplaceAnError()
    {
        // A contribution gets the same replacement policy the host's own notifications get.
        SendOperationFailure(ResourceOperationType.Delete, "notes.txt");

        _editorHandler!.Invoke(this, new EditorNotificationMessage(ReportSeverity.Warning, "conversion finished with warnings"));

        _viewModel.ToastSeverity.Should().Be(InfoBarSeverity.Error);
    }

    [Test]
    public void AnEditorAction_CarriesItsOwnLabel()
    {
        // The editor's action can open anything, so it says what it opens rather than inheriting the
        // host's wording for a report.
        var message = new EditorNotificationMessage(ReportSeverity.Error, "config.json has a syntax error")
        {
            Action = new OpenDocumentAction(new ResourceKey("project:config.json"), "Open config.json", 42, 7)
        };

        _editorHandler!.Invoke(this, message);

        _viewModel.IsActionVisible.Should().BeTrue();
        _viewModel.ActionLabel.Should().Be("Open config.json");
    }

    [Test]
    public void AHostNotification_LabelsItsActionForTheReportItOpens()
    {
        SendLoadNotification(ReportSeverity.Warning, issueCount: 3);

        _viewModel.ActionLabel.Should().Be("Toast_ViewReportButton");
    }

    [Test]
    public void AOneItemSaveFailure_NamesItAndItsReasonWithNoReportAction()
    {
        // An auto-save the user did not ask for, so it reports as a notification rather than a dialog.
        SendSaveFailure(new FailedResource(new ResourceKey("project:notes.txt"), "the file is locked"));

        _viewModel.ToastSeverity.Should().Be(InfoBarSeverity.Error);
        _viewModel.ToastMessage.Should().Contain("notes.txt");
        _viewModel.ToastMessage.Should().Contain("the file is locked");
        _viewModel.IsActionVisible.Should().BeFalse();
    }

    [Test]
    public void SeveralItemSaveFailures_CountThemWithNoReportAction()
    {
        // Several failing at once share one systemic cause, so a report would repeat the same reason per
        // row and the count is what the line has to say.
        SendSaveFailure(
            new FailedResource(new ResourceKey("project:notes.txt"), "the file is locked"),
            new FailedResource(new ResourceKey("project:data.json"), "the file is locked"));

        _viewModel.ToastMessage.Should().Contain("Toast_SaveFailed_Multiple");
        _viewModel.ToastMessage.Should().Contain("2");
        _viewModel.IsActionVisible.Should().BeFalse();
    }

    private void SendSaveFailure(params FailedResource[] failedItems)
    {
        _saveFailureHandler!.Invoke(this, new WorkspaceItemSaveFailedMessage(failedItems));
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
