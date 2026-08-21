using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers the workspace panel view model's unload path, which runs after the workspace has been torn
/// down: the application shell tears the workspace down while its view is still in the visual tree, so
/// Unloaded reaches the view model once the workspace service is already gone.
/// </summary>
[TestFixture]
public class WorkspacePanelViewModelTests
{
    private IMessengerService _messengerService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private WorkspacePanelViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();

        var bindableSettings = Substitute.For<IBindableWorkspaceSettings>();

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(Substitute.For<IDocumentsService>());
        workspaceService.BindableWorkspaceSettings.Returns(bindableSettings);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
        _workspaceWrapper.HasWorkspaceService.Returns(true);

        _viewModel = new WorkspacePanelViewModel(
            _messengerService,
            Substitute.For<ICommandService>(),
            Substitute.For<ILayoutService>(),
            _workspaceWrapper);
    }

    [Test]
    public void OnViewUnloaded_WithLiveWorkspace_ReachesTheSettingsToUnsubscribe()
    {
        _viewModel.OnViewUnloaded();

        // The guard must not skip the unsubscribe while the workspace is still there to unsubscribe from.
        _ = _workspaceWrapper.Received().WorkspaceService;
    }

    [Test]
    public void OnViewUnloaded_AfterWorkspaceTeardown_DoesNotThrow()
    {
        // The workspace is disposed before its view leaves the visual tree, so asking the wrapper for the
        // workspace service here would throw rather than return it.
        _workspaceWrapper.HasWorkspaceService.Returns(false);
        _workspaceWrapper.WorkspaceService.Returns(
            _ => throw new InvalidOperationException("Failed to acquire workspace because no workspace is loaded"));

        var unload = () => _viewModel.OnViewUnloaded();

        unload.Should().NotThrow();
    }
}
