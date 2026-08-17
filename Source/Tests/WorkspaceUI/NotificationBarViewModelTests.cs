using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.WorkspaceUI.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Tests that the notification bar shows one banner at a time without losing the conditions behind it, and
/// that a notification with a report behind it offers a way into it.
/// </summary>
[TestFixture]
public class NotificationBarViewModelTests
{
    private IMessengerService _messengerService = null!;
    private IDispatcher _dispatcher = null!;
    private IStringLocalizer _stringLocalizer = null!;
    private IProjectService _projectService = null!;
    private ICommandService _commandService = null!;
    private MessageHandler<object, ProjectErrorMessage>? _projectErrorHandler;

    [SetUp]
    public void Setup()
    {
        _messengerService = Substitute.For<IMessengerService>();

        // The substitute records registrations rather than dispatching, so capture the view model's handler
        // for the tests to raise messages through.
        _projectErrorHandler = null;
        _messengerService
            .When(messenger => messenger.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, ProjectErrorMessage>>()))
            .Do(callInfo => _projectErrorHandler =
                callInfo.Arg<MessageHandler<object, ProjectErrorMessage>>());

        // Run enqueued actions synchronously so a raised message reaches the banner inline.
        _dispatcher = Substitute.For<IDispatcher>();
        _dispatcher.TryEnqueue(Arg.Any<Action>())
            .Returns(callInfo =>
            {
                callInfo.Arg<Action>().Invoke();
                return true;
            });

        // Resolve every string to its own key, so a test can name the banner it expects.
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer[Arg.Any<string>()]
            .Returns(callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));
        _stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()]
            .Returns(callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        // No project loaded, so the migration banner these tests are not about never appears.
        _projectService = Substitute.For<IProjectService>();
        _projectService.CurrentProject.Returns((IProject?)null);

        _commandService = Substitute.For<ICommandService>();
    }

    [Test]
    public void SecondNotification_IsShownOnceTheFirstIsDismissed()
    {
        var viewModel = CreateViewModel();

        RaiseProjectError(new ProjectErrorMessage(ProjectErrorType.ProjectCheckError, string.Empty));
        RaiseProjectError(new ProjectErrorMessage(ProjectErrorType.ProjectConfigEntryError, "project.celbridge"));

        viewModel.BannerTitle.Should().Be("NotificationBar_ProjectCheckFindingsTitle");

        viewModel.OnBannerDismissed();

        viewModel.IsBannerVisible.Should().BeTrue();
        viewModel.BannerTitle.Should().Be("NotificationBar_ProjectConfigEntryErrorTitle");
    }

    [Test]
    public void DismissingTheLastNotification_HidesTheBanner()
    {
        var viewModel = CreateViewModel();

        RaiseProjectError(new ProjectErrorMessage(ProjectErrorType.ProjectCheckError, string.Empty));

        viewModel.OnBannerDismissed();

        viewModel.IsBannerVisible.Should().BeFalse();
    }

    [Test]
    public void NotificationTheUserCannotDismiss_SitsBehindTheOnesTheyCan()
    {
        var viewModel = CreateViewModel();

        RaiseProjectError(new ProjectErrorMessage(ProjectErrorType.MigrationError, "project.celbridge"));
        RaiseProjectError(new ProjectErrorMessage(ProjectErrorType.ProjectCheckError, string.Empty));

        viewModel.BannerTitle.Should().Be("NotificationBar_ProjectCheckFindingsTitle");
        viewModel.IsBannerDismissable.Should().BeTrue();

        viewModel.OnBannerDismissed();

        viewModel.BannerTitle.Should().Be("NotificationBar_MigrationErrorTitle");
        viewModel.IsBannerDismissable.Should().BeFalse();
    }

    [Test]
    public void FindingsWithAReport_OpenItOnTheBannerAction()
    {
        var viewModel = CreateViewModel();

        var reportResource = new ResourceKey("logs:reports/project-load-20260817T101500Z.report");
        var message = new ProjectErrorMessage(ProjectErrorType.ProjectCheckError, string.Empty)
        {
            FindingCount = 3,
            ReportResource = reportResource
        };

        RaiseProjectError(message);

        viewModel.IsActionVisible.Should().BeTrue();
        viewModel.ActionText.Should().Be("NotificationBar_ViewReportButton");

        viewModel.OnActionClicked();

        _commandService.Received(1).Execute<IOpenDocumentCommand>(
            Arg.Any<Action<IOpenDocumentCommand>>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public void FindingsWithoutAReport_OfferNoAction()
    {
        var viewModel = CreateViewModel();

        RaiseProjectError(new ProjectErrorMessage(ProjectErrorType.ProjectCheckError, string.Empty));

        viewModel.IsActionVisible.Should().BeFalse();
    }

    private NotificationBarViewModel CreateViewModel()
    {
        return new NotificationBarViewModel(
            _messengerService,
            _dispatcher,
            _stringLocalizer,
            _projectService,
            _commandService);
    }

    private void RaiseProjectError(ProjectErrorMessage message)
    {
        _projectErrorHandler.Should().NotBeNull();
        _projectErrorHandler!.Invoke(this, message);
    }
}
