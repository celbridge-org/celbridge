using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers the error panel in Project Settings, shown when the project file failed to load. The project is then
/// in Safe Mode, so the panel says that most features are off and offers Reload Project.
/// </summary>
[TestFixture]
public class ProjectSettingsEditorViewModelTests
{
    private IServiceProvider? _previousServiceProvider;

    [SetUp]
    public void Setup()
    {
        // Some sections acquire their services through the global ServiceLocator. The fixture restores the
        // ServiceLocator after each test.
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IFeatureFlags>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        _previousServiceProvider = ServiceLocator.ServiceProvider;
        ServiceLocator.Initialize(services.BuildServiceProvider());
    }

    [TearDown]
    public void TearDown()
    {
        if (_previousServiceProvider is not null)
        {
            ServiceLocator.Initialize(_previousServiceProvider);
        }
        else
        {
            ServiceLocator.Reset();
        }
    }

    [Test]
    public void AProjectFileThatFailedToLoad_ShowsTheErrorPanelInSafeMode()
    {
        var viewModel = CreateViewModel(configIsHealthy: false);

        viewModel.HasConfigError.Should().BeTrue();
        viewModel.HasSections.Should().BeFalse();
        viewModel.IsSafeMode.Should().BeTrue();
        viewModel.ReloadProjectCommand.Should().NotBeNull();
    }

    [Test]
    public void AProjectThatLoadedCleanly_ShowsTheSections()
    {
        var viewModel = CreateViewModel(configIsHealthy: true);

        viewModel.HasConfigError.Should().BeFalse();
        viewModel.HasSections.Should().BeTrue();
        viewModel.IsSafeMode.Should().BeFalse();
    }

    private static ProjectSettingsEditorViewModel CreateViewModel(bool configIsHealthy)
    {
        var project = Substitute.For<IProject>();
        project.ConfigIsHealthy.Returns(configIsHealthy);
        project.Config.Returns(new ProjectConfig());

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        // The Safe Mode service and the view model read the same project.
        var workspaceService = Substitute.For<IWorkspaceService>();
        var safeModeService = TestSafeMode.Create(isActive: !configIsHealthy);
        workspaceService.SafeModeService.Returns(safeModeService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        return new ProjectSettingsEditorViewModel(
            new NullLogger<ProjectSettingsEditorViewModel>(),
            projectService,
            Substitute.For<ICommandService>(),
            workspaceWrapper,
            Substitute.For<ILocalFileSystem>(),
            Substitute.For<IStringLocalizer>(),
            Substitute.For<ISettingsService>(),
            Substitute.For<IPackageLocalizationService>(),
            Substitute.For<IFileTypeCatalog>(),
            Substitute.For<IIconService>(),
            Substitute.For<IDialogService>(),
            Substitute.For<IProjectLoadReporter>());
    }
}
