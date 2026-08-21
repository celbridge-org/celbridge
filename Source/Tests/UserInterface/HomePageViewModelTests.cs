using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.FileSystem;
using Celbridge.Projects;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Pages;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers which recent projects list Home asks for. Home is built by the application shell part way
/// through a project unload, while the project being closed is still the current one, so asking for the
/// list that drops the current project would hide the project the user just closed.
/// </summary>
[TestFixture]
public class HomePageViewModelTests
{
    private const string RecentProjectPath = @"C:\Projects\Recent\Recent.celbridge";

    private IProjectService _projectService = null!;

    [SetUp]
    public void Setup()
    {
        _projectService = Substitute.For<IProjectService>();
        _projectService.GetRecentProjects(Arg.Any<bool>())
            .Returns(new List<RecentProject> { new(RecentProjectPath) });
    }

    [Test]
    public void RecentProjects_AreReadWithoutExcludingTheProjectBeingClosed()
    {
        var viewModel = CreateViewModel();

        _projectService.Received(1).GetRecentProjects(excludeCurrentProject: false);
        _projectService.DidNotReceive().GetRecentProjects(excludeCurrentProject: true);

        viewModel.RecentProjects.Select(recentProject => recentProject.ProjectFilePath)
            .Should().Equal(RecentProjectPath);
    }

    private HomePageViewModel CreateViewModel()
    {
        var dialogService = Substitute.For<IDialogService>();
        var filePickerService = Substitute.For<IFilePickerService>();
        var commandService = Substitute.For<ICommandService>();

        var mainMenuUtils = new MainMenuUtils(
            dialogService,
            filePickerService,
            commandService);

        return new HomePageViewModel(
            new NullLogger<HomePageViewModel>(),
            commandService,
            _projectService,
            filePickerService,
            dialogService,
            Substitute.For<ILocalFileSystem>(),
            mainMenuUtils);
    }
}
