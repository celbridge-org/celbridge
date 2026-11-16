using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Views.Controls;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers Project Settings in Safe Mode. The error panel shows when the project file failed to load, and says that
/// most features are off. The error panel offers Trust Project while the project is untrusted, and Reload Project
/// otherwise. The Safe Mode section shows only in Safe Mode, comes first, and opens first. The section offers Trust
/// Project.
/// </summary>
[TestFixture]
public class ProjectSettingsEditorViewModelTests
{
    private IServiceProvider? _previousServiceProvider;
    private ISettingsService _settingsService = null!;
    private ICommandService _commandService = null!;

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

        _settingsService = Substitute.For<ISettingsService>();
        _settingsService.IsScopeAvailable(SettingScope.Workspace).Returns(true);
        _commandService = Substitute.For<ICommandService>();
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
    public void AProjectFileThatFailedToLoad_ShowsTheErrorPanelInSafeModeWithReloadProject()
    {
        var viewModel = CreateViewModel(isTrusted: true, configIsHealthy: false);

        viewModel.HasConfigError.Should().BeTrue();
        viewModel.HasSections.Should().BeFalse();
        viewModel.IsSafeMode.Should().BeTrue();
        viewModel.IsTrusted.Should().BeTrue();
    }

    [Test]
    public void AnUntrustedProjectWhoseFileFailedToLoad_OffersTrustProjectInTheErrorPanel()
    {
        var viewModel = CreateViewModel(isTrusted: false, configIsHealthy: false);

        viewModel.HasConfigError.Should().BeTrue();
        viewModel.IsTrusted.Should().BeFalse();
    }

    [Test]
    public void AProjectThatLoadedCleanly_ShowsTheSections()
    {
        var viewModel = CreateViewModel(isTrusted: true, configIsHealthy: true);

        viewModel.HasConfigError.Should().BeFalse();
        viewModel.HasSections.Should().BeTrue();
        viewModel.IsSafeMode.Should().BeFalse();
    }

    [Test]
    public void InSafeMode_TheSafeModeSectionComesFirstAndOpensFirst()
    {
        _settingsService.Get(SettingCatalog.Layout.ProjectSettingsSelectedSection).Returns("Packages");
        var viewModel = CreateViewModel(isTrusted: false, configIsHealthy: true);

        viewModel.InitializeSections(CreateSections());

        viewModel.Sections.Select(section => section.Key).Should().Equal(
            ProjectSettingsEditorViewModel.SafeModeSectionKey,
            "Information",
            "Packages");
        viewModel.SelectedSection!.Key.Should().Be(ProjectSettingsEditorViewModel.SafeModeSectionKey);
        viewModel.IsTrusted.Should().BeFalse();
    }

    [Test]
    public void OutsideSafeMode_TheSafeModeSectionIsLeftOut_AndTheSectionShownLastOpens()
    {
        _settingsService.Get(SettingCatalog.Layout.ProjectSettingsSelectedSection).Returns("Packages");
        var viewModel = CreateViewModel(isTrusted: true, configIsHealthy: true);

        viewModel.InitializeSections(CreateSections());

        viewModel.Sections.Select(section => section.Key).Should().Equal("Information", "Packages");
        viewModel.SelectedSection!.Key.Should().Be("Packages");
    }

    [Test]
    public void InSafeMode_TheSafeModeSectionIsNeverStoredAsTheSectionShownLast()
    {
        var viewModel = CreateViewModel(isTrusted: false, configIsHealthy: true);
        viewModel.InitializeSections(CreateSections());

        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Key == "Information");
        viewModel.SelectedSection = viewModel.Sections.Single(
            section => section.Key == ProjectSettingsEditorViewModel.SafeModeSectionKey);

        _settingsService.Received(1).Set(SettingCatalog.Layout.ProjectSettingsSelectedSection, "Information");
        _settingsService.DidNotReceive().Set(
            SettingCatalog.Layout.ProjectSettingsSelectedSection,
            ProjectSettingsEditorViewModel.SafeModeSectionKey);
    }

    [Test]
    public async Task TrustProject_RunsTheTrustProjectCommand()
    {
        var viewModel = CreateViewModel(isTrusted: false, configIsHealthy: true);

        await ((IAsyncRelayCommand)viewModel.TrustProjectCommand).ExecuteAsync(null);

        _commandService.Received(1).Execute(
            Arg.Any<Action<ITrustProjectCommand>?>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    [Test]
    public async Task ReloadProject_RunsTheReloadProjectCommand()
    {
        var viewModel = CreateViewModel(isTrusted: true, configIsHealthy: false);

        await ((IAsyncRelayCommand)viewModel.ReloadProjectCommand).ExecuteAsync(null);

        _commandService.Received(1).Execute(
            Arg.Any<Action<IReloadProjectCommand>?>(),
            Arg.Any<string>(),
            Arg.Any<int>());
    }

    // The tests never show a section's content, so each section's view is a plain object.
    private static List<SettingsSection> CreateSections()
    {
        return new List<SettingsSection>
        {
            new(ProjectSettingsEditorViewModel.SafeModeSectionKey, "", "Safe Mode", "", new object()),
            new("Information", "", "Information", "", new object()),
            new("Packages", "", "Packages", "", new object()),
        };
    }

    private ProjectSettingsEditorViewModel CreateViewModel(bool isTrusted, bool configIsHealthy)
    {
        var project = Substitute.For<IProject>();
        project.IsTrusted.Returns(isTrusted);
        project.ConfigIsHealthy.Returns(configIsHealthy);
        project.Config.Returns(new ProjectConfig());

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        // The Safe Mode service and the view model read the same project.
        var workspaceService = Substitute.For<IWorkspaceService>();
        var safeModeService = TestSafeMode.Create(isTrusted, configIsHealthy);
        workspaceService.SafeModeService.Returns(safeModeService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        return new ProjectSettingsEditorViewModel(
            new NullLogger<ProjectSettingsEditorViewModel>(),
            projectService,
            _commandService,
            workspaceWrapper,
            Substitute.For<ILocalFileSystem>(),
            Substitute.For<IStringLocalizer>(),
            _settingsService,
            Substitute.For<IPackageLocalizationService>(),
            Substitute.For<IFileTypeCatalog>(),
            Substitute.For<IIconService>(),
            Substitute.For<IDialogService>(),
            Substitute.For<IProjectLoadReporter>());
    }
}
